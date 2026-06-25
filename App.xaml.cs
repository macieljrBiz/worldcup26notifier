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
    }
}

