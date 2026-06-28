using System.Windows;
using System.Windows.Controls;

namespace WorldCupNotifier;

public partial class SettingsView : UserControl
{
    private MainWindow? _mainWindow;
    private bool _eventHandlersWired;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        LoadSettings();
        WireEventHandlers();
    }

    private void LoadSettings()
    {
        if (_mainWindow == null) return;

        var settings = _mainWindow.GetSettings();

        // Load API key
        ApiKeyInput.Text = settings.ApiKey ?? string.Empty;

        // Load notification preferences
        NotifyKickoffCheck.IsChecked = settings.NotifyKickoff;
        NotifyGoalsCheck.IsChecked = settings.NotifyGoals;
        NotifyResultsCheck.IsChecked = settings.NotifyResults;
        EnableNotificationsCheck.IsChecked = settings.NotificationsEnabled;

        // Load theme preference
        if (settings.UseDarkMode)
        {
            DarkThemeRadio.IsChecked = true;
        }
        else
        {
            LightThemeRadio.IsChecked = true;
        }
    }

    private void WireEventHandlers()
    {
        if (_eventHandlersWired)
        {
            return;
        }

        _eventHandlersWired = true;

        SaveSettingsButton.Click += SaveSettingsButton_Click;
        LightThemeRadio.Checked += ThemeRadio_Checked;
        DarkThemeRadio.Checked += ThemeRadio_Checked;
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var settings = _mainWindow.GetSettings();

        // Update settings
        settings.ApiKey = ApiKeyInput.Text.Trim();
        settings.NotifyKickoff = NotifyKickoffCheck.IsChecked ?? false;
        settings.NotifyGoals = NotifyGoalsCheck.IsChecked ?? false;
        settings.NotifyResults = NotifyResultsCheck.IsChecked ?? false;
        settings.NotificationsEnabled = EnableNotificationsCheck.IsChecked ?? false;
        settings.UseDarkMode = DarkThemeRadio.IsChecked ?? false;

        // Save to disk
        _mainWindow.SaveSettings();

        // Show confirmation
        MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        bool useDarkMode = DarkThemeRadio.IsChecked ?? false;
        App.SwitchTheme(useDarkMode);
    }
}
