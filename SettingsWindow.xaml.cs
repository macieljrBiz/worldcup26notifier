using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace WorldCupNotifier;

public partial class SettingsWindow : Window
{
    private MainWindow? _parentWindow;
    private AppSettings _settings = new();
    private bool _isLoading = true;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_Loaded;
    }

    public void SetParentWindow(MainWindow parent)
    {
        _parentWindow = parent;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        LoadSettings();
        PopulateTeams();
        ApplySettingsToUi();
        WireEventHandlers();
        _isLoading = false;
    }

    private void LoadSettings()
    {
        var settingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WorldCupNotifier",
            "settings.json");

        if (!System.IO.File.Exists(settingsPath))
        {
            return;
        }

        var json = System.IO.File.ReadAllText(settingsPath);
        _settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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

        var settingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WorldCupNotifier",
            "settings.json");

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!);
        System.IO.File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        UpdateParentStatus();
        ApplySettingsToUi();
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
    }

    private void PopulateTeams()
    {
        var fallbackTeams = new[]
        {
            "Argentina", "Australia", "Belgium", "Brazil", "Cameroon", "Canada", "Costa Rica", "Croatia",
            "Denmark", "Ecuador", "England", "France", "Germany", "Ghana", "Iran", "Japan", "Mexico",
            "Morocco", "Netherlands", "Poland", "Portugal", "Qatar", "Saudi Arabia", "Senegal",
            "Serbia", "South Korea", "Spain", "Switzerland", "Tunisia", "United States", "Uruguay", "Wales"
        };

        var names = fallbackTeams
            .Append(_settings.FavoriteTeam)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        TeamComboBox.ItemsSource = names;
    }

    private void WireEventHandlers()
    {
        ApiKeyPasswordBox.PasswordChanged += (_, _) => OnSettingChanged();
        CompetitionTextBox.TextChanged += (_, _) => OnSettingChanged();
        TeamComboBox.SelectionChanged += (_, _) => OnSettingChanged();
        TeamComboBox.AddHandler(System.Windows.Controls.TextBox.TextChangedEvent, new System.Windows.Controls.TextChangedEventHandler((_, _) => OnSettingChanged()));
        KickoffCheckBox.Checked += (_, _) => OnSettingChanged();
        KickoffCheckBox.Unchecked += (_, _) => OnSettingChanged();
        GoalsCheckBox.Checked += (_, _) => OnSettingChanged();
        GoalsCheckBox.Unchecked += (_, _) => OnSettingChanged();
        ResultsCheckBox.Checked += (_, _) => OnSettingChanged();
        ResultsCheckBox.Unchecked += (_, _) => OnSettingChanged();
        NotificationsEnabledCheckBox.Checked += (_, _) => OnSettingChanged();
        NotificationsEnabledCheckBox.Unchecked += (_, _) => OnSettingChanged();
        PollingIntervalTextBox.TextChanged += (_, _) => OnSettingChanged();
    }

    private void OnSettingChanged()
    {
        if (_isLoading)
        {
            return;
        }

        SaveSettings();
    }

    private async void TestNotificationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DesktopNotificationService.ShowToast("World Cup Notifier", "Native Windows notifications are working.");
            _parentWindow?.AddFeedItemPublic("Test notification sent", "Windows accepted a native toast notification request.");
            UpdateParentStatus("Test notification sent", "Success!");
        }
        catch (Exception ex)
        {
            _parentWindow?.AddFeedItemPublic("Test notification failed", ex.Message);
            UpdateParentStatus("Notification error", ex.Message);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateParentStatus()
    {
        _parentWindow?.SetStatusPublic("Settings updated", $"Polling every {_settings.PollingIntervalSeconds} seconds.");
    }

    private void UpdateParentStatus(string status, string detail)
    {
        _parentWindow?.SetStatusPublic(status, detail);
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
