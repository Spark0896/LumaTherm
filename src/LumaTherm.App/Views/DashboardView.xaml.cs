using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
namespace LumaTherm.App.Views;
public partial class DashboardView : UserControl
{
    private const double CompactThreshold = 820;
    public DashboardView() { InitializeComponent(); ApplyLayout(ActualWidth); }
    internal bool IsCompactLayout { get; private set; }
    internal int ActiveTopColumnCount => IsCompactLayout ? 1 : 2;
    internal int ActiveBottomColumnCount => IsCompactLayout ? 1 : 2;
    private void OnDashboardSizeChanged(object sender, SizeChangedEventArgs args) => ApplyLayout(args.NewSize.Width);
    private void ApplyLayout(double width)
    {
        var compact = width > 0 && width < CompactThreshold;
        IsCompactLayout = compact;
        TopRingColumn.Width = new GridLength(compact ? 1 : 1.05, GridUnitType.Star);
        TopGapColumn.Width = new GridLength(compact ? 0 : 16);
        TopProfileColumn.Width = new GridLength(compact ? 0 : 1, GridUnitType.Star);
        TopFirstRow.Height = new GridLength(260);
        TopCompactGapRow.Height = new GridLength(compact ? 16 : 0);
        TopSecondRow.Height = new GridLength(compact ? 240 : 0);
        Grid.SetColumn(MonitorPanel, compact ? 0 : 2); Grid.SetRow(MonitorPanel, compact ? 2 : 0);
        MonitorColumn.Width = new GridLength(compact ? 1 : 1.05, GridUnitType.Star);
        BottomGapColumn.Width = new GridLength(compact ? 0 : 16);
        BehaviorColumn.Width = new GridLength(compact ? 0 : 1, GridUnitType.Star);
        BottomFirstRow.Height = new GridLength(230);
        BottomCompactGapRow.Height = new GridLength(compact ? 16 : 0);
        BottomSecondRow.Height = new GridLength(compact ? 220 : 0);
        Grid.SetColumn(ProfilePanel, compact ? 0 : 2); Grid.SetRow(ProfilePanel, compact ? 2 : 0);
    }
}
