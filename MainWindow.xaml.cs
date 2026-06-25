using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace WorldCupNotifier;

public partial class MainWindow : Window
{
    private static readonly string[] FallbackTeams =
    [
        "Argentina", "Australia", "Belgium", "Brazil", "Cameroon", "Canada", "Costa Rica", "Croatia",
        "Denmark", "Ecuador", "England", "France", "Germany", "Ghana", "Iran", "Japan", "Mexico",
        "Morocco", "Netherlands", "Poland", "Portugal", "Qatar", "Saudi Arabia", "Senegal",
        "Serbia", "South Korea", "Spain", "Switzerland", "Tunisia", "United States", "Uruguay", "Wales"
    ];

    private static readonly HttpClient HttpClient = new();
    private readonly ObservableCollection<AlertItem> _feed = [];
    private readonly Dictionary<int, MatchSnapshot> _snapshots = [];
    private readonly HashSet<string> _sentEventIds = [];
    private readonly DispatcherTimer _pollTimer = new();
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WorldCupNotifier",
            "settings.json");

        FeedListBox.ItemsSource = _feed;
        _pollTimer.Tick += async (_, _) => await CheckLiveDataAsync();

        LoadSettings();
        PopulateTeams(FallbackTeams);
        ApplySettingsToUi();
        UpdateFeedSummary();
    }

    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        var json = File.ReadAllText(_settingsPath);
        _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    private void SaveSettings()
    {
        _settings.ApiKey = ApiKeyPasswordBox.Password.Trim();
        _settings.Competition = string.IsNullOrWhiteSpace(CompetitionTextBox.Text) ? "WC" : CompetitionTextBox.Text.Trim().ToUpperInvariant();
        _settings.FavoriteTeam = TeamComboBox.Text.Trim();
        _settings.NotifyKickoff = KickoffCheckBox.IsChecked == true;
        _settings.NotifyGoals = GoalsCheckBox.IsChecked == true;
        _settings.NotifyResults = ResultsCheckBox.IsChecked == true;
        _settings.NotificationsEnabled = NotificationsEnabledCheckBox.IsChecked == true;
        _settings.PollingIntervalSeconds = Math.Max(70, ParseInt(PollingIntervalTextBox.Text, 70));

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        ApplySettingsToUi();
        SetStatus("Settings saved", $"Polling every {_settings.PollingIntervalSeconds} seconds.");
    }

    private void ApplySettingsToUi()
    {
        ApiKeyPasswordBox.Password = _settings.ApiKey;
        CompetitionTextBox.Text = _settings.Competition;
        TeamComboBox.Text = _settings.FavoriteTeam;
        KickoffCheckBox.IsChecked = _settings.NotifyKickoff;
        GoalsCheckBox.IsChecked = _settings.NotifyGoals;
        ResultsCheckBox.IsChecked = _settings.NotifyResults;
        NotificationsEnabledCheckBox.IsChecked = _settings.NotificationsEnabled;
        PollingIntervalTextBox.Text = _settings.PollingIntervalSeconds.ToString();

        _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(70, _settings.PollingIntervalSeconds));
    }

    private void PopulateTeams(IEnumerable<string> teamNames)
    {
        var names = teamNames
            .Append(_settings.FavoriteTeam)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        TeamComboBox.ItemsSource = names;
    }

    private async void CheckNowButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        await CheckLiveDataAsync();
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();

        if (_pollTimer.IsEnabled)
        {
            _pollTimer.Stop();
            StartStopButton.Content = "Start polling";
            SetStatus("Polling stopped", "Live data monitoring is paused.");
            return;
        }

        await CheckLiveDataAsync();
        _pollTimer.Start();
        StartStopButton.Content = "Stop polling";
        SetStatus("Polling started", $"Next automatic check in {_settings.PollingIntervalSeconds} seconds.");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private async void TestNotificationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SendNativeNotificationAsync("World Cup Notifier", "Native Windows notifications are working.");
            AddFeedItem("Test notification sent", "Windows accepted a native toast notification request.");
        }
        catch (Exception ex)
        {
            AddFeedItem("Test notification failed", ex.Message);
            SetStatus("Notification error", ex.Message);
        }
    }

    private void ClearFeedButton_Click(object sender, RoutedEventArgs e)
    {
        _feed.Clear();
        UpdateFeedSummary();
    }

    private async Task CheckLiveDataAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            SetStatus("Missing API key", "Add your football-data.org API key and save settings.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.FavoriteTeam))
        {
            SetStatus("Missing favorite team", "Select or type a favorite team.");
            return;
        }

        SetStatus("Checking live data", "Calling football-data.org...");
        CheckNowButton.IsEnabled = false;

        try
        {
            var response = await FetchMatchesAsync();
            PopulateTeams(response.Matches.SelectMany(match => new[] { match.HomeTeam?.Name, match.AwayTeam?.Name }).OfType<string>());

            var events = DetectEvents(response.Matches);
            foreach (var alert in events)
            {
                AddFeedItem(alert.Title, alert.Message);

                if (_settings.NotificationsEnabled)
                {
                    await SendNativeNotificationAsync(alert.Title, alert.Message);
                }
            }

            var detail = events.Count == 0
                ? $"No new alerts. Matches returned: {response.Matches.Count}."
                : $"{events.Count} new alert(s). Matches returned: {response.Matches.Count}.";
            SetStatus("Live data checked", detail);
        }
        catch (Exception ex)
        {
            SetStatus("Live data error", ex.Message);
            AddFeedItem("Live data error", ex.Message);
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private async Task<FootballDataResponse> FetchMatchesAsync()
    {
        var today = DateTime.UtcNow.Date;
        var dateFrom = today.AddDays(-1).ToString("yyyy-MM-dd");
        var dateTo = today.AddDays(14).ToString("yyyy-MM-dd");
        var uri = $"https://api.football-data.org/v4/competitions/{Uri.EscapeDataString(_settings.Competition)}/matches?dateFrom={dateFrom}&dateTo={dateTo}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("X-Auth-Token", _settings.ApiKey);

        using var response = await HttpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"football-data.org returned {(int)response.StatusCode}: {error}");
        }

        return await response.Content.ReadFromJsonAsync<FootballDataResponse>() ?? new FootballDataResponse();
    }

    private List<DetectedEvent> DetectEvents(IEnumerable<Match> matches)
    {
        var alerts = new List<DetectedEvent>();

        foreach (var match in matches.Where(IsFavoriteMatch))
        {
            var snapshot = MatchSnapshot.From(match);
            _snapshots.TryGetValue(match.Id, out var previous);

            alerts.AddRange(DetectKickoffEvents(match, snapshot, previous));
            alerts.AddRange(DetectGoalEvents(match, snapshot, previous));
            alerts.AddRange(DetectResultEvents(match, snapshot, previous));

            _snapshots[match.Id] = snapshot;
        }

        return alerts.Where(ShouldDeliver).ToList();
    }

    private IEnumerable<DetectedEvent> DetectKickoffEvents(Match match, MatchSnapshot snapshot, MatchSnapshot? previous)
    {
        if (!_settings.NotifyKickoff)
        {
            yield break;
        }

        var startsIn = match.UtcDate - DateTimeOffset.UtcNow;
        if (startsIn > TimeSpan.Zero && startsIn <= TimeSpan.FromMinutes(15))
        {
            yield return new DetectedEvent(
                $"{match.Id}-kickoff-{match.UtcDate:O}",
                $"{_settings.FavoriteTeam} match starts soon",
                $"{FormatMatchTitle(match)} kicks off at {match.UtcDate.LocalDateTime:t}.");
        }

        if (previous is not null && !IsLiveStatus(previous.Status) && IsLiveStatus(snapshot.Status))
        {
            yield return new DetectedEvent(
                $"{match.Id}-live-{snapshot.Status}",
                $"{_settings.FavoriteTeam} match is live",
                $"{FormatMatchTitle(match)} is now {snapshot.Status.ToLowerInvariant().Replace("_", " ")}.");
        }
    }

    private IEnumerable<DetectedEvent> DetectGoalEvents(Match match, MatchSnapshot snapshot, MatchSnapshot? previous)
    {
        if (!_settings.NotifyGoals || previous is null)
        {
            yield break;
        }

        var homeIncreased = snapshot.HomeScore is not null && previous.HomeScore is not null && snapshot.HomeScore > previous.HomeScore;
        var awayIncreased = snapshot.AwayScore is not null && previous.AwayScore is not null && snapshot.AwayScore > previous.AwayScore;

        if (homeIncreased || awayIncreased)
        {
            yield return new DetectedEvent(
                $"{match.Id}-goal-{snapshot.HomeScore}-{snapshot.AwayScore}",
                $"Goal update: {FormatMatchTitle(match)}",
                $"Current score: {snapshot.HomeScore ?? 0}-{snapshot.AwayScore ?? 0}.");
        }
    }

    private IEnumerable<DetectedEvent> DetectResultEvents(Match match, MatchSnapshot snapshot, MatchSnapshot? previous)
    {
        if (!_settings.NotifyResults)
        {
            yield break;
        }

        if (snapshot.Status == "FINISHED" && previous?.Status != "FINISHED")
        {
            yield return new DetectedEvent(
                $"{match.Id}-finished-{snapshot.HomeScore}-{snapshot.AwayScore}",
                $"Final result: {FormatMatchTitle(match)}",
                $"Full time: {snapshot.HomeScore ?? 0}-{snapshot.AwayScore ?? 0}.");
        }
    }

    private bool ShouldDeliver(DetectedEvent detectedEvent)
    {
        if (_sentEventIds.Contains(detectedEvent.Id))
        {
            return false;
        }

        _sentEventIds.Add(detectedEvent.Id);
        return true;
    }

    private bool IsFavoriteMatch(Match match)
    {
        return string.Equals(match.HomeTeam?.Name, _settings.FavoriteTeam, StringComparison.OrdinalIgnoreCase)
            || string.Equals(match.AwayTeam?.Name, _settings.FavoriteTeam, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLiveStatus(string status)
    {
        return status is "LIVE" or "IN_PLAY" or "PAUSED";
    }

    private static string FormatMatchTitle(Match match)
    {
        return $"{match.HomeTeam?.Name ?? "Home"} vs {match.AwayTeam?.Name ?? "Away"}";
    }

    private void AddFeedItem(string title, string message)
    {
        _feed.Insert(0, new AlertItem(title, message, DateTimeOffset.Now));

        while (_feed.Count > 50)
        {
            _feed.RemoveAt(_feed.Count - 1);
        }

        UpdateFeedSummary();
    }

    private void UpdateFeedSummary()
    {
        FeedSummaryTextBlock.Text = _feed.Count == 0 ? "No alerts yet." : $"{_feed.Count} alert(s).";
    }

    private void SetStatus(string status, string detail)
    {
        StatusTextBlock.Text = status;
        LastCheckedTextBlock.Text = detail;
    }

    private static Task SendNativeNotificationAsync(string title, string message)
    {
        DesktopNotificationService.ShowToast(title, message);
        return Task.CompletedTask;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}

public sealed class AppSettings
{
    public string ApiKey { get; set; } = "";
    public string Competition { get; set; } = "WC";
    public string FavoriteTeam { get; set; } = "Brazil";
    public bool NotifyKickoff { get; set; } = true;
    public bool NotifyGoals { get; set; } = true;
    public bool NotifyResults { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 70;
}

public sealed record AlertItem(string Title, string Message, DateTimeOffset CreatedAt)
{
    public string CreatedAtDisplay => CreatedAt.ToString("g");
}

public sealed record DetectedEvent(string Id, string Title, string Message);

public sealed record MatchSnapshot(int Id, string Status, int? HomeScore, int? AwayScore)
{
    public static MatchSnapshot From(Match match)
    {
        var score = match.Score?.FullTime ?? match.Score?.RegularTime ?? match.Score?.HalfTime ?? new ScorePart();
        return new MatchSnapshot(match.Id, match.Status, score.Home, score.Away);
    }
}

public sealed class FootballDataResponse
{
    [JsonPropertyName("matches")]
    public List<Match> Matches { get; set; } = [];
}

public sealed class Match
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("utcDate")]
    public DateTimeOffset UtcDate { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("homeTeam")]
    public Team? HomeTeam { get; set; }

    [JsonPropertyName("awayTeam")]
    public Team? AwayTeam { get; set; }

    [JsonPropertyName("score")]
    public Score? Score { get; set; }
}

public sealed class Team
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class Score
{
    [JsonPropertyName("fullTime")]
    public ScorePart? FullTime { get; set; }

    [JsonPropertyName("regularTime")]
    public ScorePart? RegularTime { get; set; }

    [JsonPropertyName("halfTime")]
    public ScorePart? HalfTime { get; set; }
}

public sealed class ScorePart
{
    [JsonPropertyName("home")]
    public int? Home { get; set; }

    [JsonPropertyName("away")]
    public int? Away { get; set; }
}
