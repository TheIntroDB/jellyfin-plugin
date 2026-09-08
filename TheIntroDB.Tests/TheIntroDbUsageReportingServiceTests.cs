using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Session;
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

    private static TheIntroDbUsageReportingService CreateService()
        => new(
            new Mock<ISessionManager>().Object,
            new Mock<IMediaSegmentManager>().Object,
            new Mock<ILibraryManager>().Object,
            NullLogger<TheIntroDbUsageReportingService>.Instance);
}
