using System.Text;
using LumaTherm.Core.System;

namespace LumaTherm.Infrastructure.System;

internal interface ISessionSentinelFileSystem
{
    bool TryCreateMarker(string path);
    Task UpdateMetadataAsync(string path, string metadata, CancellationToken cancellationToken);
    void DeleteMarker(string path);
}

public sealed class FileSessionSentinel : ISessionSentinel
{
    private readonly string _path;
    private readonly ISessionSentinelFileSystem _fileSystem;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _begun;
    private bool _priorSessionWasUnclean;
    private bool _completed;

    public FileSessionSentinel(string path)
        : this(path, PhysicalSessionSentinelFileSystem.Instance)
    {
    }

    internal FileSessionSentinel(string path, ISessionSentinelFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<bool> BeginAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completed)
            {
                _begun = false;
                _completed = false;
            }
            if (_begun) return _priorSessionWasUnclean;
            _priorSessionWasUnclean = !_fileSystem.TryCreateMarker(_path);
            await _fileSystem.UpdateMetadataAsync(
                _path,
                $"{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}",
                cancellationToken).ConfigureAwait(false);
            _begun = true;
            _completed = false;
            return _priorSessionWasUnclean;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completed || !_begun) return;
            _fileSystem.DeleteMarker(_path);
            _completed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class PhysicalSessionSentinelFileSystem : ISessionSentinelFileSystem
    {
        public static PhysicalSessionSentinelFileSystem Instance { get; } = new();

        public bool TryCreateMarker(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64, FileOptions.WriteThrough);
                stream.Write("pending"u8);
                stream.Flush(flushToDisk: true);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }
        }

        public async Task UpdateMetadataAsync(string path, string metadata, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(metadata);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            stream.SetLength(0);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        public void DeleteMarker(string path) => File.Delete(path);
    }
}
