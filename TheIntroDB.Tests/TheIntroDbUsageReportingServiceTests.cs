using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace TheIntroDB.Tests;

/// <summary>
/// Regression tests for https://github.com/TheIntroDB/jellyfin-plugin/issues/23:
/// Jellyfin crashes with an unhandled <see cref="ObjectDisposedException"/> on shutdown
/// or restart because the usage-reporting service's <see cref="TheIntroDbUsageReportingService.Dispose"/>
/// re-cancels and re-disposes a <see cref="CancellationTokenSource"/> that
/// <see cref="TheIntroDbUsageReportingService.StopAsync"/> had already disposed.
/// </summary>
public class TheIntroDbUsageReportingServiceTests
{
    [Fact]
    public async Task Dispose_AfterStopAsync_DoesNotThrow()
    {
        var service = CreateService();

        // The generic host calls StopAsync, then disposes the hosted service.
        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var service = CreateService();

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void ItemAdded_Movie_Fetches()
    {
        Assert.True(CreateService().ShouldFetchOnItemAdded(new Movie()));
    }

    [Theory]
    [InlineData(nameof(MetadataProvider.Tvdb))]
    [InlineData(nameof(MetadataProvider.Tmdb))]
    public void ItemAdded_EpisodeOfSeriesWithStableId_Fetches(string provider)
    {
        var series = new Series { Id = Guid.NewGuid() };
        series.SetProviderId(provider, "123");

        var service = CreateService(series);

        Assert.True(service.ShouldFetchOnItemAdded(new Episode { SeriesId = series.Id }));
    }

    [Fact]
    public void ItemAdded_EpisodeOfSeriesWithImdbOnly_WaitsForMetadata()
    {
        var series = new Series { Id = Guid.NewGuid() };
        series.SetProviderId(MetadataProvider.Imdb, "tt0397306");

        var service = CreateService(series);

        Assert.False(service.ShouldFetchOnItemAdded(new Episode { SeriesId = series.Id }));
    }

    [Fact]
    public void ItemAdded_EpisodeOfUnknownSeries_WaitsForMetadata()
    {
        Assert.False(CreateService().ShouldFetchOnItemAdded(new Episode { SeriesId = Guid.NewGuid() }));
    }

    [Fact]
    public void ItemAdded_EpisodeWithoutSeriesId_WaitsForMetadataInsteadOfThrowing()
    {
        // Jellyfin's GetItemById throws on an empty id, so the lookup must not happen.
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(m => m.GetItemById(Guid.Empty)).Throws(new ArgumentException("Guid can't be empty", "id"));

        var service = CreateService(libraryManager: libraryManager);

        Assert.False(service.ShouldFetchOnItemAdded(new Episode()));
    }

    [Theory]
    [InlineData(ItemUpdateType.MetadataDownload, true)]
    [InlineData(ItemUpdateType.MetadataDownload | ItemUpdateType.ImageUpdate, true)]
    [InlineData(ItemUpdateType.MetadataImport, false)]
    [InlineData(ItemUpdateType.MetadataEdit, false)]
    [InlineData(ItemUpdateType.ImageUpdate, false)]
    public void ItemUpdated_EpisodeFetchesOnlyAfterMetadataDownload(ItemUpdateType reason, bool expected)
    {
        var e = new ItemChangeEventArgs { Item = new Episode(), UpdateReason = reason };

        Assert.Equal(expected, TheIntroDbUsageReportingService.ShouldFetchOnItemUpdated(e));
    }

    [Fact]
    public void ItemUpdated_MovieFetchesAfterMetadataDownload()
    {
        var e = new ItemChangeEventArgs { Item = new Movie(), UpdateReason = ItemUpdateType.MetadataDownload };

        Assert.True(TheIntroDbUsageReportingService.ShouldFetchOnItemUpdated(e));
    }

    [Fact]
    public void ItemUpdated_Series_DoesNotFetch()
    {
        var e = new ItemChangeEventArgs { Item = new Series(), UpdateReason = ItemUpdateType.MetadataDownload };

        Assert.False(TheIntroDbUsageReportingService.ShouldFetchOnItemUpdated(e));
    }

    private static TheIntroDbUsageReportingService CreateService(BaseItem? knownItem = null, Mock<ILibraryManager>? libraryManager = null)
    {
        libraryManager ??= new Mock<ILibraryManager>();
        if (knownItem is not null)
        {
            libraryManager.Setup(m => m.GetItemById(knownItem.Id)).Returns(knownItem);
        }

        return new(
            new Mock<ISessionManager>().Object,
            new Mock<IMediaSegmentManager>().Object,
            libraryManager.Object,
            NullLogger<TheIntroDbUsageReportingService>.Instance);
    }
}
