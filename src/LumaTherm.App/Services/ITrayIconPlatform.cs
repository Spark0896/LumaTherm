namespace LumaTherm.App.Services;

public sealed record TrayMenuEntry(string Label, bool Enabled);

public sealed record TrayMenuState(string Tooltip, string DeviceStatus, IReadOnlyList<TrayMenuEntry> Entries);

public interface ITrayIconPlatform : IDisposable
{
    event EventHandler? LeftClick;
    event EventHandler? DoubleClick;
    event EventHandler? OpenRequested;
    event EventHandler? ToggleRequested;
    event EventHandler? ExitRequested;
    bool Visible { get; set; }
    TrayMenuState? MenuState { get; set; }
    void ShowNotification(string title, string message);
}

public interface ITrayWindow
{
    bool IsVisible { get; }
    bool IsMinimized { get; }
    void Show();
    void Restore();
    void Activate();
    void BringToFront();
}

public interface ITrayApplication
{
    void RequestShutdown();
}
