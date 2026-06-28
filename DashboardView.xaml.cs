using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace WorldCupNotifier;

public partial class DashboardView : UserControl
{
    private MainWindow? _mainWindow;
    private bool _animationsWired;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += (sender, e) => WireAnimations();
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void UpdateStats(int live, int upcoming, int finished)
    {
        LiveCountText.Text = live.ToString();
        UpcomingCountText.Text = upcoming.ToString();
        FinishedCountText.Text = finished.ToString();
    }

    public void SetPersistentMessage(string title, string detail)
    {
        PersistentMessageTitleText.Text = title;
        PersistentMessageDetailText.Text = detail;
        PersistentMessageBorder.Visibility = System.Windows.Visibility.Visible;
    }

    public void ClearPersistentMessage()
    {
        PersistentMessageTitleText.Text = string.Empty;
        PersistentMessageDetailText.Text = string.Empty;
        PersistentMessageBorder.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void WireAnimations()
    {
        if (_animationsWired)
        {
            return;
        }

        _animationsWired = true;

        CheckNowButton.MouseEnter += Button_MouseEnter;
        CheckNowButton.MouseLeave += Button_MouseLeave;
        
        StartStopButton.MouseEnter += Button_MouseEnter;
        StartStopButton.MouseLeave += Button_MouseLeave;

        var clearButton = FindName("ClearFeedButton") as Button;
        if (clearButton != null)
        {
            clearButton.MouseEnter += Button_MouseEnter;
            clearButton.MouseLeave += Button_MouseLeave;
        }
    }

    private void CheckNowButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _mainWindow?.CheckNowPublic();
    }

    private void StartStopButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _mainWindow?.StartStopPublic();
    }

    private void ClearFeedButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _mainWindow?.ClearFeedPublic();
    }

    private void Button_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button && TryFindResource("ButtonHoverScale") is Storyboard storyboard)
        {
            storyboard.Begin(button);
        }
    }

    private void Button_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button && TryFindResource("ButtonHoverRestore") is Storyboard storyboard)
        {
            storyboard.Begin(button);
        }
    }
}
