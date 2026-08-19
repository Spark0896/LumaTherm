namespace LumaTherm.Infrastructure.Lighting;

internal sealed class LampArrayAvailabilityState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AvailabilityEntry> _entries = new(StringComparer.Ordinal);
    private long _generation;

    public long CaptureGeneration()
    {
        lock (_sync)
        {
            return _generation;
        }
    }

    public void RecordWatcherUpdate(string id, bool isAvailable)
    {
        lock (_sync)
        {
            _generation++;
            _entries[id] = new AvailabilityEntry(isAvailable, _generation);
        }
    }

    public void ApplySnapshot(string id, bool isAvailable, long snapshotGeneration)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(id, out var current) || current.Generation <= snapshotGeneration)
            {
                _entries[id] = new AvailabilityEntry(isAvailable, snapshotGeneration);
            }
        }
    }

    public bool IsAvailable(string id)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(id, out var entry) && entry.IsAvailable;
        }
    }

    private sealed record AvailabilityEntry(bool IsAvailable, long Generation);
}
