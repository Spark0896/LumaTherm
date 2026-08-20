using LumaTherm.Core.Colors;

namespace LumaTherm.App.Services;

public interface IColorPickerService
{
    RgbColor? Pick(RgbColor current);
}

internal sealed class NullColorPickerService : IColorPickerService
{
    public static NullColorPickerService Instance { get; } = new();

    private NullColorPickerService()
    {
    }

    public RgbColor? Pick(RgbColor current) => null;
}
