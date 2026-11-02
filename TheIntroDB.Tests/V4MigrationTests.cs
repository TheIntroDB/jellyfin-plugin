using System;
using System.Linq;
using System.Net;
using MediaBrowser.Controller.Entities.TV;
using TheIntroDB.Api;
using TheIntroDB.Providers;
using Xunit;

namespace TheIntroDB.Tests;

/// <summary>
/// Guards the v4 contract: the client must not silently drift back to a frozen
/// version, the signed-in rate tier must stay under its ceiling, and a series'
/// display order must reach the API only when it is a real non-default order.
/// </summary>
public class V4MigrationTests
{
    private const int ApiMediaAuthenticatedBurstCeiling = 120;

    [Fact]
    public void ClientSpeaksV4()
    {
        Assert.Equal("v4", TheIntroDbClient.ApiVersion);
        Assert.Equal("https://api.theintrodb.org/v4", TheIntroDbClient.BaseUrl);
    }

    [Fact]
    public void AuthenticatedPacingStaysUnderTheSignedInCeiling()
    {
        // Signed-in callers get 120/10s. 100/window keeps a margin for jitter and
        // still lets a 58k-item scan finish in one run.
        Assert.True(
            TheIntroDbClient.MinDelayBetweenRequestsAuthenticated.TotalMilliseconds * (ApiMediaAuthenticatedBurstCeiling - 1) >= 10_000,
            $"Authenticated pacing of {TheIntroDbClient.MinDelayBetweenRequestsAuthenticated.TotalMilliseconds} ms exceeds the ceiling.");
        Assert.True(TheIntroDbClient.MinDelayBetweenRequestsAuthenticated < TheIntroDbClient.MinDelayBetweenRequests);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("official", null)]
    [InlineData("Aired", null)]
    [InlineData("aired-order", null)]
    [InlineData("airdate", null)]
    [InlineData("default", null)]
    [InlineData("alternate", "alternate")]
    [InlineData("altdvd", "dvd")]
    [InlineData("alt-two", "alternate")]
    [InlineData("dvd", "dvd")]
    [InlineData("absolute", "absolute")]
    [InlineData("regional", "regional")]
    public void DisplayOrderIsSentOnlyWhenItIsNotTheDefaultOrder(string? displayOrder, string? expected)
    {
        var series = new Series { DisplayOrder = displayOrder! };
        Assert.Equal(expected, TheIntroDbSegmentProvider.GetDisplayOrder(series, out var unsupported));
        Assert.False(unsupported);
    }

    [Theory]
    [InlineData("originalAirDate")]
    [InlineData("digital")]
    [InlineData("storyArc")]
    [InlineData("production")]
    [InlineData("tv")]
    [InlineData("somethingElse")]
    public void OrdersTheApiHasNoNameForAreReportedAsUnsupported(string displayOrder)
    {
        // Jellyfin's TMDB provider fills Series.DisplayOrder with its episode-group
        // names (TmdbSeriesProvider.MapDisplayOrderToTvGroupType). Sending one as
        // `mapping` is a 400, so the series' episodes are skipped instead.
        var series = new Series { DisplayOrder = displayOrder };
        Assert.Null(TheIntroDbSegmentProvider.GetDisplayOrder(series, out var unsupported));
        Assert.True(unsupported);
    }

    [Fact]
    public void DisplayOrderIsNullWithoutASeries()
    {
        Assert.Null(TheIntroDbSegmentProvider.GetDisplayOrder(null, out var unsupported));
        Assert.False(unsupported);
    }

    [Fact]
    public void TvdbAddressedEpisodeCarriesItsOrderAndEpisodeId()
    {
        // Futurama under Jellyfin's `alternate` display order: S07E01 is TVDB
        // `default` S06E01 (Rebirth). Without mapping the API answers TMDB/default
        // S07E01 — another episode's intro (the 14.6s one from issue #31).
        var query = TheIntroDbClient.BuildMediaQuery(
            tmdbId: 615,
            tvdbId: 73871,
            imdbId: "tt0149460",
            isMovie: false,
            season: 7,
            episode: 1,
            durationMs: 1297000,
            mapping: "alternate",
            tvdbEpisodeId: 1051911);

        Assert.Equal("?tvdb_id=73871&season=7&episode=1&mapping=alternate&tvdb_episode_id=1051911&duration_ms=1297000", query);
    }

    [Fact]
    public void DefaultOrderedEpisodeSendsNoOrderAndNoTmdbId()
    {
        var query = TheIntroDbClient.BuildMediaQuery(456, 71663, null, false, 1, 1, null, null, null);
        Assert.Equal("?tvdb_id=71663&season=1&episode=1", query);
    }

    [Fact]
    public void OrderAndEpisodeIdAreDroppedWithoutATvdbIdentity()
    {
        // A tmdb_id request is always read as TMDB numbering, so an order beside it
        // would be a lie rather than a hint (the provider skips those series).
        var query = TheIntroDbClient.BuildMediaQuery(615, 0, null, false, 7, 1, null, "alternate", 1051911);
        Assert.Equal("?tmdb_id=615&season=7&episode=1", query);
    }

    [Fact]
    public void TmdbIsOnlyUsedWhenNoTvdbIdExistsAndImdbIsLast()
    {
        Assert.Equal("?tmdb_id=615", TheIntroDbClient.BuildMediaQuery(615, 0, "tt0149460", true, null, null, null, null, null));
        Assert.Equal(
            "?imdb_id=tt0149460",
            TheIntroDbClient.BuildMediaQuery(0, 0, "tt0149460", true, null, null, null, null, null));
    }

    [Fact]
    public void UnsupportedCoordinatesAreFinalAndNeverTransient()
    {
        // The provider branches on these flags, so an out-of-range season must not look
        // like a transient error (retried forever) or like a 404.
        var result = MediaFetchResult.Unsupported();
        Assert.True(result.IsUnsupported);
        Assert.False(result.IsError);
        Assert.False(result.IsRateLimited);
        Assert.False(result.IsNotFound);
        Assert.Null(result.Response);
    }

    [Theory]
    [InlineData("{\"error\":\"Invalid season. Must be between 0 and 1000\"}", true)]  // issue #39: S2024
    [InlineData("{\"error\":\"Invalid episode. Must be between 0 and 10000\"}", true)]
    [InlineData("{\"error\":\"invalid season. must be between 0 and 1000\"}", true)]
    [InlineData("{\"error\":\"Invalid season format: abc\"}", false)]                  // a client bug, not a range
    [InlineData("{\"error\":\"Invalid tvdb_id format\"}", false)]
    [InlineData("{\"error\":\"no submissions for this media\"}", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheSeasonOrEpisodeRangeIsClassifiedAsUnsupportedCoordinates(string? body, bool expected)
    {
        // Jellyfin hands a year-based season number straight to the API, which caps a
        // season at 1000. Only that range message is final; every other 400 (a malformed
        // id, an unknown mapping) stays a loud error, because it is our own bug.
        Assert.Equal(expected, TheIntroDbClient.ClassifyErrorResponse(HttpStatusCode.BadRequest, body).IsUnsupported);
        Assert.False(TheIntroDbClient.ClassifyErrorResponse(HttpStatusCode.NotFound, body).IsUnsupported);
        Assert.False(TheIntroDbClient.ClassifyErrorResponse(HttpStatusCode.InternalServerError, body).IsUnsupported);
    }

    [Theory]
    [InlineData(400, "{\"error\":\"Invalid season. Must be between 0 and 1000\"}", "unsupported")]
    [InlineData(400, "{\"error\":\"Invalid season format: \"}", "error")]
    [InlineData(400, "{\"error\":\"Invalid imdb_id format\"}", "error")]
    [InlineData(404, "{\"error\":\"no submissions for this media\",\"code\":\"no_submissions\"}", "not_found")]
    [InlineData(400, "{\"error\":\"no submissions for this media\"}", "error")]
    [InlineData(500, "{\"error\":\"internal\"}", "error")]
    [InlineData(503, "", "error")]
    public void ErrorResponsesKeepTheirOutcome(int statusCode, string body, string expected)
    {
        // The provider retries an error on every scan but suppresses a not-found or an
        // unsupported answer, so a misclassification here either spams or hides a bug.
        var outcome = TheIntroDbClient.ClassifyErrorResponse((HttpStatusCode)statusCode, body);
        var actual = outcome.IsUnsupported ? "unsupported" : outcome.IsNotFound ? "not_found" : outcome.IsError ? "error" : "other";
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NotFoundCacheKeyCarriesTheOrderAndThePinnedEpisodeId()
    {
        // Both change the answer, so neither may be dropped: a 404 recorded for one
        // address must not suppress a later lookup of another, in particular after a
        // series' metadata is corrected.
        var alternate = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 615, 73871, null, 7, 1, "alternate", 1051911);
        var byDefault = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 615, 73871, null, 7, 1, null, 1051911);
        var otherPin = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 615, 73871, null, 7, 1, "alternate", 8234611);
        var noPin = TheIntroDbSegmentProvider.BuildNotFoundKey(false, 615, 73871, null, 7, 1, "alternate", null);

        Assert.Equal("episode:tvdb:73871:7:1:alternate:ep1051911", alternate);
        Assert.Equal(4, new[] { alternate, byDefault, otherPin, noPin }.Distinct().Count());
        Assert.Equal("movie:tvdb:169", TheIntroDbSegmentProvider.BuildNotFoundKey(true, 0, 169, null, null, null, null, null));
    }
}
