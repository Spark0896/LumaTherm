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

    [Fact]
    public async Task BeginAsync_AtomicallyEstablishesMarkerBeforeFailableMetadataUpdate()
    {
        var fileSystem = new RecordingSentinelFileSystem { FailUpdate = true };
        var sentinel = new FileSessionSentinel("session.lock", fileSystem);

        await Assert.ThrowsAsync<IOException>(() => sentinel.BeginAsync(TestContext.Current.CancellationToken));

        Assert.True(fileSystem.MarkerExists);
        Assert.Equal(["create-new", "update"], fileSystem.Events);
        Assert.True(fileSystem.MarkerExistedWhenUpdateStarted);
    }

    [Fact]
    public async Task BeginAsync_ExistingMarkerNeverRemovesCrashEvidenceDuringMetadataRefresh()
    {
        var fileSystem = new RecordingSentinelFileSystem { MarkerExists = true };
        var sentinel = new FileSessionSentinel("session.lock", fileSystem);

        Assert.True(await sentinel.BeginAsync(TestContext.Current.CancellationToken));

        Assert.True(fileSystem.MarkerExists);
        Assert.True(fileSystem.MarkerExistedWhenUpdateStarted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingSentinelFileSystem : ISessionSentinelFileSystem
    {
        public bool MarkerExists { get; set; }
        public bool MarkerExistedWhenUpdateStarted { get; private set; }
        public bool FailUpdate { get; set; }
        public List<string> Events { get; } = [];
        public bool TryCreateMarker(string path)
        {
            Events.Add("create-new");
            if (MarkerExists) return false;
            MarkerExists = true;
            return true;
        }
        public Task UpdateMetadataAsync(string path, string metadata, CancellationToken cancellationToken)
        {
            Events.Add("update");
            MarkerExistedWhenUpdateStarted = MarkerExists;
            if (FailUpdate) throw new IOException("metadata");
            return Task.CompletedTask;
        }
        public void DeleteMarker(string path) => MarkerExists = false;
    }
}
