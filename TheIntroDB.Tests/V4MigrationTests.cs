using System;
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
    [InlineData("default", null)]
    [InlineData("alternate", "alternate")]
    [InlineData("altdvd", "altdvd")]
    [InlineData("dvd", "dvd")]
    [InlineData("absolute", "absolute")]
    [InlineData("alttwo", "alttwo")]
    [InlineData("regional", "regional")]
    public void DisplayOrderIsSentOnlyWhenItIsNotTheDefaultOrder(string? displayOrder, string? expected)
    {
        var series = new Series { DisplayOrder = displayOrder! };
        Assert.Equal(expected, TheIntroDbSegmentProvider.GetDisplayOrder(series));
    }

    [Fact]
    public void DisplayOrderIsNullWithoutASeries()
    {
        Assert.Null(TheIntroDbSegmentProvider.GetDisplayOrder(null));
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
}
