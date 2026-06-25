using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private SettingsWindow? _settingsWindow;

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
        UpdatePollingInterval();
        UpdateFeedSummary();
        
        Loaded += async (_, _) => await MainWindow_LoadedAsync();
        Loaded += MainWindow_Loaded_WireAnimations;
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

    private async Task MainWindow_LoadedAsync()
    {
        if (_settings.ShowNotificationsFromMinutes > 0)
        {
            await InitializeHistoricalFeed();
        }

        if (_settings.AutoStartPolling)
        {
            _pollTimer.Start();
            StartStopButton.Content = "Stop polling";
            SetStatus("Polling auto-started", $"Next automatic check in {_settings.PollingIntervalSeconds} seconds.");
        }
    }

    private void MainWindow_Loaded_WireAnimations(object sender, RoutedEventArgs e)
    {
        // Wire event handlers for button animations
        CheckNowButton.MouseEnter += Button_MouseEnter;
        CheckNowButton.MouseLeave += Button_MouseLeave;
        
        StartStopButton.MouseEnter += Button_MouseEnter;
        StartStopButton.MouseLeave += Button_MouseLeave;
        
        if (FindName("ClearFeedButton") is Button clearButton)
        {
            clearButton.MouseEnter += Button_MouseEnter;
            clearButton.MouseLeave += Button_MouseLeave;
        }
        
        SettingsButton.MouseEnter += Button_MouseEnter;
        SettingsButton.MouseLeave += Button_MouseLeave;
    }

    private async Task InitializeHistoricalFeed()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return;
        }

        SetStatus("Loading historical notifications", "Fetching recent World Cup data...");

        try
        {
            var response = await FetchMatchesAsync();
            var events = DetectEvents(response.Matches);

            var cutoffMinutes = _settings.ShowNotificationsFromMinutes;
            var historicalItemsCount = 0;

            foreach (var alert in events)
            {
                AddFeedItem(alert.Title, alert.Message, alert.EventType, alert.HomeTeam, alert.AwayTeam, alert.HomeScore, alert.AwayScore, alert.MatchUtcDate, alert.MatchStatus);
                historicalItemsCount++;
            }

            if (historicalItemsCount > 0)
            {
                AddFeedItem("Initialization complete", $"Loaded {historicalItemsCount} notification(s) from last {cutoffMinutes} minute(s).", "Info");
                SetStatus("Historical feed loaded", $"{historicalItemsCount} event(s) found.");
            }
            else
            {
                AddFeedItem("No recent notifications", $"No World Cup events detected in the last {cutoffMinutes} minute(s).", "Info");
                SetStatus("Historical feed loaded", "No recent events.");
            }
        }
        catch (Exception ex)
        {
            AddFeedItem("Failed to load historical data", ex.Message, "Info");
            SetStatus("Error loading history", ex.Message);
        }
    }

    private void ReloadSettings()
    {
        LoadSettings();
        UpdatePollingInterval();
    }

    public void SetStatusPublic(string status, string detail)
    {
        SetStatus(status, detail);
    }



    private void UpdatePollingInterval()
    {
        _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(70, _settings.PollingIntervalSeconds));
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SettingsWindow { Owner = this };
            _settingsWindow.SetParentWindow(this);
            _settingsWindow.ShowDialog();
        }
        else
        {
            _settingsWindow.Focus();
        }
    }

    private async void CheckNowButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadSettings();
        await CheckLiveDataAsync();
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadSettings();

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

    private void ClearFeedButton_Click(object sender, RoutedEventArgs e)
    {
        _feed.Clear();
        UpdateFeedSummary();
    }

    private void Button_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button)
        {
            var storyboard = (Storyboard)Resources["ButtonHoverScale"];
            storyboard?.Begin(button);
        }
    }

    private void Button_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button)
        {
            var storyboard = (Storyboard)Resources["ButtonHoverRestore"];
            storyboard?.Begin(button);
        }
    }

    private void ApplyEntranceAnimationToNewItem()
    {
        if (FeedListBox.Items.Count > 0)
        {
            var container = FeedListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            if (container != null)
            {
                var storyboard = (Storyboard)Resources["FeedItemEntranceAnimation"];
                storyboard?.Begin(container);

                // Apply pulse animation to progress bar for LIVE matches
                if (container.DataContext is AlertItem item && item.MatchStatus?.Contains("LIVE") == true)
                {
                    var progressBar = FindVisualChild<ProgressBar>(container);
                    if (progressBar != null)
                    {
                        var pulseStoryboard = (Storyboard)Resources["ProgressBarLiveAnimation"];
                        pulseStoryboard?.Begin(progressBar);
                    }
                }
            }
        }
    }

    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    private async Task CheckLiveDataAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            SetStatus("Missing API key", "Add your football-data.org API key and save settings.");
            return;
        }

        SetStatus("Checking live data", "Calling football-data.org...");
        CheckNowButton.IsEnabled = false;

        try
        {
            var response = await FetchMatchesAsync();

            var events = DetectEvents(response.Matches);
            foreach (var alert in events)
            {
                AddFeedItem(alert.Title, alert.Message, alert.EventType, alert.HomeTeam, alert.AwayTeam, alert.HomeScore, alert.AwayScore, alert.MatchUtcDate, alert.MatchStatus);

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
                "World Cup match starts soon",
                $"{FormatMatchTitle(match)} kicks off at {match.UtcDate.LocalDateTime:t}.",
                "Kickoff",
                match.HomeTeam?.Name,
                match.AwayTeam?.Name,
                snapshot.HomeScore,
                snapshot.AwayScore,
                match.UtcDate,
                snapshot.Status);
        }

        if (previous is not null && !IsLiveStatus(previous.Status) && IsLiveStatus(snapshot.Status))
        {
            yield return new DetectedEvent(
                $"{match.Id}-live-{snapshot.Status}",
                "World Cup match is live",
                $"{FormatMatchTitle(match)} is now {snapshot.Status.ToLowerInvariant().Replace("_", " ")}.",
                "Kickoff",
                match.HomeTeam?.Name,
                match.AwayTeam?.Name,
                snapshot.HomeScore,
                snapshot.AwayScore,
                match.UtcDate,
                snapshot.Status);
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
                $"Current score: {snapshot.HomeScore ?? 0}-{snapshot.AwayScore ?? 0}.",
                "Goal",
                match.HomeTeam?.Name,
                match.AwayTeam?.Name,
                snapshot.HomeScore,
                snapshot.AwayScore,
                match.UtcDate,
                snapshot.Status);
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
                $"Full time: {snapshot.HomeScore ?? 0}-{snapshot.AwayScore ?? 0}.",
                "Result",
                match.HomeTeam?.Name,
                match.AwayTeam?.Name,
                snapshot.HomeScore,
                snapshot.AwayScore,
                match.UtcDate,
                snapshot.Status);
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
        return true; // Process all matches - no team filtering
    }

    private static bool IsLiveStatus(string status)
    {
        return status is "LIVE" or "IN_PLAY" or "PAUSED";
    }

    private static string FormatMatchTitle(Match match)
    {
        return $"{match.HomeTeam?.Name ?? "Home"} vs {match.AwayTeam?.Name ?? "Away"}";
    }

    private void AddFeedItem(string title, string message, string eventType = "Info", string? homeTeam = null, string? awayTeam = null, int? homeScore = null, int? awayScore = null, DateTimeOffset? matchUtcDate = null, string? matchStatus = null)
    {
        _feed.Insert(0, new AlertItem(title, message, DateTimeOffset.Now, eventType, homeTeam, awayTeam, homeScore, awayScore, matchUtcDate, matchStatus));

        while (_feed.Count > 50)
        {
            _feed.RemoveAt(_feed.Count - 1);
        }

        UpdateFeedSummary();

        // Defer animation until container is created
        Dispatcher.BeginInvoke(() => ApplyEntranceAnimationToNewItem(), DispatcherPriority.Loaded);
    }

    public void AddFeedItemPublic(string title, string message)
    {
        AddFeedItem(title, message);
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

public sealed record AlertItem(
    string Title, 
    string Message, 
    DateTimeOffset CreatedAt,
    string EventType = "Info",
    string? HomeTeam = null,
    string? AwayTeam = null,
    int? HomeScore = null,
    int? AwayScore = null,
    DateTimeOffset? MatchUtcDate = null,
    string? MatchStatus = null)
{
    public string CreatedAtDisplay => CreatedAt.ToString("g");
    public string? MatchDisplay => HomeTeam != null && AwayTeam != null ? $"{HomeTeam} vs {AwayTeam}" : null;
    public string? ScoreDisplay => HomeScore != null && AwayScore != null ? $"{HomeScore}-{AwayScore}" : null;
    public string EventIcon => EventType switch
    {
        "Kickoff" => "⚽",
        "Goal" => "⚡",
        "Result" => "🏁",
        _ => "ℹ️"
    };
    public double MatchProgressPercentage
    {
        get
        {
            if (MatchUtcDate == null || MatchStatus == null || MatchStatus == "TIMED")
                return 0;
            if (MatchStatus == "FINISHED" || MatchStatus == "POSTPONED" || MatchStatus == "CANCELLED")
                return 100;
            
            var elapsed = DateTimeOffset.UtcNow - MatchUtcDate.Value;
            var total = TimeSpan.FromMinutes(90);
            var progress = Math.Min(100, (elapsed.TotalMinutes / total.TotalMinutes) * 100);
            return Math.Max(0, progress);
        }
    }
}

public sealed record DetectedEvent(
    string Id, 
    string Title, 
    string Message,
    string EventType = "Info",
    string? HomeTeam = null,
    string? AwayTeam = null,
    int? HomeScore = null,
    int? AwayScore = null,
    DateTimeOffset? MatchUtcDate = null,
    string? MatchStatus = null
);

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
