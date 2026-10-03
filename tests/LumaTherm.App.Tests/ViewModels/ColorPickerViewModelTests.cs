using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;
namespace LumaTherm.App.Tests.ViewModels;
public sealed class ColorPickerViewModelTests
{
    [Fact]
    public void HexAndChannelsStayInSyncWithoutApplyingInvalidInput()
    {
        var vm = new ColorPickerViewModel(new RgbColor(0, 0, 255));
        vm.Hex = "#12AB34";
        Assert.Equal(new RgbColor(0x12, 0xAB, 0x34), vm.Color);
        vm.Red = 255;
        Assert.Equal("#FFAB34", vm.Hex);
        vm.Hex = "bad";
        Assert.False(vm.IsValid);
        Assert.Equal(new RgbColor(255, 0xAB, 0x34), vm.Color);
        vm.Blue = 0;
        Assert.True(vm.IsValid);
        Assert.Equal("#FFAB00", vm.Hex);
    }
}
