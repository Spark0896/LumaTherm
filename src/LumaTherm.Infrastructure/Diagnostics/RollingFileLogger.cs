using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LumaTherm.Core.Diagnostics;

namespace LumaTherm.Infrastructure.Diagnostics;

public sealed class RollingFileLogger : IAppLogger, IDisposable
{
    private const int MaximumDataDepth = 8;
    private const int MaximumCollectionItems = 64;
    public const long DefaultMaximumBytes = 1024 * 1024;
    public const int DefaultArchiveCount = 4;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();
    private readonly string _path;
    private readonly long _maximumBytes;
    private readonly int _archiveCount;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public RollingFileLogger(
        string path,
        long maximumBytes = DefaultMaximumBytes,
        int archiveCount = DefaultArchiveCount,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes < 128) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (archiveCount < 0) throw new ArgumentOutOfRangeException(nameof(archiveCount));
        _path = path;
        _maximumBytes = maximumBytes;
        _archiveCount = archiveCount;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Write(
        AppLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var record = CreateBoundedRecord(new LogEntry(
                    _timeProvider.GetUtcNow(),
                    level,
                    Sanitize(eventName),
                    Sanitize(message),
                    exception,
                    SanitizeData(data)));
                var existingLength = File.Exists(_path) ? new FileInfo(_path).Length : 0;
                if (existingLength > 0 && existingLength + record.Length > _maximumBytes)
                {
                    Rotate();
                }
                using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(record);
                stream.Flush();
            }
            catch (Exception failure)
            {
                Debug.WriteLine($"LumaTherm logging failure: {failure}");
            }
        }
    }

    public void Dispose()
    {
        lock (_sync) _disposed = true;
    }

    private byte[] CreateBoundedRecord(LogEntry entry)
    {
        var eventName = entry.EventName;
        var message = entry.Message;
        var exceptionMessage = entry.Exception is null ? null : Sanitize(entry.Exception.Message);
        var stackTrace = entry.Exception?.StackTrace is null ? null : Sanitize(entry.Exception.StackTrace);
        var data = entry.Data;
        var truncated = false;
        while (true)
        {
            var bytes = Serialize(entry, eventName, message, exceptionMessage, stackTrace, data, truncated);
            if (bytes.Length <= _maximumBytes) return bytes;
            truncated = true;
            if (data is not null) data = null;
            else if (!string.IsNullOrEmpty(stackTrace)) stackTrace = Reduce(stackTrace);
            else if (!string.IsNullOrEmpty(exceptionMessage)) exceptionMessage = Reduce(exceptionMessage);
            else if (message.Length > 0) message = Reduce(message);
            else if (eventName.Length > 0) eventName = Reduce(eventName);
            else return Serialize(entry with { Exception = null, Data = null }, string.Empty, string.Empty, null, null, null, true);
        }
    }

    private static byte[] Serialize(
        LogEntry entry,
        string eventName,
        string message,
        string? exceptionMessage,
        string? stackTrace,
        IReadOnlyDictionary<string, object?>? data,
        bool truncated)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", entry.Timestamp);
            writer.WriteString("level", entry.Level.ToString());
            writer.WriteString("eventName", eventName);
            writer.WriteString("message", message);
            if (entry.Exception is not null)
            {
                writer.WriteStartObject("exception");
                writer.WriteString("type", entry.Exception.GetType().FullName);
                writer.WriteString("message", exceptionMessage);
                if (stackTrace is not null) writer.WriteString("stackTrace", stackTrace);
                writer.WriteEndObject();
            }
            if (data is { Count: > 0 })
            {
                writer.WritePropertyName("data");
                JsonSerializer.Serialize(writer, data);
            }
            if (truncated) writer.WriteBoolean("truncated", true);
            writer.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    private void Rotate()
    {
        if (_archiveCount == 0)
        {
            File.Delete(_path);
            return;
        }
        File.Delete(ArchivePath(_archiveCount));
        for (var index = _archiveCount - 1; index >= 1; index--)
        {
            var source = ArchivePath(index);
            if (File.Exists(source)) File.Move(source, ArchivePath(index + 1));
        }
        if (File.Exists(_path)) File.Move(_path, ArchivePath(1));
    }

    private string ArchivePath(int index)
    {
        var directory = Path.GetDirectoryName(_path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(_path);
        return Path.Combine(directory, $"{name}.{index}{Path.GetExtension(_path)}");
    }

    private static string Reduce(string value) => value.Length <= 1 ? string.Empty : value[..(value.Length / 2)];

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }
        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, object?>? SanitizeData(IReadOnlyDictionary<string, object?>? data)
    {
        if (data is null) return null;
        var path = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return NormalizeDictionary(data, depth: 0, path);
    }

    private static IReadOnlyDictionary<string, object?> NormalizeDictionary(
        IEnumerable<KeyValuePair<string, object?>> data,
        int depth,
        HashSet<object> path)
    {
        var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
        var count = 0;
        foreach (var pair in data)
        {
            if (count++ >= MaximumCollectionItems)
            {
                safe["[truncated]"] = true;
                break;
            }
            var key = Sanitize(pair.Key);
            if (IsSecretKey(key)) continue;
            safe[key] = NormalizeValue(pair.Value, depth + 1, path);
        }
        return safe;
    }

    private static object? NormalizeValue(object? value, int depth, HashSet<object> path)
    {
        if (value is null) return null;
        if (depth > MaximumDataDepth) return "[max-depth]";
        if (value is string text) return Sanitize(text);
        if (value is char character) return Sanitize(character.ToString());
        if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            or DateTime or DateTimeOffset or DateOnly or TimeOnly or Guid)
        {
            return value;
        }
        if (value is Enum) return Sanitize(value.ToString());

        if (!path.Add(value)) return "[cycle]";
        try
        {
            if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
            {
                return NormalizeDictionary(readOnlyDictionary, depth, path);
            }
            if (value is IDictionary dictionary)
            {
                return NormalizeDictionary(dictionary, depth, path);
            }
            if (value is IEnumerable enumerable)
            {
                var safe = new List<object?>();
                foreach (var item in enumerable)
                {
                    if (safe.Count >= MaximumCollectionItems)
                    {
                        safe.Add("[truncated]");
                        break;
                    }
                    safe.Add(NormalizeValue(item, depth + 1, path));
                }
                return safe;
            }
            return $"[unsupported:{Sanitize(value.GetType().Name)}]";
        }
        finally
        {
            path.Remove(value);
        }
    }

    private static IReadOnlyDictionary<string, object?> NormalizeDictionary(
        IDictionary data,
        int depth,
        HashSet<object> path)
    {
        var safe = new Dictionary<string, object?>(StringComparer.Ordinal);
        var count = 0;
        foreach (DictionaryEntry entry in data)
        {
            if (count++ >= MaximumCollectionItems)
            {
                safe["[truncated]"] = true;
                break;
            }
            if (entry.Key is not string rawKey) continue;
            var key = Sanitize(rawKey);
            if (IsSecretKey(key)) continue;
            safe[key] = NormalizeValue(entry.Value, depth + 1, path);
        }
        return safe;
    }

    private static bool IsSecretKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("certificate", StringComparison.OrdinalIgnoreCase);
}
