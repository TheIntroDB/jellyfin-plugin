using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TheIntroDB;

internal sealed class TheIntroDbUsageReportingService : IHostedService
{
    private const long MinJumpTicks = 15 * TimeSpan.TicksPerSecond;
    private const long SegmentEndToleranceTicks = 5 * TimeSpan.TicksPerSecond;
    private static readonly TimeSpan MinReportInterval = TimeSpan.FromSeconds(30);

    private readonly ISessionManager _sessionManager;
    private readonly IMediaSegmentManager _mediaSegmentManager;
    private readonly ILogger<TheIntroDbUsageReportingService> _logger;
    private readonly ConcurrentDictionary<string, PlaybackState> _states = new();

    public TheIntroDbUsageReportingService(
        ISessionManager sessionManager,
        IMediaSegmentManager mediaSegmentManager,
        ILogger<TheIntroDbUsageReportingService> logger)
    {
        _sessionManager = sessionManager;
        _mediaSegmentManager = mediaSegmentManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackProgress += SessionManager_PlaybackProgress;
        _sessionManager.PlaybackStopped += SessionManager_PlaybackStopped;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackProgress -= SessionManager_PlaybackProgress;
        _sessionManager.PlaybackStopped -= SessionManager_PlaybackStopped;
        return Task.CompletedTask;
    }

    private void SessionManager_PlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        var sessionId = e?.Session?.Id;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _states.TryRemove(sessionId, out _);
    }

    private void SessionManager_PlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (e is null)
        {
            return;
        }

        var sessionId = e.Session?.Id;
        if (string.IsNullOrWhiteSpace(sessionId) || e.Item is null)
        {
            return;
        }

        var currentTicks = e.PlaybackPositionTicks ?? 0;
        if (currentTicks <= 0)
        {
            return;
        }

        var itemId = e.Item.Id;
        var state = _states.GetOrAdd(sessionId, _ => new PlaybackState(itemId, currentTicks));
        if (state.ItemId != itemId)
        {
            state.ItemId = itemId;
            state.LastPositionTicks = currentTicks;
            state.LastReportedKey = null;
            state.LastReportedUtc = DateTime.MinValue;
            return;
        }

        var lastTicks = state.LastPositionTicks;
        state.LastPositionTicks = currentTicks;

        if (lastTicks <= 0)
        {
            return;
        }

        var delta = currentTicks - lastTicks;
        if (delta < MinJumpTicks)
        {
            return;
        }

        var capturedItem = e.Item;
        _ = Task.Run(async () =>
        {
            try
            {
                var segments = await _mediaSegmentManager.GetSegmentsAsync(capturedItem, null, new LibraryOptions(), false).ConfigureAwait(false);
                if (segments is null)
                {
                    return;
                }

                MediaSegmentDto? matched = null;
                foreach (var s in segments)
                {
                    if (lastTicks >= s.StartTicks && lastTicks <= s.EndTicks)
                    {
                        matched = s;
                        break;
                    }
                }

                if (matched is null)
                {
                    return;
                }

                if (currentTicks < (matched.EndTicks - SegmentEndToleranceTicks))
                {
                    return;
                }

                var reportKey = matched.Type + ":" + matched.StartTicks.ToString(CultureInfo.InvariantCulture);
                var now = DateTime.UtcNow;
                if (state.LastReportedKey == reportKey && now - state.LastReportedUtc < MinReportInterval)
                {
                    return;
                }

                state.LastReportedKey = reportKey;
                state.LastReportedUtc = now;

                var config = Plugin.Instance?.Configuration;
                Plugin.TrackAnonymousUsageEvent(
                    "segment_skipped",
                    new Dictionary<string, object>
                    {
                        ["host"] = "jellyfin",
                        ["segment_type"] = matched.Type.ToString().ToLowerInvariant(),
                        ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config?.ApiKey) ? 1 : 0,
                        ["jump_seconds"] = (int)(delta / TimeSpan.TicksPerSecond)
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to report segment skipped event");
            }
        });
    }

    private sealed class PlaybackState
    {
        public PlaybackState(Guid itemId, long lastPositionTicks)
        {
            ItemId = itemId;
            LastPositionTicks = lastPositionTicks;
        }

        public Guid ItemId { get; set; }

        public long LastPositionTicks { get; set; }

        public string? LastReportedKey { get; set; }

        public DateTime LastReportedUtc { get; set; }
    }
}
