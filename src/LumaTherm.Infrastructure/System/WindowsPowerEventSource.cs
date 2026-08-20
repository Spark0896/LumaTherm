using Microsoft.Win32;

namespace LumaTherm.Infrastructure.System;

public sealed class WindowsPowerEventSource : IPowerEventSource
{
    private bool _disposed;

    public WindowsPowerEventSource()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event EventHandler<PowerEventArgs>? PowerEvent;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        PowerEvent = null;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        var kind = args.Mode switch
        {
            PowerModes.Suspend => PowerEventKind.Suspend,
            PowerModes.Resume => PowerEventKind.Resume,
            _ => (PowerEventKind?)null,
        };

        if (kind is not null)
        {
            PowerEvent?.Invoke(this, new PowerEventArgs(kind.Value));
        }
    }
}
