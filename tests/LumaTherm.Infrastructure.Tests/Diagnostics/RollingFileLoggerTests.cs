using System.Collections;
using System.Text;
using System.Text.Json;
using LumaTherm.Core.Diagnostics;
using LumaTherm.Infrastructure.Diagnostics;

namespace LumaTherm.Infrastructure.Tests.Diagnostics;

public sealed class RollingFileLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LumaTherm.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_ProducesOneUtf8JsonRecordWithStableFields()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 4096, archiveCount: 4);

        logger.Write(AppLogLevel.Information, "app.start", "Запуск", data: new Dictionary<string, object?> { ["modeEnabled"] = false });

        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain(Encoding.UTF8.Preamble, bytes);
        var line = Assert.Single(File.ReadAllLines(path, Encoding.UTF8));
        using var json = JsonDocument.Parse(line);
        Assert.NotEqual(default, json.RootElement.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal("Information", json.RootElement.GetProperty("level").GetString());
        Assert.Equal("app.start", json.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("Запуск", json.RootElement.GetProperty("message").GetString());
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
        Assert.False(json.RootElement.TryGetProperty("stackTrace", out _));
        Assert.False(json.RootElement.GetProperty("data").GetProperty("modeEnabled").GetBoolean());
    }

    [Fact]
    public void Write_RotatesByUtf8BytesAndRetainsExactlyFourArchives()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 256, archiveCount: 4);

        for (var index = 0; index < 20; index++)
        {
            logger.Write(AppLogLevel.Information, "settings.saved", $"{index:D2}: {new string('Я', 28)}");
        }

        Assert.True(new FileInfo(path).Length <= 256);
        Assert.Equal(4, Directory.GetFiles(_directory, "lumatherm.*.log").Length);
        Assert.False(File.Exists(Path.Combine(_directory, "lumatherm.5.log")));
        foreach (var file in Directory.GetFiles(_directory, "*.log"))
        {
            foreach (var line in File.ReadLines(file, Encoding.UTF8))
            {
                using var _ = JsonDocument.Parse(line);
            }
        }
    }

    [Fact]
    public void ConcurrentWrites_AreCompleteJsonLines()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 128 * 1024, archiveCount: 4);

        Parallel.For(0, 200, index => logger.Write(AppLogLevel.Debug, "sensor.selected", index.ToString()));

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal(200, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public void OversizedRecord_IsTruncatedAndBoundedWithMarker()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 256, archiveCount: 4);

        logger.Write(AppLogLevel.Error, "app.unhandled", new string('x', 20_000), new InvalidOperationException(new string('s', 20_000)));

        Assert.InRange(new FileInfo(path).Length, 1, 256);
        using var json = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void OversizedEventAndData_AreAlsoBounded()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 256, archiveCount: 4);

        logger.Write(
            AppLogLevel.Error,
            new string('e', 20_000),
            "message",
            data: new Dictionary<string, object?> { ["payload"] = new string('d', 20_000) });

        Assert.InRange(new FileInfo(path).Length, 1, 256);
        using var json = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void ControlCharactersAreSanitized_AndFailuresOrDisposeNeverEscape()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        var logger = new RollingFileLogger(path, 1024, archiveCount: 4);
        logger.Write(AppLogLevel.Warning, "lamp.connected\r\nforged", "hello\0world");
        var json = File.ReadAllText(path, Encoding.UTF8);
        Assert.DoesNotContain("\\u0000", json, StringComparison.OrdinalIgnoreCase);
        using (var record = JsonDocument.Parse(json))
        {
            Assert.DoesNotContain('\r', record.RootElement.GetProperty("eventName").GetString()!);
            Assert.DoesNotContain('\n', record.RootElement.GetProperty("eventName").GetString()!);
            Assert.DoesNotContain('\0', record.RootElement.GetProperty("message").GetString()!);
        }

        logger.Dispose();
        logger.Dispose();
        var exception = Record.Exception(() => logger.Write(AppLogLevel.Error, "app.stop", "after dispose"));
        Assert.Null(exception);

        var badLogger = new RollingFileLogger(_directory, 1024, archiveCount: 4);
        exception = Record.Exception(() => badLogger.Write(AppLogLevel.Error, "app.stop", "unwritable target"));
        Assert.Null(exception);
        badLogger.Dispose();
    }

    [Fact]
    public void NestedData_IsRecursivelySanitizedAndNeverSerializesUnsupportedDtos()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 4096, archiveCount: 4);
        var data = new Dictionary<string, object?>
        {
            ["safe\r\nkey"] = new Dictionary<string, object?>
            {
                ["accessToken"] = "must-not-leak",
                ["items"] = new object?[]
                {
                    "hello\0world",
                    new Dictionary<string, object?> { ["password"] = "nested-secret", ["value"] = "ok\u0001" },
                    new SensitiveDto("dto-secret"),
                },
            },
        };

        logger.Write(AppLogLevel.Information, "nested", "data", data: data);

        using var json = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var serialized = json.RootElement.GetRawText();
        Assert.DoesNotContain("must-not-leak", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("dto-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0000", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u0001", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[unsupported:SensitiveDto]", serialized, StringComparison.Ordinal);
        Assert.Contains("hello world", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void CyclicAndDeepData_AreFiniteAndLoggingNeverThrows()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 4096, archiveCount: 4);
        var cycle = new List<object?>();
        cycle.Add(cycle);
        var deep = new Dictionary<string, object?>();
        var cursor = deep;
        for (var index = 0; index < 20; index++)
        {
            var next = new Dictionary<string, object?>();
            cursor["next"] = next;
            cursor = next;
        }

        var exception = Record.Exception(() => logger.Write(
            AppLogLevel.Information,
            "bounded",
            "data",
            data: new Dictionary<string, object?> { ["cycle"] = cycle, ["deep"] = deep }));

        Assert.Null(exception);
        var serialized = File.ReadAllText(path, Encoding.UTF8);
        Assert.InRange(Encoding.UTF8.GetByteCount(serialized), 1, 4096);
        Assert.Contains("[cycle]", serialized, StringComparison.Ordinal);
        Assert.Contains("[max-depth]", serialized, StringComparison.Ordinal);
        using var _ = JsonDocument.Parse(serialized);
    }

    [Fact]
    public void NonGenericDictionary_StopsEnumerationAtTheConfiguredItemBound()
    {
        var path = Path.Combine(_directory, "lumatherm.log");
        using var logger = new RollingFileLogger(path, 16 * 1024, archiveCount: 4);
        var dictionary = new GuardedDictionary(throwAfterMoveNextCalls: 65);

        logger.Write(
            AppLogLevel.Information,
            "bounded-dictionary",
            "data",
            data: new Dictionary<string, object?> { ["dictionary"] = dictionary });

        Assert.True(File.Exists(path));
        Assert.InRange(dictionary.MoveNextCalls, 1, 65);
        using var json = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var normalized = json.RootElement.GetProperty("data").GetProperty("dictionary");
        Assert.True(normalized.GetProperty("[truncated]").GetBoolean());
        Assert.False(normalized.TryGetProperty("accessToken", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed record SensitiveDto(string Secret);

    private sealed class GuardedDictionary(int throwAfterMoveNextCalls) : IDictionary
    {
        public int MoveNextCalls { get; private set; }
        public object? this[object key] { get => null; set => throw new NotSupportedException(); }
        public ICollection Keys => Array.Empty<object>();
        public ICollection Values => Array.Empty<object>();
        public bool IsReadOnly => true;
        public bool IsFixedSize => true;
        public int Count => int.MaxValue;
        public object SyncRoot => this;
        public bool IsSynchronized => false;
        public void Add(object key, object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(object key) => false;
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
        public IDictionaryEnumerator GetEnumerator() => new GuardedEnumerator(this, throwAfterMoveNextCalls);
        public void Remove(object key) => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class GuardedEnumerator(GuardedDictionary owner, int throwAfterMoveNextCalls) : IDictionaryEnumerator
        {
            private int _index = -1;
            public DictionaryEntry Entry => new(Key, Value);
            public object Key => _index == 5 ? "accessToken" : $"key{_index:D2}";
            public object Value => _index == 5 ? "must-not-leak" : _index;
            public object Current => Entry;
            public bool MoveNext()
            {
                owner.MoveNextCalls++;
                if (owner.MoveNextCalls > throwAfterMoveNextCalls) throw new InvalidOperationException("dictionary enumeration exceeded its bound");
                _index++;
                return true;
            }
            public void Reset() => _index = -1;
        }
    }
}
