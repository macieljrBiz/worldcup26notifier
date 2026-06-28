using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorldCupNotifier;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static string? StartupNotificationInitializationError { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            DesktopNotificationService.Initialize();
            StartupNotificationInitializationError = null;
        }
        catch (Exception ex)
        {
            StartupNotificationInitializationError = ex.Message;
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
        public bool UseDarkMode { get; set; } = true;
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
            "Kickoff" => Application.Current.Resources["FeedEventLiveBrush"] ?? Application.Current.Resources["KickoffEventGradient"] ?? System.Windows.Media.Brushes.Transparent,
            "Goal" => Application.Current.Resources["FeedEventLiveBrush"] ?? Application.Current.Resources["GoalEventGradient"] ?? System.Windows.Media.Brushes.Transparent,
            "Result" => Application.Current.Resources["FeedEventResultBrush"] ?? Application.Current.Resources["ResultEventGradient"] ?? System.Windows.Media.Brushes.Transparent,
            _ => Application.Current.Resources["InfoEventGradient"] ?? System.Windows.Media.Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new System.NotImplementedException();
    }
}

public sealed class EventTypeIconImageConverter : System.Windows.Data.IValueConverter
{
    private static readonly Dictionary<string, ImageSource> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        var eventType = value as string ?? "Info";

        if (IconCache.TryGetValue(eventType, out var cached))
        {
            return cached;
        }

        var path = eventType switch
        {
            "Kickoff" => "pack://application:,,,/Assets/Images/Icons/Events/event_icon_kickoff.png",
            "Goal" => "pack://application:,,,/Assets/Images/Icons/Events/event_icon_goal.png",
            "Result" => "pack://application:,,,/Assets/Images/Icons/Events/event_icon_final.png",
            _ => "pack://application:,,,/Assets/Images/Icons/Events/event_icon_info.png"
        };

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            IconCache[eventType] = bitmap;
            return bitmap;
        }
        catch
        {
            return null!;
        }
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new System.NotImplementedException();
    }
}

// Converts a Match's Score object to a display string like "2 – 1" or "– –" when not started
public sealed class MatchScoreConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        if (value is not Score score)
            return "– –";

        var part = score.FullTime ?? score.RegularTime ?? score.HalfTime;
        if (part?.Home == null || part?.Away == null)
            return "– –";

        return $"{part.Home} – {part.Away}";
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new System.NotImplementedException();
    }
}

// Converts match Status string to a brush for the status badge background
public sealed class MatchStatusColorConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        if (value is not string status)
            return Application.Current.Resources["PrimaryBrandBrush"] ?? System.Windows.Media.Brushes.Blue;

        return status switch
        {
            "IN_PLAY" or "PAUSED" => Application.Current.Resources["StatusKickoffBrush"] ?? System.Windows.Media.Brushes.Green,
            "FINISHED" or "AWARDED" => Application.Current.Resources["StatusIdleBrush"] ?? System.Windows.Media.Brushes.Gray,
            "POSTPONED" or "CANCELLED" or "SUSPENDED" => Application.Current.Resources["StatusLiveBrush"] ?? System.Windows.Media.Brushes.OrangeRed,
            _ => Application.Current.Resources["PrimaryBrandBrush"] ?? System.Windows.Media.Brushes.Blue
        };
    }

    public object ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new System.NotImplementedException();
    }
}

