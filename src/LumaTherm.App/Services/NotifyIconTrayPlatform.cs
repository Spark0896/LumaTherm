using System.Drawing;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace LumaTherm.App.Services;

public sealed class NotifyIconTrayPlatform : ITrayIconPlatform
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    private TrayMenuState? _menuState;
    private bool _disposed;

    public NotifyIconTrayPlatform() : this(LoadOwnedIcon(), new Forms.NotifyIcon())
    {
    }

    internal NotifyIconTrayPlatform(Icon ownedIcon, Forms.NotifyIcon icon)
    {
        _icon = icon ?? throw new ArgumentNullException(nameof(icon));
        _ownedIcon = ownedIcon ?? throw new ArgumentNullException(nameof(ownedIcon));
        _icon.Icon = _ownedIcon;
        _icon.ContextMenuStrip = null;
        _icon.Visible = false;
        _icon.MouseClick += OnMouseClick;
        _icon.DoubleClick += OnDoubleClick;
    }

    private static Icon LoadOwnedIcon()
    {
        var resource = WpfApplication.GetResourceStream(new Uri("pack://application:,,,/Assets/LumaTherm.ico"))
            ?? throw new InvalidOperationException("The embedded LumaTherm icon was not found.");
        using (resource.Stream)
        {
            using var loadedIcon = new Icon(resource.Stream);
            return (Icon)loadedIcon.Clone();
        }
    }

    public event EventHandler? LeftClick;
    public event EventHandler? DoubleClick;
    public event EventHandler<TrayCommandKind>? CommandRequested;

    public bool Visible
    {
        get => !_disposed && _icon.Visible;
        set => InvokeSynchronously(() => _icon.Visible = value);
    }

    public TrayMenuState? MenuState
    {
        get => _menuState;
        set
        {
            _menuState = value;
            Invoke(() => RebuildMenu(value));
        }
    }

    public void ShowNotification(string title, string message) =>
        Invoke(() => _icon.ShowBalloonTip(4000, title, message, Forms.ToolTipIcon.Warning));

    public void Dispose()
    {
        if (_context is not null && !ReferenceEquals(SynchronizationContext.Current, _context))
        {
            _context.Send(_ => DisposeCore(), null);
            return;
        }
        DisposeCore();
    }

    private void DisposeCore()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.MouseClick -= OnMouseClick;
        _icon.DoubleClick -= OnDoubleClick;
        _icon.Visible = false;
        var menu = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = null;
        DisposeMenu(menu);
        _icon.Dispose();
        _ownedIcon.Dispose();
        LeftClick = null;
        DoubleClick = null;
        CommandRequested = null;
    }

    private void RebuildMenu(TrayMenuState? state)
    {
        Forms.ContextMenuStrip? replacement = null;
        if (state is not null)
        {
            replacement = new Forms.ContextMenuStrip();
            try
            {
                foreach (var entry in state.Entries)
                {
                    var item = new Forms.ToolStripMenuItem(entry.Label)
                    {
                        Enabled = entry.Enabled,
                        Tag = entry.Kind,
                        ToolTipText = entry.Kind == TrayCommandKind.Temperature ? state.DeviceStatus : string.Empty,
                    };
                    if (entry.Kind != TrayCommandKind.Temperature)
                    {
                        item.Click += OnCommandClick;
                    }
                    replacement.Items.Add(item);
                }
                _icon.Text = state.Tooltip;
            }
            catch
            {
                DisposeMenu(replacement);
                throw;
            }
        }

        var previous = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = replacement;
        DisposeMenu(previous);
    }

    private void DisposeMenu(Forms.ContextMenuStrip? menu)
    {
        if (menu is null) return;
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.Click -= OnCommandClick;
        }
        menu.Dispose();
    }

    private void InvokeSynchronously(Action action)
    {
        if (_disposed) return;
        if (_context is not null && !ReferenceEquals(SynchronizationContext.Current, _context))
        {
            _context.Send(_ => { if (!_disposed) action(); }, null);
            return;
        }
        action();
    }

    private void Invoke(Action action)
    {
        if (_disposed) return;
        if (_context is not null && !ReferenceEquals(SynchronizationContext.Current, _context))
        {
            _context.Post(_ => { if (!_disposed) action(); }, null);
            return;
        }
        action();
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs args)
    {
        if (args.Button == Forms.MouseButtons.Left) LeftClick?.Invoke(this, EventArgs.Empty);
    }
    private void OnDoubleClick(object? sender, EventArgs args) => DoubleClick?.Invoke(this, EventArgs.Empty);
    private void OnCommandClick(object? sender, EventArgs args)
    {
        if (sender is Forms.ToolStripItem { Tag: TrayCommandKind kind } && kind != TrayCommandKind.Temperature)
        {
            CommandRequested?.Invoke(this, kind);
        }
    }
}
