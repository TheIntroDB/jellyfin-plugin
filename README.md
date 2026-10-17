# TheIntroDB – Jellyfin Plugin

<p align="center">
  <img src="https://raw.githubusercontent.com/TheIntroDB/theintrodb-assets/main/logo-banner.png">
</p>
<!-- <p align="center">
  <a href="https://discord.gg/INVITE"><img src="https://invidget.switchblade.xyz/INVITE"></a>
</p> -->

This plugin integrates [TheIntroDB API](https://api.theintrodb.org) with Jellyfin’s **Media Segments** feature. It fetches intro, recap, credits, and preview timestamps (API v4) and exposes them as Jellyfin media segments so clients can show skip buttons.

**Requirements:** Jellyfin 12+. **TVDB metadata is recommended**: TVDB is TheIntroDB's identity, and it is what lets series with a non-default display order (DVD, alternate, absolute, …) resolve to the correct episode. TMDB and IMDb ids still work as fallbacks.
([10.11.6+ version](https://github.com/TheIntroDB/jellyfin-plugin/releases/tag/v1.0.7.4)]

**Important:** Segments are **not** fetched when you press play. They are filled when the **Media segment scan** task runs (manually or on its schedule). Until that task has run for your library, skip intro/outro will not appear.

**Troubleshooting (no segments):** See the Metadata Requirements and Installation sections below.

---

## Installation

### Option A: Add repository (automatic updates)

1. In Jellyfin: **Dashboard → Plugins → Repositories → Add**.
2. Add this repository URL:
   `https://raw.githubusercontent.com/TheIntroDB/jellyfin-plugin/main/manifest.json`
3. Go to **Catalog** and install **TheIntroDB**.
4. Jellyfin will notify you when updates are available.
5. Configure at **Dashboard → Plugins → TheIntroDB** (optional API key, enable/disable segment types).
6. Run **Dashboard → Scheduled Tasks → Media Segment Scan** and click the **Play** button (▶) to populate segments.

### Option B: Manual install

It's recommended to use the repository. Manual installations don't automatically update!

1. Download the latest plugin from the [Releases](https://github.com/TheIntroDB/jellyfin-plugin/releases) page.
2. Extract `TheIntroDB.zip` into your Jellyfin plugins folder:
   - **Linux/macOS:** `~/.local/share/jellyfin/plugins/` or `$HOME/Library/Application Support/jellyfin/plugins/`
   - **Windows:** `%LocalAppData%\jellyfin\plugins\`
Ensure that `TheIntroDB/` folder contains `TheIntroDB.dll`
3. Restart Jellyfin.
4. Configure at **Dashboard → Plugins → TheIntroDB** (optional API key, enable/disable segment types).
5. Run **Dashboard → Scheduled Tasks → Media Segment Scan** and click the **Play** button (▶) to populate segments. Skip intro/outro buttons will appear in clients once the scan has run for your library.

---

### Metadata Requirements

**TVDB is recommended.** The plugin addresses media by TVDB id when one is available, then falls back to TMDB, then IMDb.

The series' **display order** is sent with every episode lookup, so a show whose files follow a non-default order (e.g. Futurama under `alternate`, or any series set to DVD/absolute order) is looked up by the numbers you actually have — not another order's. The episode's own TVDB id is sent as well, which pins the exact episode record. Series with a non-default order but no TVDB id are skipped and logged once, because no other id can express the order; adding TVDB metadata to the series fixes it.

IMDb and TMDB ids are less accurate for TV episodes: those requests are always read in that provider's numbering.

### Customizing Skip Button

You can use the File Transformation plugin to adjust the delay and timeout of Jellyfin's segment skip button.

- Plugin repo: https://github.com/IAmParadox27/jellyfin-plugin-file-transformation

Easiest way to install:

- Add as a plugin source repository to your Jellyfin server.
`https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
- Find "File Transformation" in the Catalog and install it.

## Configuration

TheIntroDB plugin includes some configuration options to adjust and improve your experience.

- **API Key**: You can enter your TheIntroDB API key to fetch your submissions even if they're still pending and prioritize yours in the averaging calculation. It also moves requests to the signed-in rate limits (120 requests per 10 s, 100,000 lookups per day instead of 30 and 5,000), which is what lets a full library scan finish in one run. The settings page validates the key server-side and shows your TheIntroDB contribution stats when the token is valid.
- **Selected Libraries And Shows**: Optionally limit the Media Segment Scan task from the plugin settings page. You can browse Jellyfin libraries, whitelist an entire library/folder, or pick individual series. When a filter is active, the plugin only fetches segments for items inside those selected libraries or for the selected shows.
- **Segment Toggles**: (All enabled by default) You can disable each segment individually so they're not applied when fetching.
- **Ignore Media That Already Has Segments**: (Enabled by default) Prevent refetching of media that already has segments. This is recommended for large libraries.

---

## Development

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build Commands

```bash
dotnet build
```

### Quick Test Loop

1. Build: `dotnet build`
2. Copy: `cp TheIntroDB/bin/Debug/net10.0/TheIntroDB.dll ~/.local/share/jellyfin/plugins/TheIntroDB/` (adjust path for your OS)
3. Restart Jellyfin
