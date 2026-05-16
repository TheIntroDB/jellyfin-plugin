using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using TheIntroDB.Configuration;

namespace TheIntroDB;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        AnonymousUsageReporter.TrackPluginLoaded(this);
    }

    /// <inheritdoc />
    public override string Name => "TheIntroDB";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("eb5d7894-8eef-4b36-aa6f-5d124e828ce1");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    internal static DateTime RateLimitExpiryUtc { get; set; }

    internal static void TrackAnonymousUsageEvent(string eventName, Dictionary<string, object>? props = null)
    {
        AnonymousUsageReporter.TrackEvent(Instance, eventName, props);
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

    internal static class AnonymousUsageReporter
    {
        private const string AppKey = "A-SH-4146734650";
        private const string Host = "https://analytics.theintrodb.org";
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly string SessionId = NewSessionId();

        public static void TrackPluginLoaded(Plugin plugin)
        {
            var config = plugin?.Configuration;
            TrackEvent(
                plugin,
                "plugin_loaded",
                new Dictionary<string, object>
                {
                    ["host"] = "jellyfin",
                    ["enable_intro"] = config?.EnableIntro == true ? 1 : 0,
                    ["enable_recap"] = config?.EnableRecap == true ? 1 : 0,
                    ["enable_credits"] = config?.EnableCredits == true ? 1 : 0,
                    ["enable_preview"] = config?.EnablePreview == true ? 1 : 0,
                    ["ignore_existing"] = config?.IgnoreMediaWithExistingSegments == true ? 1 : 0,
                    ["has_theintrodb_api_key"] = !string.IsNullOrWhiteSpace(config?.ApiKey) ? 1 : 0
                });
        }

        internal static void TrackEvent(Plugin? plugin, string eventName, Dictionary<string, object>? props = null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TrackEventAsync(plugin, eventName, props).ConfigureAwait(false);
                }
                catch
                {
                }
            });
        }

        private static async Task TrackEventAsync(Plugin? plugin, string eventName, Dictionary<string, object>? props)
        {
            if (plugin is null)
            {
                return;
            }

            var config = plugin.Configuration;
            if (config is null || !config.EnableAnonymousUsageReporting)
            {
                return;
            }

            var appKey = AppKey;
            if (string.IsNullOrWhiteSpace(appKey))
            {
                return;
            }

            if (!Uri.TryCreate(Host, UriKind.Absolute, out var hostUri))
            {
                return;
            }

            var version = plugin.GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var payload = new[]
            {
                new AptabaseEvent
                {
                    Timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    SessionId = SessionId,
                    EventName = eventName,
                    SystemProps = new Dictionary<string, object>
                    {
                        ["locale"] = CultureInfo.CurrentCulture.Name,
                        ["osName"] = Environment.OSVersion.Platform.ToString(),
                        ["osVersion"] = Environment.OSVersion.Version.ToString(),
                        ["isDebug"] =
#if DEBUG
                            true,
#else
                            false,
#endif
                        ["appVersion"] = version,
                        ["sdkVersion"] = "theintrodb-jellyfin-plugin@" + version
                    },
                    Props = MergeProps(
                        new Dictionary<string, object>
                        {
                            ["plugin"] = plugin.Name,
                            ["plugin_version"] = version
                        },
                        props)
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var requestUri = new Uri(hostUri.AbsoluteUri.TrimEnd('/') + "/api/v0/events", UriKind.Absolute);
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.TryAddWithoutValidation("App-Key", appKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
        }

        private static Dictionary<string, object> MergeProps(Dictionary<string, object> baseProps, Dictionary<string, object>? extraProps)
        {
            if (extraProps is null || extraProps.Count == 0)
            {
                return baseProps;
            }

            foreach (var kvp in extraProps)
            {
                baseProps[kvp.Key] = kvp.Value;
            }

            return baseProps;
        }

        private static string NewSessionId()
        {
            var epochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var randomNumber = RandomNumberGenerator.GetInt32(0, 100000000);
            return epochSeconds.ToString(CultureInfo.InvariantCulture) + randomNumber.ToString("D8", CultureInfo.InvariantCulture);
        }

        private sealed class AptabaseEvent
        {
            [JsonPropertyName("timestamp")]
            public string Timestamp { get; set; } = string.Empty;

            [JsonPropertyName("sessionId")]
            public string SessionId { get; set; } = string.Empty;

            [JsonPropertyName("eventName")]
            public string EventName { get; set; } = string.Empty;

            [JsonPropertyName("systemProps")]
            public Dictionary<string, object> SystemProps { get; set; } = new Dictionary<string, object>();

            [JsonPropertyName("props")]
            public Dictionary<string, object> Props { get; set; } = new Dictionary<string, object>();
        }
    }
}
