namespace LumaTherm.Infrastructure.System;

public enum PowerEventKind
{
    Suspend,
    Resume,
}

public sealed class PowerEventArgs(PowerEventKind kind) : EventArgs
{
    public PowerEventKind Kind { get; } = kind;
}

public interface IPowerEventSource : IDisposable
{
    event EventHandler<PowerEventArgs>? PowerEvent;
}
