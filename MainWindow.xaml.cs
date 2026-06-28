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
using System.Diagnostics;

namespace WorldCupNotifier;

public partial class MainWindow : Window
{
    private enum ViewType
    {
        Dashboard,
        Notifications,
        Matches,
        Settings,
        About
    }

    private static readonly string[] FallbackTeams =
    [
        "Argentina", "Australia", "Belgium", "Brazil", "Cameroon", "Canada", "Costa Rica", "Croatia",
        "Denmark", "Ecuador", "England", "France", "Germany", "Ghana", "Iran", "Japan", "Mexico",
        "Morocco", "Netherlands", "Poland", "Portugal", "Qatar", "Saudi Arabia", "Senegal",
        "Serbia", "South Korea", "Spain", "Switzerland", "Tunisia", "United States", "Uruguay", "Wales"
    ];

    private static readonly HttpClient HttpClient = new();
    private readonly ObservableCollection<AlertItem> _feed = [];
    private readonly ObservableCollection<Match> _matches = [];
    private readonly Dictionary<int, MatchSnapshot> _snapshots = [];
    private readonly HashSet<string> _sentEventIds = [];
    private readonly DispatcherTimer _pollTimer = new();
    private readonly string _settingsPath;
    private AppSettings _settings = new();
    private ViewType _currentView = ViewType.Dashboard;
    private readonly DashboardView _dashboardView = new();
    private readonly NotificationsView _notificationsView = new();
    private readonly MatchesView _matchesView = new();
    private readonly SettingsView _settingsView = new();
    private readonly AboutView _aboutView = new();
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isSwitchingView;
    private bool _isCheckingLiveData;
    private string? _lastPersistentIssueKey;

    public MainWindow()
    {
        InitializeComponent();

        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WorldCupNotifier",
            "settings.json");

        _pollTimer.Tick += PollTimer_Tick;

        LoadSettings();
        UpdatePollingInterval();
        InitializeTrayIcon();

        Loaded += MainWindow_LoadedAsync;
        Loaded += MainWindow_Loaded_WireAnimations;
    }

    private async void PollTimer_Tick(object? sender, EventArgs e)
    {
        await TriggerLiveCheckAsync("Automatic timer check", addFeedOnMissingApiKey: false);
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "World Cup Notifier",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _notifyIcon?.ShowBalloonTip(
                2000,
                "World Cup Notifier",
                "Still polling in the background. Double-click the tray icon to restore.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _pollTimer.Stop();
        _notifyIcon?.Dispose();
        base.OnClosing(e);
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

    private async void MainWindow_LoadedAsync(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(App.StartupNotificationInitializationError))
        {
            AddFeedItem("Notification setup warning", App.StartupNotificationInitializationError, "Info");
            SetPersistentIssue(
                "Notification setup warning",
                "Native notifications may be unavailable until this is resolved. Events will still appear in-app.",
                "notification-init-warning");
        }

        if (_settings.ShowNotificationsFromMinutes > 0)
        {
            await InitializeHistoricalFeed();
        }

        if (_settings.AutoStartPolling)
        {
            _pollTimer.Start();
            if (_dashboardView?.StartStopButton != null)
            {
                _dashboardView.StartStopButton.Content = "⏸ Stop";
            }
            SetStatus("Polling auto-started", $"Next automatic check in {_settings.PollingIntervalSeconds} seconds.");
        }
    }

    private void MainWindow_Loaded_WireAnimations(object sender, RoutedEventArgs e)
    {
        // Wire sidebar navigation buttons
        DashboardNavButton.MouseEnter += Button_MouseEnter;
        DashboardNavButton.MouseLeave += Button_MouseLeave;

        NotificationsNavButton.MouseEnter += Button_MouseEnter;
        NotificationsNavButton.MouseLeave += Button_MouseLeave;

        MatchesNavButton.MouseEnter += Button_MouseEnter;
        MatchesNavButton.MouseLeave += Button_MouseLeave;

        SettingsNavButton.MouseEnter += Button_MouseEnter;
        SettingsNavButton.MouseLeave += Button_MouseLeave;

        AboutNavButton.MouseEnter += Button_MouseEnter;
        AboutNavButton.MouseLeave += Button_MouseLeave;

        SettingsButton.MouseEnter += Button_MouseEnter;
        SettingsButton.MouseLeave += Button_MouseLeave;

        // Initialize DashboardView with feed binding and main window reference
        _dashboardView.SetMainWindow(this);
        _dashboardView.FeedListBox.ItemsSource = _feed;
        _dashboardView.FeedSummaryTextBlock.Text = _feed.Count == 0 ? "No alerts yet." : $"{_feed.Count} alert(s).";

        // Initialize NotificationsView with feed reference
        _notificationsView.SetMainWindow(this);

        // Initialize MatchesView with matches reference
        _matchesView.SetMainWindow(this);

        // Initialize SettingsView with settings reference
        _settingsView.SetMainWindow(this);

        // Initialize with Dashboard view
        SwitchView(ViewType.Dashboard);
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

    public ObservableCollection<AlertItem> GetFeed()
    {
        return _feed;
    }

    public ObservableCollection<Match> GetMatches()
    {
        return _matches;
    }

    public AppSettings GetSettings()
    {
        return _settings;
    }

    public void SaveSettings()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir!);
        }
        File.WriteAllText(_settingsPath, json);
        
        // Reload settings to reflect any changes
        ReloadSettings();
    }



    private void UpdatePollingInterval()
    {
        _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(70, _settings.PollingIntervalSeconds));
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewType.Settings);
        SettingsNavButton.Focus();
    }

    public void CheckNowPublic()
    {
        CheckNowButton_ClickImpl();
    }

    public void StartStopPublic()
    {
        StartStopButton_ClickImpl();
    }

    public void ClearFeedPublic()
    {
        ClearFeedButton_ClickImpl();
    }

    private void CheckNowButton_ClickImpl()
    {
        ReloadSettings();
        _ = TriggerLiveCheckAsync("Manual check");
    }

    private void StartStopButton_ClickImpl()
    {
        ReloadSettings();

        if (_pollTimer.IsEnabled)
        {
            _pollTimer.Stop();
            _dashboardView.StartStopButton.Content = "▶ Start";
            SetStatus("Polling stopped", "Live data monitoring is paused.");
            return;
        }

        _ = TriggerLiveCheckAsync("Manual start");
        _pollTimer.Start();
        _dashboardView.StartStopButton.Content = "⏸ Stop";
        SetStatus("Polling started", $"Next automatic check in {_settings.PollingIntervalSeconds} seconds.");
    }

    private void ClearFeedButton_ClickImpl()
    {
        _feed.Clear();
        UpdateFeedSummary();
    }

    private void UpdateFeedSummary()
    {
        var summary = _feed.Count == 0 ? "No alerts yet." : $"{_feed.Count} alert(s).";
        if (_dashboardView != null)
        {
            _dashboardView.FeedSummaryTextBlock.Text = summary;
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string viewName)
        {
            if (Enum.TryParse<ViewType>(viewName, out var viewType))
            {
                if (viewType == ViewType.Settings)
                {
                    SwitchView(viewType);
                }
                else
                {
                    SwitchView(viewType);
                }
            }
        }
    }

    private void SwitchView(ViewType viewType)
    {
        _ = SwitchViewAsync(viewType);
    }

    private async Task SwitchViewAsync(ViewType viewType)
    {
        if (_isSwitchingView)
        {
            return;
        }

        _isSwitchingView = true;

        try
        {
            _currentView = viewType;

            // Update button styles (active button in primary color, others in border color)
            Dispatcher.Invoke(UpdateNavButtonStyles);

            // Initialize view if first time
            if (viewType == ViewType.Notifications && _notificationsView.FindName("NotificationsListBox") != null)
            {
                _notificationsView.SetMainWindow(this);
            }

            // Fade out current content
            if (ContentControl.Content != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (TryFindResource("ViewFadeOut") is Storyboard fadeOutStoryboard)
                    {
                        fadeOutStoryboard.Begin(ContentControl);
                    }
                });

                await Task.Delay(150);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ContentControl.Content = viewType switch
                {
                    ViewType.Dashboard => _dashboardView,
                    ViewType.Notifications => _notificationsView,
                    ViewType.Matches => _matchesView,
                    ViewType.Settings => _settingsView,
                    ViewType.About => _aboutView,
                    _ => _dashboardView
                };
            });

            await Dispatcher.InvokeAsync(() =>
            {
                if (TryFindResource("ViewFadeIn") is Storyboard fadeInStoryboard)
                {
                    fadeInStoryboard.Begin(ContentControl);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"View switch failed: {ex}");
            await Dispatcher.InvokeAsync(() =>
            {
                ContentControl.Content = _dashboardView;
                _currentView = ViewType.Dashboard;
                UpdateNavButtonStyles();
            });
        }
        finally
        {
            _isSwitchingView = false;
        }
    }

    private void UpdateNavButtonStyles()
    {
        var brushConverter = new BrushConverter();
        var primaryColor = (Brush)(brushConverter.ConvertFromString("#1FC5FF") ?? Brushes.DeepSkyBlue);
        var secondaryColor = (Brush)(brushConverter.ConvertFromString("#213A72") ?? Brushes.SteelBlue);
        var primaryText = (Brush)(brushConverter.ConvertFromString("#EAF2FF") ?? Brushes.WhiteSmoke);
        var whiteText = Brushes.White;

        var buttons = new[]
        {
            (DashboardNavButton, ViewType.Dashboard),
            (NotificationsNavButton, ViewType.Notifications),
            (MatchesNavButton, ViewType.Matches),
            (SettingsNavButton, ViewType.Settings),
            (AboutNavButton, ViewType.About)
        };

        foreach (var (button, viewType) in buttons)
        {
            if (viewType == _currentView)
            {
                button.Background = primaryColor;
                button.Foreground = whiteText;
                button.BorderBrush = (Brush)(brushConverter.ConvertFromString("#7DE4FF") ?? Brushes.LightBlue);
                button.BorderThickness = new Thickness(1);
            }
            else
            {
                button.Background = secondaryColor;
                button.Foreground = primaryText;
                button.BorderBrush = (Brush)(brushConverter.ConvertFromString("#2E4C8A") ?? Brushes.SlateBlue);
                button.BorderThickness = new Thickness(1);
            }
        }
    }

    private void Button_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button)
        {
            if (TryFindResource("ButtonHoverScale") is Storyboard storyboard)
            {
                storyboard.Begin(button);
            }
        }
    }

    private void Button_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button)
        {
            if (TryFindResource("ButtonHoverRestore") is Storyboard storyboard)
            {
                storyboard.Begin(button);
            }
        }
    }

    private void ApplyEntranceAnimationToNewItem()
    {
        var feedListBox = _dashboardView.FeedListBox;
        if (feedListBox.Items.Count > 0)
        {
            var container = feedListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            if (container != null)
            {
                if (TryFindResource("FeedItemEntranceAnimation") is Storyboard storyboard)
                {
                    storyboard.Begin(container);
                }

                // Apply pulse animation to progress bar for LIVE matches
                if (container.DataContext is AlertItem item && item.MatchStatus?.Contains("LIVE") == true)
                {
                    var progressBar = FindVisualChild<ProgressBar>(container);
                    if (progressBar != null)
                    {
                        if (TryFindResource("ProgressBarLiveAnimation") is Storyboard pulseStoryboard)
                        {
                            pulseStoryboard.Begin(progressBar);
                        }
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

    private async Task TriggerLiveCheckAsync(string source, bool addFeedOnMissingApiKey = true)
    {
        if (_isCheckingLiveData)
        {
            return;
        }

        _isCheckingLiveData = true;
        try
        {
            await CheckLiveDataAsync(addFeedOnMissingApiKey);
        }
        catch (Exception ex)
        {
            SetStatus("Live data error", ex.Message);
            AddFeedItem("Live data error", ex.Message);
            SetPersistentIssue("Live data error", ex.Message, $"fatal-live-check:{source}");
        }
        finally
        {
            _isCheckingLiveData = false;
        }
    }

    private async Task CheckLiveDataAsync(bool addFeedOnMissingApiKey)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            SetStatus("Missing API key", "Add your football-data.org API key and save settings.");
            SetPersistentIssue(
                "Missing API key",
                "Add your football-data.org API key in Settings to start match checks and alerts.",
                "missing-api-key");
            if (addFeedOnMissingApiKey)
            {
                AddFeedItem("Missing API key", "Add your football-data.org API key in Settings to start match checks and alerts.", "Info");
            }
            return;
        }

        SetStatus("Checking live data", "Calling football-data.org...");
        if (_dashboardView?.CheckNowButton != null)
        {
            _dashboardView.CheckNowButton.IsEnabled = false;
        }

        try
        {
            var response = await FetchMatchesAsync();

            // Update matches collection for MatchesView
            _matches.Clear();
            foreach (var match in response.Matches)
            {
                _matches.Add(match);
            }

            // Update Dashboard summary stats
            var live     = _matches.Count(m => m.Status is "IN_PLAY" or "PAUSED");
            var upcoming = _matches.Count(m => m.Status is "TIMED" or "SCHEDULED");
            var finished = _matches.Count(m => m.Status is "FINISHED" or "AWARDED");
            Dispatcher.Invoke(() => _dashboardView?.UpdateStats(live, upcoming, finished));

            var events = DetectEvents(response.Matches);
            foreach (var alert in events)
            {
                AddFeedItem(alert.Title, alert.Message, alert.EventType, alert.HomeTeam, alert.AwayTeam, alert.HomeScore, alert.AwayScore, alert.MatchUtcDate, alert.MatchStatus);

                if (_settings.NotificationsEnabled)
                {
                    try
                    {
                        await SendNativeNotificationAsync(alert.Title, alert.Message);
                    }
                    catch (Exception ex)
                    {
                        AddFeedItem("Native notification failed", ex.Message, "Info");
                        SetStatus("Notification warning", "Native toast failed. Event still logged in-app.");
                    }
                }
            }

            var detail = events.Count == 0
                ? $"No new alerts. Matches returned: {response.Matches.Count}."
                : $"{events.Count} new alert(s). Matches returned: {response.Matches.Count}.";
            SetStatus("Live data checked", detail);
            ClearPersistentIssue();
        }
        catch (Exception ex)
        {
            SetStatus("Live data error", ex.Message);
            AddFeedItem("Live data error", ex.Message);
            SetPersistentIssue("Live data error", ex.Message, $"live-data-error:{ex.Message}");
        }
        finally
        {
            if (_dashboardView?.CheckNowButton != null)
            {
                _dashboardView.CheckNowButton.IsEnabled = true;
            }
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

    private void SetStatus(string status, string detail)
    {
        StatusTextBlock.Text = status;
        LastCheckedTextBlock.Text = detail;

        var statusBrush = status.Contains("error", StringComparison.OrdinalIgnoreCase)
            ? (Brush)Resources["StatusErrorBrush"]
            : status.Contains("warning", StringComparison.OrdinalIgnoreCase)
                ? (Brush)Resources["StatusGoalBrush"]
                : status.Contains("checked", StringComparison.OrdinalIgnoreCase)
                    ? (Brush)Resources["StatusKickoffBrush"]
                    : (Brush)Resources["StatusFinalBrush"];

        StatusTextBlock.Foreground = statusBrush;
    }

    private void SetPersistentIssue(string title, string detail, string issueKey)
    {
        if (_lastPersistentIssueKey == issueKey)
        {
            return;
        }

        _lastPersistentIssueKey = issueKey;
        _dashboardView.SetPersistentMessage(title, detail);
        _notificationsView.SetPersistentMessage(title, detail);
    }

    private void ClearPersistentIssue()
    {
        _lastPersistentIssueKey = null;
        _dashboardView.ClearPersistentMessage();
        _notificationsView.ClearPersistentMessage();
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

public sealed class Match : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isExpanded;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded))); }
    }

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
