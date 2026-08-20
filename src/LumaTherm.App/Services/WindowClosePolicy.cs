namespace LumaTherm.App.Services;

public enum WindowCloseReason
{
    Close,
    Minimize,
}

public enum WindowCloseDecision
{
    AllowClose,
    HideAndCancel,
}

public sealed class WindowClosePolicy
{
    private readonly Func<bool> _closeToTrayEnabled;
    private int _explicitExitRequested;

    public WindowClosePolicy(Func<bool> closeToTrayEnabled)
    {
        _closeToTrayEnabled = closeToTrayEnabled ?? throw new ArgumentNullException(nameof(closeToTrayEnabled));
    }

    public bool IsExplicitExitRequested => Volatile.Read(ref _explicitExitRequested) != 0;

    public void RequestExplicitExit() => Interlocked.Exchange(ref _explicitExitRequested, 1);

    public WindowCloseDecision Decide(WindowCloseReason reason) =>
        IsExplicitExitRequested || !_closeToTrayEnabled()
            ? WindowCloseDecision.AllowClose
            : WindowCloseDecision.HideAndCancel;
}
