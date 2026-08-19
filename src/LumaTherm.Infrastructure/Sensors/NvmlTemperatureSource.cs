using System.Text;
using LumaTherm.Core.Sensors;

namespace LumaTherm.Infrastructure.Sensors;

public sealed class NvmlTemperatureSource : ITemperatureSource
{
    private const string SourceName = "NVML";

    private readonly INvmlApi _api;
    private readonly TimeProvider _timeProvider;
    private bool _isInitialized;
    private bool _isDisposed;

    public NvmlTemperatureSource(INvmlApi api, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _api = api;
        _timeProvider = timeProvider;
    }

    public string Name => SourceName;

    public ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isDisposed)
        {
            return ValueTask.FromResult<TemperatureReading?>(null);
        }

        try
        {
            if (!_isInitialized)
            {
                if (_api.Initialize() != 0)
                {
                    return ValueTask.FromResult<TemperatureReading?>(null);
                }

                _isInitialized = true;
            }

            if (_api.GetDeviceHandle(0, out var handle) != 0)
            {
                return ValueTask.FromResult<TemperatureReading?>(null);
            }

            var buffer = new byte[96];
            if (_api.GetDeviceName(handle, buffer) != 0 || _api.GetTemperature(handle, out var temperature) != 0)
            {
                return ValueTask.FromResult<TemperatureReading?>(null);
            }

            var celsius = (double)temperature;
            if (!double.IsFinite(celsius) || celsius > 120)
            {
                return ValueTask.FromResult<TemperatureReading?>(null);
            }

            var nullTerminator = Array.IndexOf(buffer, (byte)0);
            var deviceName = Encoding.UTF8.GetString(buffer, 0, nullTerminator >= 0 ? nullTerminator : buffer.Length);
            return ValueTask.FromResult<TemperatureReading?>(new TemperatureReading(celsius, SourceName, deviceName, _timeProvider.GetUtcNow()));
        }
        catch (DllNotFoundException)
        {
            return ValueTask.FromResult<TemperatureReading?>(null);
        }
        catch (EntryPointNotFoundException)
        {
            return ValueTask.FromResult<TemperatureReading?>(null);
        }
        catch (BadImageFormatException)
        {
            return ValueTask.FromResult<TemperatureReading?>(null);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            if (_isInitialized)
            {
                _api.Shutdown();
                _isInitialized = false;
            }
        }

        return ValueTask.CompletedTask;
    }
}
