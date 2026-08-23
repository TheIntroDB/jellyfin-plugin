using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging.Abstractions;
using TheIntroDB.Api;
using Xunit;

namespace TheIntroDB.Tests;

/// <summary>
/// Verifies that the remove-segments endpoint deletes only the segments owned by
/// TheIntroDB, leaving segments produced by other providers (e.g. Intro Skipper)
/// completely untouched.
/// </summary>
public class TheIntroDbSegmentsControllerTests
{
    private const string TheIntroDbProviderName = "TheIntroDB";
    private const string IntroSkipperProviderName = "Intro Skipper";

    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public async Task RemoveTheIntroDbSegments_WhenTheIntroDbProviderNotRegistered_DeletesNothing()
    {
        var fakeManager = new FakeMediaSegmentManager(
            new[] { (IntroSkipperProviderName, "intro-skipper-hash") },
            segmentsByProvider: new Dictionary<string, List<MediaSegmentDto>>
            {
                [GetProviderId(IntroSkipperProviderName)] = new()
                {
                    Segment(MediaSegmentType.Intro, 100_000, 200_000)
                }
            });

        var controller = CreateController(fakeManager);
        var removed = await InvokeRemoveAsync(controller, new Episode { Id = ItemId, Name = "S1E1" });

        Assert.Equal(0, removed);
        Assert.Empty(fakeManager.DeletedSegmentIds);
    }

    [Fact]
    public async Task RemoveTheIntroDbSegments_DeletesOnlyTheIntroDbSegments()
    {
        var tidbProviderId = GetProviderId(TheIntroDbProviderName);
        var introSkipperProviderId = GetProviderId(IntroSkipperProviderName);

        var tidbIntro = Segment(MediaSegmentType.Intro, 100_000, 200_000);
        var tidbCredits = Segment(MediaSegmentType.Outro, 1_500_000, 1_600_000);
        var introSkipperIntro = Segment(MediaSegmentType.Intro, 90_000, 180_000);

        var fakeManager = new FakeMediaSegmentManager(
            new[] { (TheIntroDbProviderName, tidbProviderId), (IntroSkipperProviderName, introSkipperProviderId) },
            segmentsByProvider: new Dictionary<string, List<MediaSegmentDto>>
            {
                [tidbProviderId] = new() { tidbIntro, tidbCredits },
                [introSkipperProviderId] = new() { introSkipperIntro }
            });

        var controller = CreateController(fakeManager);
        var removed = await InvokeRemoveAsync(controller, new Episode { Id = ItemId, Name = "S1E1" });

        Assert.Equal(2, removed);
        Assert.Equal(2, fakeManager.DeletedSegmentIds.Count);
        Assert.Contains(tidbIntro.Id, fakeManager.DeletedSegmentIds);
        Assert.Contains(tidbCredits.Id, fakeManager.DeletedSegmentIds);
        Assert.DoesNotContain(introSkipperIntro.Id, fakeManager.DeletedSegmentIds);

        // The query must have disabled every provider other than TheIntroDB, by
        // both its hashed id (Jellyfin 10.11 matching) and its name (newer servers).
        var disabled = Assert.Single(fakeManager.LastUsedDisabledProviders);
        Assert.Contains(introSkipperProviderId, disabled);
        Assert.Contains(IntroSkipperProviderName, disabled);
        Assert.DoesNotContain(tidbProviderId, disabled);
    }

    private static TheIntroDbSegmentsController CreateController(FakeMediaSegmentManager fakeManager)
        => new(null!, fakeManager, NullLogger<TheIntroDbSegmentsController>.Instance);

    private static async Task<int> InvokeRemoveAsync(TheIntroDbSegmentsController controller, BaseItem item)
    {
        var method = typeof(TheIntroDbSegmentsController).GetMethod(
            "RemoveTheIntroDbSegmentsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var task = Assert.IsAssignableFrom<Task<int>>(method?.Invoke(controller, new object[] { item }));
        return await task.ConfigureAwait(false);
    }

    private static string GetProviderId(string providerName)
        => providerName.ToLowerInvariant().GetHashCode().ToString("X8");

    private static MediaSegmentDto Segment(MediaSegmentType type, long startTicks, long endTicks)
        => new()
        {
            Id = Guid.NewGuid(),
            ItemId = ItemId,
            StartTicks = startTicks,
            EndTicks = endTicks,
            Type = type
        };

    /// <summary>
    /// Mimics Jellyfin 10.11 <c>MediaSegmentManager</c>: returns segments only for
    /// providers not present in <see cref="LibraryOptions.DisabledMediaSegmentProviders"/>
    /// (matched against the hashed provider id), and records every segment deleted.
    /// </summary>
    private sealed class FakeMediaSegmentManager : IMediaSegmentManager
    {
        private readonly (string Name, string Id)[] _providers;
        private readonly Dictionary<string, List<MediaSegmentDto>> _segmentsByProvider;

        public FakeMediaSegmentManager(
            (string Name, string Id)[] providers,
            Dictionary<string, List<MediaSegmentDto>> segmentsByProvider)
        {
            _providers = providers;
            _segmentsByProvider = segmentsByProvider;
        }

        public List<Guid> DeletedSegmentIds { get; } = new();

        public List<string[]> LastUsedDisabledProviders { get; } = new();

        public Task RunSegmentPluginProviders(BaseItem baseItem, LibraryOptions libraryOptions, bool forceOverwrite, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public bool IsTypeSupported(BaseItem baseItem) => true;

        public Task<MediaSegmentDto> CreateSegmentAsync(MediaSegmentDto mediaSegment, string segmentProviderId)
            => throw new NotSupportedException();

        public Task DeleteSegmentAsync(Guid segmentId)
        {
            DeletedSegmentIds.Add(segmentId);
            return Task.CompletedTask;
        }

        public Task DeleteSegmentsAsync(Guid itemId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IEnumerable<MediaSegmentDto>> GetSegmentsAsync(BaseItem item, IEnumerable<MediaSegmentType>? typeFilter, LibraryOptions libraryOptions, bool filterByProvider = true)
        {
            LastUsedDisabledProviders.Add(libraryOptions.DisabledMediaSegmentProviders);

            var providerIds = _providers
                .Where(provider => !libraryOptions.DisabledMediaSegmentProviders.Contains(provider.Id))
                .Select(provider => provider.Id)
                .ToArray();

            var segments = providerIds
                .SelectMany(providerId => _segmentsByProvider.TryGetValue(providerId, out var list) ? list : Enumerable.Empty<MediaSegmentDto>())
                .Where(segment => typeFilter is null || typeFilter.Contains(segment.Type))
                .OrderBy(segment => segment.StartTicks)
                .ToArray();

            return Task.FromResult<IEnumerable<MediaSegmentDto>>(segments);
        }

        public bool HasSegments(Guid itemId) => throw new NotSupportedException();

        public IEnumerable<(string Name, string Id)> GetSupportedProviders(BaseItem item) => _providers;
    }
}
