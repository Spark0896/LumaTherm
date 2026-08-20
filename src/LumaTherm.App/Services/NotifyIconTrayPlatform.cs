using System.Drawing;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace LumaTherm.App.Services;

public sealed class NotifyIconTrayPlatform : ITrayIconPlatform
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    private readonly Forms.ToolStripMenuItem _temperature;
    private readonly Forms.ToolStripMenuItem _open;
    private readonly Forms.ToolStripMenuItem _toggle;
    private readonly Forms.ToolStripMenuItem _exit;
    private TrayMenuState? _menuState;
    private bool _disposed;

    public NotifyIconTrayPlatform()
    {
        var resource = WpfApplication.GetResourceStream(new Uri("pack://application:,,,/Assets/LumaTherm.ico"))
            ?? throw new InvalidOperationException("Встроенная иконка LumaTherm не найдена.");
        using (resource.Stream)
        {
            using var loadedIcon = new Icon(resource.Stream);
            _ownedIcon = (Icon)loadedIcon.Clone();
        }
        _temperature = new Forms.ToolStripMenuItem { Enabled = false };
        _open = new Forms.ToolStripMenuItem("Открыть LumaTherm");
        _toggle = new Forms.ToolStripMenuItem("Включить режим");
        _exit = new Forms.ToolStripMenuItem("Выход");
        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([_temperature, _open, _toggle, _exit]);
        _icon = new Forms.NotifyIcon { Icon = _ownedIcon, ContextMenuStrip = menu, Visible = false };
        _icon.MouseClick += OnMouseClick;
        _icon.DoubleClick += OnDoubleClick;
        _open.Click += OnOpenClick;
        _toggle.Click += OnToggleClick;
        _exit.Click += OnExitClick;
    }

    public event EventHandler? LeftClick;
    public event EventHandler? DoubleClick;
    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? ExitRequested;

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
            Invoke(() =>
            {
                if (value is null) return;
                _icon.Text = value.Tooltip;
                _temperature.Text = value.Entries[0].Label;
                _temperature.Enabled = value.Entries[0].Enabled;
                _temperature.ToolTipText = value.DeviceStatus;
                _open.Text = value.Entries[1].Label;
                _toggle.Text = value.Entries[2].Label;
                _exit.Text = value.Entries[3].Label;
            });
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
        _open.Click -= OnOpenClick;
        _toggle.Click -= OnToggleClick;
        _exit.Click -= OnExitClick;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon.Dispose();
        LeftClick = null;
        DoubleClick = null;
        OpenRequested = null;
        ToggleRequested = null;
        ExitRequested = null;
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
    private void OnOpenClick(object? sender, EventArgs args) => OpenRequested?.Invoke(this, EventArgs.Empty);
    private void OnToggleClick(object? sender, EventArgs args) => ToggleRequested?.Invoke(this, EventArgs.Empty);
    private void OnExitClick(object? sender, EventArgs args) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
