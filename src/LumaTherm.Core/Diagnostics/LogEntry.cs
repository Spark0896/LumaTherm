namespace LumaTherm.Core.Diagnostics;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    AppLogLevel Level,
    string EventName,
    string Message,
    Exception? Exception = null,
    IReadOnlyDictionary<string, object?>? Data = null);
