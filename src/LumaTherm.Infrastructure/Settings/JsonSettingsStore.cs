using System.Text.Json;
using LumaTherm.Core.Settings;

namespace LumaTherm.Infrastructure.Settings;

public sealed class JsonSettingsStore(string path, TimeProvider timeProvider) : ISettingsStore
{
    private const string RecoveryMessage = "Настройки были повреждены и сброшены";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path = path;
    private readonly string _temporaryPath = path + ".tmp";
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new SettingsLoadResult(AppSettings.FirstRun);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            return new SettingsLoadResult(SettingsMigrator.Migrate(json, SerializerOptions));
        }
        catch (JsonException)
        {
            return Recover();
        }
        catch (InvalidDataException)
        {
            return Recover();
        }
        catch (IOException)
        {
            return Recover();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var stream = new FileStream(_temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(_path))
        {
            File.Replace(_temporaryPath, _path, null);
        }
        else
        {
            File.Move(_temporaryPath, _path);
        }
    }

    private SettingsLoadResult Recover()
    {
        if (File.Exists(_path))
        {
            var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", global::System.Globalization.CultureInfo.InvariantCulture);
            var directory = Path.GetDirectoryName(_path) ?? string.Empty;
            var namePrefix = $"{Path.GetFileNameWithoutExtension(_path)}.corrupt-{timestamp}";
            var extension = Path.GetExtension(_path);
            for (var suffix = 0; ; suffix++)
            {
                var fileName = suffix == 0 ? $"{namePrefix}{extension}" : $"{namePrefix}-{suffix}{extension}";
                var quarantinePath = Path.Combine(directory, fileName);
                if (File.Exists(quarantinePath))
                {
                    continue;
                }

                try
                {
                    File.Move(_path, quarantinePath);
                    break;
                }
                catch (IOException) when (File.Exists(_path) && File.Exists(quarantinePath))
                {
                }
            }
        }

        return new SettingsLoadResult(AppSettings.Default, RecoveryMessage);
    }
}
