using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace LumaTherm.App.Views;

public partial class DashboardView : UserControl
{
    private const double CompactThreshold = 980;

    public DashboardView()
    {
        InitializeComponent();
        ApplyLayout(ActualWidth);
    }

    internal bool IsCompactLayout { get; private set; }
    internal int ActiveTopColumnCount => IsCompactLayout ? 1 : 2;
    internal int ActiveBottomColumnCount => IsCompactLayout ? 1 : 2;

    private void OnDashboardSizeChanged(object sender, SizeChangedEventArgs args) => ApplyLayout(args.NewSize.Width);

    private void ApplyLayout(double width)
    {
        var compact = width > 0 && width < CompactThreshold;
        if (compact == IsCompactLayout && width > 0) return;
        IsCompactLayout = compact;

        TopRingColumn.Width = new GridLength(compact ? 1 : 230, compact ? GridUnitType.Star : GridUnitType.Pixel);
        TopGapColumn.Width = new GridLength(compact ? 0 : 15);
        TopProfileColumn.Width = new GridLength(compact ? 0 : 1, compact ? GridUnitType.Pixel : GridUnitType.Star);
        TopFirstRow.Height = new GridLength(236);
        TopCompactGapRow.Height = new GridLength(compact ? 15 : 0);
        TopSecondRow.Height = new GridLength(compact ? 236 : 0);
        Grid.SetColumn(ProfilePanel, compact ? 0 : 2);
        Grid.SetRow(ProfilePanel, compact ? 2 : 0);

        MonitorColumn.Width = new GridLength(compact ? 1 : 1.35, GridUnitType.Star);
        BottomGapColumn.Width = new GridLength(compact ? 0 : 15);
        BehaviorColumn.Width = new GridLength(compact ? 0 : 0.65, compact ? GridUnitType.Pixel : GridUnitType.Star);
        BottomFirstRow.Height = new GridLength(250);
        BottomCompactGapRow.Height = new GridLength(compact ? 15 : 0);
        BottomSecondRow.Height = new GridLength(compact ? 250 : 0);
        Grid.SetColumn(BehaviorPanel, compact ? 0 : 2);
        Grid.SetRow(BehaviorPanel, compact ? 2 : 0);
    }
}
