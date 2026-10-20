using TheIntroDB.Providers;
using Xunit;

namespace TheIntroDB.Tests;

public class ExistingSegmentsTests
{
    public static TheoryData<int, int, bool, bool> Cases => new()
    {
        { 0, 0, false, false }, // nothing stored: fetch
        { 0, 0, true, false },  // nothing stored, refresh irrelevant: fetch
        { 0, 2, false, true },  // another provider's segments are served: keep
        { 2, 2, false, true },  // own segments, not due: keep
        { 2, 2, true, false },  // own segments, due: refresh
        { 2, 3, true, true },   // own segments due, but another provider's are served too: keep
        { 0, 0, false, false }  // only a disabled provider's segments (not served): fetch
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void KeepsExistingSegmentsOnlyForServedOtherProvidersOrFreshOwnSegments(int ownCount, int servedCount, bool refreshDue, bool expected)
    {
        Assert.Equal(expected, TheIntroDbSegmentProvider.ShouldKeepExistingSegments(ownCount, servedCount, refreshDue));
    }

    [Theory]
    [InlineData(0.0, 22.5)]
    [InlineData(0.5, 30.0)]
    [InlineData(0.999999, 37.5)]
    public void RefreshTtlSpreadsAroundTheSetting(double jitter, double expectedDays)
    {
        Assert.Equal(expectedDays, TheIntroDbSegmentProvider.RefreshTtl(30, jitter).TotalDays, 3);
    }
}
