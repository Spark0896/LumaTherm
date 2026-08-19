using System.Runtime.InteropServices;

namespace LumaTherm.Infrastructure.Sensors;

public sealed class NvmlApi : INvmlApi
{
    private const uint GpuTemperatureSensor = 0;

    public int Initialize() => NvmlInitV2();

    public int Shutdown() => NvmlShutdown();

    public int GetDeviceHandle(uint index, out nint handle) => NvmlDeviceGetHandleByIndexV2(index, out handle);

    public int GetDeviceName(nint handle, byte[] buffer) => NvmlDeviceGetName(handle, buffer, (uint)buffer.Length);

    public int GetTemperature(nint handle, out uint temperature) => NvmlDeviceGetTemperature(handle, GpuTemperatureSensor, out temperature);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlInit_v2")]
    private static extern int NvmlInitV2();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlShutdown")]
    private static extern int NvmlShutdown();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int NvmlDeviceGetHandleByIndexV2(uint index, out nint handle);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetName")]
    private static extern int NvmlDeviceGetName(nint handle, [Out] byte[] name, uint length);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int NvmlDeviceGetTemperature(nint handle, uint sensorType, out uint temperature);
}
