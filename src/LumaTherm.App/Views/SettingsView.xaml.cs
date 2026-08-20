using System.Windows;
using System.Windows.Controls;

namespace LumaTherm.App.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private const double CompactBreakpoint = 980;

    public SettingsView() => InitializeComponent();

    internal bool IsCompactLayout { get; private set; }
    internal int ActiveColumnCount => IsCompactLayout ? 1 : 2;

    private void SettingsView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactBreakpoint;
        if (compact == IsCompactLayout)
        {
            return;
        }

        IsCompactLayout = compact;
        if (compact)
        {
            LeftColumn.Width = new GridLength(1, GridUnitType.Star);
            ColumnGap.Width = new GridLength(0);
            RightColumn.Width = new GridLength(0);
            Grid.SetColumn(RightSettingsStack, 0);
            Grid.SetRow(RightSettingsStack, 3);
            SettingsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RightSettingsStack.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            if (SettingsRoot.RowDefinitions.Count > 3)
            {
                SettingsRoot.RowDefinitions.RemoveAt(3);
            }

            LeftColumn.Width = new GridLength(57, GridUnitType.Star);
            ColumnGap.Width = new GridLength(14);
            RightColumn.Width = new GridLength(43, GridUnitType.Star);
            Grid.SetColumn(RightSettingsStack, 2);
            Grid.SetRow(RightSettingsStack, 2);
            RightSettingsStack.Margin = new Thickness(0);
        }
    }
}
