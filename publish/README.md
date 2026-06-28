# World Cup Notifier

A Windows desktop app built with WPF/.NET 8 for tracking real-time World Cup match updates via football-data.org. Features a modern sidebar dashboard with 5 views, live polling, native toast notifications, dark/light theme support, and persistent settings.

## Features

- **Sidebar navigation dashboard** — Dashboard, Notifications, Matches, Settings, About
- **Live match polling** — Configurable interval (default 70 s, minimum enforced to respect API limits)
- **Native Windows toast notifications** — Triggered on kickoff, goals, and final results
- **In-app alert feed** — Scrollable notification history with event type color-coding
- **Match schedule view** — Displays all tournament matches with teams, status, and kickoff times
- **Settings view** — API key, notification preferences, and theme switching (persisted to disk)
- **Dark / light theme** — Instant switching via Settings
- **Persistent settings** — Stored at `%APPDATA%\WorldCupNotifier\settings.json`

## Requirements

- Windows 10 or later (build 19041+)
- .NET 8 SDK (for building from source)
- A free football-data.org API key (Tier 1 free plan is sufficient)

## Quick start

```powershell
git clone https://github.com/yourusername/worldcupnotifier
cd worldcupnotifier
dotnet run
```

1. Click **Settings** in the sidebar.
2. Paste your football-data.org API key and click **Save Settings**.
3. Return to **Dashboard** and click **Check Now** or wait for auto-polling.
4. Alerts appear in the Dashboard feed, Notifications view, and as Windows toast notifications.

## Views

| View | Description |
|---|---|
| Dashboard | Live polling controls and notification feed |
| Notifications | Full scrollable alert history |
| Matches | Tournament match schedule with live status |
| Settings | API key, notification preferences, theme |
| About | App information and feature list |

## Configuration options

| Setting | Default | Description |
|---|---|---|
| API Key | *(empty)* | Your football-data.org API key |
| Notify on kickoff | ✅ | Toast when a match kicks off |
| Notify on goals | ✅ | Toast when a goal is scored |
| Notify on result | ✅ | Toast on full-time result |
| Native notifications | ✅ | Enable/disable Windows toast notifications |
| Theme | Light | Light or dark mode |
| Polling interval | 70 s | Seconds between API calls (min 70) |
| Auto-start polling | ✅ | Begin polling automatically on launch |

## Build from source

```powershell
cd worldcupnotifier
dotnet build
dotnet run --no-build
```

## Publish a self-contained executable

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o publish/
```

Or use the included script (builds, publishes, and zips for distribution):

```powershell
.\publish.ps1
```

The single `.exe` (≈ 60–80 MB self-contained) will be in `publish/`. The zip will be at `WorldCupNotifier-v1.0.0-win-x64.zip`.

## API rate limits

football-data.org free tier allows 10 requests per minute. The app enforces a minimum polling interval of 70 seconds to ensure you never exceed this limit.

## License

MIT
