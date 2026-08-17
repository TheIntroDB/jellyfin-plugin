using System;
using System.Reflection;
using TheIntroDB.Api;
using Xunit;

namespace TheIntroDB.Tests;

public class RateLimitSafetyTests
{
    [Fact]
    public void RequestPacingKeepsThirtyStartsOutsideTenSecondWindow()
    {
        var field = typeof(TheIntroDbClient).GetField(
            "MinDelayBetweenRequests",
            BindingFlags.NonPublic | BindingFlags.Static);
        var minimumDelay = Assert.IsType<TimeSpan>(field?.GetValue(null));

        Assert.True(
            TimeSpan.FromTicks(minimumDelay.Ticks * 29) >= TimeSpan.FromSeconds(10),
            $"Thirty request starts can fit inside ten seconds with a {minimumDelay.TotalMilliseconds} ms delay.");
    }

    public static TheoryData<bool, long?, long?, bool> RangeCases => new()
    {
        { true, null, 5000L, true },   // intro: null start, end set
        { true, 1000L, 2000L, true },  // intro: normal range
        { true, 1000L, 1000L, false }, // end == start is invalid
        { true, 1000L, null, false },  // intro: missing end is invalid
        { false, 1000L, null, true },  // credits: start set, end optional
        { false, null, 5000L, true },  // credits: end set, start optional (emby parity)
        { false, null, null, false }   // neither boundary
    };

    [Theory]
    [MemberData(nameof(RangeCases))]
    public void HasValidRangeAcceptsEitherBoundaryWhenEndIsOptional(bool endRequired, long? startMs, long? endMs, bool expected)
    {
        var stamp = new SegmentTimestamp { StartMs = startMs, EndMs = endMs };
        Assert.Equal(expected, stamp.HasValidRange(endRequired));
    }

    [Fact]
    public void FetchResultDistinguishesRateLimitFromNotFoundFromError()
    {
        var rateLimited = MediaFetchResult.RateLimited();
        Assert.True(rateLimited.IsRateLimited);
        Assert.False(rateLimited.IsNotFound);
        Assert.False(rateLimited.IsError);

        var notFound = MediaFetchResult.NotFound();
        Assert.True(notFound.IsNotFound);
        Assert.False(notFound.IsRateLimited);
        Assert.False(notFound.IsError);

        var error = MediaFetchResult.Error();
        Assert.True(error.IsError);
        Assert.False(error.IsNotFound);
        Assert.False(error.IsRateLimited);

        var success = MediaFetchResult.Success(new MediaResponse());
        Assert.False(success.IsError);
        Assert.False(success.IsRateLimited);
        Assert.False(success.IsNotFound);
        Assert.NotNull(success.Response);
    }
}
