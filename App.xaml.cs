using System.IO;
using System.Text.Json;
using System.Windows;

namespace WorldCupNotifier;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            DesktopNotificationService.Initialize();
        }
        catch
        {
            // Notification registration is retried when the user sends a test notification.
        }

        // Load theme preference on startup
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WorldCupNotifier",
                "settings.json");

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings?.UseDarkMode == true)
                {
                    SwitchTheme(true);
                }
            }
        }
        catch
        {
            // If theme loading fails, default to light theme
        }
    }

    public static void SwitchTheme(bool useDarkMode)
    {
        var currentResources = Current.Resources;
        var themePath = useDarkMode ? "Styles/Colors_Dark.xaml" : "Styles/Colors_Light.xaml";

        // Find and remove the current theme
        var themesToRemove = currentResources.MergedDictionaries
            .Where(rd => rd.Source?.OriginalString.Contains("Colors_") == true)
            .ToList();

        foreach (var theme in themesToRemove)
        {
            currentResources.MergedDictionaries.Remove(theme);
        }

        // Add new theme
        var newTheme = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };
        currentResources.MergedDictionaries.Add(newTheme);
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
    public int ShowNotificationsFromMinutes { get; set; } = 5;
    public bool AutoStartPolling { get; set; } = true;
    public bool UseDarkMode { get; set; } = false;
}

public sealed class NullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        return value == null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new System.NotImplementedException();
    }
}

public sealed class EventTypeConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        if (value is not string eventType)
            return Application.Current.Resources["StatusIdleBrush"] ?? System.Windows.Media.Brushes.Gray;

        return eventType switch
        {
            "Kickoff" => Application.Current.Resources["StatusKickoffBrush"] ?? System.Windows.Media.Brushes.Green,
            "Goal" => Application.Current.Resources["StatusGoalBrush"] ?? System.Windows.Media.Brushes.Gold,
            "Result" => Application.Current.Resources["StatusFinalBrush"] ?? System.Windows.Media.Brushes.Blue,
            _ => Application.Current.Resources["StatusIdleBrush"] ?? System.Windows.Media.Brushes.Gray
        };
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new System.NotImplementedException();
    }
}

public sealed class EventTypeBackgroundConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        if (value is not string eventType)
            return Application.Current.Resources["InfoEventGradient"] ?? System.Windows.Media.Brushes.Transparent;

        return eventType switch
        {
            "Kickoff" => Application.Current.Resources["KickoffEventGradient"] ?? System.Windows.Media.Brushes.Transparent,
            "Goal" => Application.Current.Resources["GoalEventGradient"] ?? System.Windows.Media.Brushes.Transparent,
            "Result" => Application.Current.Resources["ResultEventGradient"] ?? System.Windows.Media.Brushes.Transparent,
            _ => Application.Current.Resources["InfoEventGradient"] ?? System.Windows.Media.Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new System.NotImplementedException();
    }
}

