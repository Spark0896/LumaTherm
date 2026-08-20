using LumaTherm.Infrastructure.System;

namespace LumaTherm.Infrastructure.Tests.System;

public sealed class SessionSentinelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LumaTherm.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BeginAsync_FirstRunCreatesMarker_AndCleanStopRemovesIt()
    {
        var path = Path.Combine(_directory, "session.lock");
        var sentinel = new FileSessionSentinel(path);

        Assert.False(await sentinel.BeginAsync(CancellationToken.None));
        Assert.True(File.Exists(path));
        await sentinel.CompleteAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task BeginAsync_ExistingMarkerReportsPriorCrashAndReplacesMarker()
    {
        var path = Path.Combine(_directory, "session.lock");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "stale", TestContext.Current.CancellationToken);
        var sentinel = new FileSessionSentinel(path);

        Assert.True(await sentinel.BeginAsync(CancellationToken.None));
        Assert.NotEqual("stale", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BeginAndComplete_AreIdempotent()
    {
        var path = Path.Combine(_directory, "session.lock");
        var sentinel = new FileSessionSentinel(path);
        Assert.False(await sentinel.BeginAsync(CancellationToken.None));
        Assert.False(await sentinel.BeginAsync(CancellationToken.None));
        await sentinel.CompleteAsync(CancellationToken.None);
        await sentinel.CompleteAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
