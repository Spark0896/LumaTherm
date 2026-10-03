using System.Windows;
using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;
namespace LumaTherm.App.Views;
public partial class ColorPickerWindow : Window
{
    public ColorPickerWindow(RgbColor current) { InitializeComponent(); DataContext = new ColorPickerViewModel(current); }
    public RgbColor SelectedColor => ((ColorPickerViewModel)DataContext).Color;
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RgbColor color }) ((ColorPickerViewModel)DataContext).SetColor(color);
    }
    private void Apply_Click(object sender, RoutedEventArgs e) { if (((ColorPickerViewModel)DataContext).IsValid) DialogResult = true; }
    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
}
