# World Cup Notifier

Windows native WPF/XAML app for tracking Soccer World Cup match updates from football-data.org.

## Features

- Native XAML desktop UI.
- Stores settings locally under `%APPDATA%\WorldCupNotifier\settings.json`.
- Polls football-data.org with a minimum 70-second interval to stay below 10 API calls/minute.
- Tracks favorite team kickoff/live status, score changes, and final results.
- Sends native Windows toast notifications.
- Keeps a local in-app alert feed.

## Configure

1. Run the app.
2. Paste your football-data.org API key.
3. Keep competition code as `WC` unless you want to monitor a different football-data.org competition.
4. Pick or type your favorite team.
5. Save settings.

## Run from source

```powershell
cd C:\repos\personal\worldcupnotifier
dotnet run
```