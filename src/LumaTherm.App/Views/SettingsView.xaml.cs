using System.Windows;
using System.Windows.Controls;
namespace LumaTherm.App.Views;
public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();
    internal bool HasInvalidNumericInput => Validation.GetHasError(SelectedPointTemperatureEditor)
        || Validation.GetHasError(SmoothingEditor);
    internal bool IsCompactLayout { get; private set; }
    internal int ActiveColumnCount => IsCompactLayout ? 1 : 2;
    private void SettingsView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        IsCompactLayout = e.NewSize.Width < 820;
        LeftColumn.Width = new GridLength(IsCompactLayout ? 1 : 1.1, GridUnitType.Star);
        ColumnGap.Width = new GridLength(IsCompactLayout ? 0 : 16);
        RightColumn.Width = new GridLength(IsCompactLayout ? 0 : 1, GridUnitType.Star);
        Grid.SetColumn(RightSettingsStack, IsCompactLayout ? 0 : 2);
        Grid.SetRow(RightSettingsStack, IsCompactLayout ? 3 : 2);
    }
}
