namespace LumaTherm.Core.Diagnostics;

public enum AppLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

public interface IAppLogger
{
    void Write(
        AppLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? data = null);
}
