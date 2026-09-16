using System;
using System.Net.Http;
using System.Net.Http.Headers;
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

    [Fact]
    public void UsageLimitResetIsTrustedUpToDailyCeiling()
    {
        using var response = new HttpResponseMessage();

        // A usage-limit 429 carries seconds until UTC midnight. Clamping this
        // to five minutes would turn an exhausted daily budget into a probe
        // loop (the reported 429-every-five-minutes behaviour).
        response.Headers.TryAddWithoutValidation("X-UsageLimit-Reset", "49000");
        const string usageBody = "{\"error\":\"Usage limit exceeded\",\"retry_after\":\"13.6 hours\",\"code\":\"usage_limit_exceeded\"}";
        Assert.Equal(49000, TheIntroDbClient.GetRetryAfterSeconds(response.Headers, usageBody));

        // Sanity bound: never wait longer than a day for the daily bucket.
        response.Headers.Clear();
        response.Headers.TryAddWithoutValidation("X-UsageLimit-Reset", "999999");
        Assert.Equal(86400, TheIntroDbClient.GetRetryAfterSeconds(response.Headers, usageBody));
    }

    [Fact]
    public void RateLimitResetIsClampedToFiveMinutes()
    {
        using var response = new HttpResponseMessage();

        // A broken/huge rate-limit reset must never disable lookups for days.
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "999999");
        Assert.Equal(300, TheIntroDbClient.GetRetryAfterSeconds(response.Headers, null));
    }

    [Fact]
    public void RetryAfterDeltaIsUsedForRateLimit429()
    {
        using var response = new HttpResponseMessage();

        // Fiber's limiter answers rate-limit 429s with Retry-After only.
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
        Assert.Equal(10, TheIntroDbClient.GetRetryAfterSeconds(response.Headers, null));
    }

    [Fact]
    public void MissingResetHeadersFallBackToFiveMinutes()
    {
        using var response = new HttpResponseMessage();
        Assert.Equal(300, TheIntroDbClient.GetRetryAfterSeconds(response.Headers, null));
    }

    [Fact]
    public void ConsecutiveRateLimit429sBackOffMultiplicatively()
    {
        Assert.Equal(10, TheIntroDbClient.ApplyConsecutiveBackOff(10, 1));
        Assert.Equal(20, TheIntroDbClient.ApplyConsecutiveBackOff(10, 2));
        Assert.Equal(30, TheIntroDbClient.ApplyConsecutiveBackOff(10, 3));
        Assert.Equal(80, TheIntroDbClient.ApplyConsecutiveBackOff(10, 8));
        Assert.Equal(80, TheIntroDbClient.ApplyConsecutiveBackOff(10, 20));
        Assert.Equal(300, TheIntroDbClient.ApplyConsecutiveBackOff(300, 2));
    }
}
