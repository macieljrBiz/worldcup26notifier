using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WorldCupNotifier;

public partial class MatchesView : UserControl
{
    public MatchesView()
    {
        InitializeComponent();
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        var view = CollectionViewSource.GetDefaultView(mainWindow.GetMatches());
        if (view is ListCollectionView lcv)
        {
            lcv.CustomSort = new MatchLiveFirstComparer();
        }
        MatchesItemsControl.ItemsSource = view;
    }

    private void MatchesItemsControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Walk up the visual tree from the clicked element to find the ContentPresenter
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not ContentPresenter)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is ContentPresenter cp && cp.Content is Match match)
        {
            match.IsExpanded = !match.IsExpanded;
        }
    }

    // Sorts: IN_PLAY/PAUSED first → TIMED/SCHEDULED by date → FINISHED last
    private sealed class MatchLiveFirstComparer : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = x as Match;
            var b = y as Match;
            return Priority(a).CompareTo(Priority(b)) is int d and not 0 ? d
                : (a?.UtcDate ?? DateTimeOffset.MinValue).CompareTo(b?.UtcDate ?? DateTimeOffset.MinValue);
        }

        private static int Priority(Match? m) => m?.Status switch
        {
            "IN_PLAY" or "PAUSED" => 0,
            "TIMED" or "SCHEDULED" => 1,
            _ => 2
        };
    }
}
