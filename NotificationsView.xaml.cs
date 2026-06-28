using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace WorldCupNotifier;

public partial class NotificationsView : UserControl
{
    private ICollectionView? _collectionView;
    private string _activeFilter = "All";

    public NotificationsView()
    {
        InitializeComponent();
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _collectionView = CollectionViewSource.GetDefaultView(mainWindow.GetFeed());
        _collectionView.Filter = ApplyFilter;
        NotificationsListBox.ItemsSource = _collectionView;
        UpdateFilterButtonStyles();
    }

    public void SetPersistentMessage(string title, string detail)
    {
        PersistentMessageTitleText.Text = title;
        PersistentMessageDetailText.Text = detail;
        PersistentMessageBorder.Visibility = Visibility.Visible;
    }

    public void ClearPersistentMessage()
    {
        PersistentMessageTitleText.Text = string.Empty;
        PersistentMessageDetailText.Text = string.Empty;
        PersistentMessageBorder.Visibility = Visibility.Collapsed;
    }

    private bool ApplyFilter(object item)
    {
        if (_activeFilter == "All") return true;
        return item is AlertItem alert && alert.EventType == _activeFilter;
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _activeFilter = btn.Tag as string ?? "All";
        _collectionView?.Refresh();
        UpdateFilterButtonStyles();
    }

    private void UpdateFilterButtonStyles()
    {
        var active = (Brush)Application.Current.Resources["PrimaryBrandBrush"];
        var inactive = (Brush)Application.Current.Resources["CardBackgroundBrush"];
        var activeText = Brushes.White;
        var inactiveText = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        var border = (Brush)Application.Current.Resources["BorderBrush"];

        foreach (var btn in new[] { FilterAllButton, FilterGoalButton, FilterKickoffButton, FilterResultButton })
        {
            bool isActive = (btn.Tag as string) == _activeFilter;
            btn.Background = isActive ? active : inactive;
            btn.Foreground = isActive ? activeText : inactiveText;
            btn.BorderBrush = isActive ? active : border;
        }
    }
}
