using LumaTherm.App.Composition;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Diagnostics;
using LumaTherm.Infrastructure.Diagnostics;
using System.IO;
using LumaTherm.Infrastructure.System;
using Windows.ApplicationModel;

namespace LumaTherm.App;

internal static class LightingStartupCheck
{
    private const int WaitingForControl = 10;
    private const int NoDeviceOrSensor = 11;
    private const int CheckFailed = 12;
    private const int SyncDisabled = 13;
    private static readonly TimeSpan CheckWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan StableWindow = TimeSpan.FromSeconds(2);

    internal static int Run()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            return Task.Run(() => CheckAsync(cancellation.Token))
                .WaitAsync(cancellation.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            WriteFailure(exception.Message, exception);
            return CheckFailed;
        }
    }

    private static async Task<int> CheckAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(Package.Current.Id.Name, "LumaTherm", StringComparison.Ordinal))
        {
            return CheckFailed;
        }

        await using var instance = new SingleInstanceCoordinator();
        if (!await instance.TryAcquireAsync(cancellationToken))
        {
            return CheckFailed;
        }

        var store = ProductionAppServices.CreateSettingsStore();
        var settings = (await store.LoadAsync(cancellationToken)).Settings;
        if (!settings.IsModeEnabled)
        {
            return SyncDisabled;
        }

        var lighting = ProductionAppServices.CreateLightingController();
        await using var runtime = ProductionAppServices.CreateRuntime(settings, store, lighting);
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        long? activeSince = null;
        RuntimeSnapshot snapshot;
        do
        {
            snapshot = await runtime.ProcessOnceAsync(cancellationToken);
            if (snapshot is { Status: RuntimeStatus.Active, Color: not null, LightingDevice.IsAvailable: true })
            {
                activeSince ??= clock.GetTimestamp();
                if (clock.GetElapsedTime(activeSince.Value) >= StableWindow)
                {
                    return 0;
                }
            }
            else
            {
                activeSince = null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        } while (clock.GetElapsedTime(started) < CheckWindow);

        var result = Classify(snapshot);
        if (result == CheckFailed)
        {
            WriteFailure(snapshot.Message ?? snapshot.Status.ToString());
        }

        return result;
    }

    internal static int Classify(RuntimeSnapshot snapshot) => snapshot.Status switch
        {
            RuntimeStatus.LightingWaiting or RuntimeStatus.Active => WaitingForControl,
            RuntimeStatus.SensorUnavailable => NoDeviceOrSensor,
            RuntimeStatus.LightingUnavailable when snapshot.LightingDevice is null &&
                snapshot.Message == ThermalRuntime.DeviceNotFoundMessage => NoDeviceOrSensor,
            _ => CheckFailed,
        };

    private static void WriteFailure(string message, Exception? exception = null)
    {
        try
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            using var logger = new RollingFileLogger(Path.Combine(localData, "LumaTherm", "logs", "lumatherm.log"));
            logger.Write(AppLogLevel.Warning, "setup.lighting_failed", message, exception);
        }
        catch (Exception) { }
    }
}
