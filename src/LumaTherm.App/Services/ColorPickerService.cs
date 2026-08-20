using System.Windows.Interop;
using LumaTherm.Core.Colors;

namespace LumaTherm.App.Services;

public sealed class ColorPickerService : IColorPickerService
{
    private readonly Func<RgbColor, nint, RgbColor?> _showDialog;

    public ColorPickerService()
        : this(ShowNativeDialog)
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

    private static RgbColor? ShowNativeDialog(RgbColor current, nint ownerHandle)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
        };
        var result = ownerHandle == 0
            ? dialog.ShowDialog()
            : dialog.ShowDialog(new NativeDialogOwner(ownerHandle));
        return result == System.Windows.Forms.DialogResult.OK
            ? new RgbColor(dialog.Color.R, dialog.Color.G, dialog.Color.B)
            : null;
    }

    private sealed class NativeDialogOwner(nint handle) : System.Windows.Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }
}
