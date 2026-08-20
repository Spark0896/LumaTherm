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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
