# TheIntroDB – Jellyfin Plugin

This plugin integrates [TheIntroDB API](https://api.theintrodb.org) with Jellyfin’s **Media Segments** feature. It fetches intro, recap, credits, and preview timestamps by TMDB ID and exposes them as Jellyfin media segments so clients can show skip buttons.

**Requirements:** Jellyfin 10.10+ (Media Segments). **TMDb metadata is recommended** for best accuracy (IMDb works as a fallback but is less accurate for TV).

**Important:** Segments are **not** fetched when you press play. They are filled when the **Media segment scan** task runs (manually or on its schedule). Until that task has run for your library, skip intro/outro will not appear.

**Troubleshooting (no segments):** See the Metadata Requirements and Installation sections below.

---

## Installation

1. Download the latest plugin from the [Releases](https://github.com/TheIntroDB/jellyfin-plugin/releases) page.
2. Extract `TheIntroDB.zip` into your Jellyfin plugins folder:
   - **Linux/macOS:** `~/.local/share/jellyfin/plugins/` or `$HOME/Library/Application Support/jellyfin/plugins/`
   - **Windows:** `%LocalAppData%\jellyfin\plugins\`
Ensure that `TheIntroDB/` folder contains `TheIntroDB.dll`
3. Restart Jellyfin.
4. Configure at **Dashboard → Plugins → TheIntroDB** (optional API key, enable/disable segment types).
5. Run **Dashboard → Scheduled Tasks → Media Segment Scan** and click the **Play** button (▶) to populate segments. Skip intro/outro buttons will appear in clients once the scan has run for your library.

### Metadata Requirements

**TMDb is recommended.** The plugin matches content by TMDb ID for best accuracy. Add the [TMDb](https://github.com/jellyfin/jellyfin-plugin-tmdb) metadata plugin and let it fill provider IDs for your library.

IMDb IDs work as a fallback but are less accurate for TV episodes. The plugin will use whichever IDs are available on your items.

---

## Development

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Build Commands

```bash
dotnet build
```

### Quick Test Loop

1. Build: `dotnet build`
2. Copy: `cp TheIntroDB/bin/Debug/net9.0/TheIntroDB.dll ~/.local/share/jellyfin/plugins/TheIntroDB/` (adjust path for your OS)
3. Restart Jellyfin
