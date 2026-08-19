namespace LumaTherm.Infrastructure.Sensors;

public interface INvmlApi
{
    int Initialize();

    int Shutdown();

    int GetDeviceHandle(uint index, out nint handle);

    int GetDeviceName(nint handle, byte[] buffer);

    int GetTemperature(nint handle, out uint temperature);
}
