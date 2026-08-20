using LumaTherm.Core.System;

namespace LumaTherm.Infrastructure.System;

public sealed class FileSessionSentinel : ISessionSentinel
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _begun;
    private bool _priorSessionWasUnclean;
    private bool _completed;

    public FileSessionSentinel(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public async Task<bool> BeginAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_begun) return _priorSessionWasUnclean;
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            _priorSessionWasUnclean = File.Exists(_path);
            var temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(temporaryPath, $"{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}", cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
            _begun = true;
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
            File.Delete(_path);
            _completed = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
