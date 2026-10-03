using System.Windows.Interop;
using LumaTherm.Core.Colors;

namespace LumaTherm.App.Services;

public sealed class ColorPickerService : IColorPickerService
{
    private readonly Func<RgbColor, nint, RgbColor?> _showDialog;

    public ColorPickerService()
        : this(ShowDialog)
    {
    }

    internal ColorPickerService(Func<RgbColor, nint, RgbColor?> showDialog) =>
        _showDialog = showDialog ?? throw new ArgumentNullException(nameof(showDialog));

    public RgbColor? Pick(RgbColor current) => _showDialog(current, ResolveOwnerHandle());

    private static nint ResolveOwnerHandle()
    {
        var application = System.Windows.Application.Current;
        var owner = application?.Windows.OfType<System.Windows.Window>().FirstOrDefault(window => window.IsActive)
            ?? application?.MainWindow;
        if (owner is null)
        {
            return 0;
        }

        try
        {
            return new WindowInteropHelper(owner).EnsureHandle();
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static RgbColor? ShowDialog(RgbColor current, nint ownerHandle)
    {
        var dialog = new LumaTherm.App.Views.ColorPickerWindow(current);
        if (ownerHandle != 0) new WindowInteropHelper(dialog).Owner = ownerHandle;
        return dialog.ShowDialog() == true ? dialog.SelectedColor : null;
    }

}
