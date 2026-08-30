# Compatibility

## Supported contract

LumaTherm 1.1.0 is an x64 Windows 11 desktop application. It requires:

- Windows 11 version 22H2 or newer;
- an NVIDIA GPU with a working NVIDIA driver for the preferred NVML temperature source;
- a Windows Dynamic Lighting-compatible **Windows LampArray** device for lighting output.

MSI Afterburner shared memory can supply a read-only fallback temperature when it is installed, running, and configured to expose that data. It is not required on a clean Windows 11 install.

## Explicit limits

LumaTherm controls devices only through the Windows LampArray API. It does not guarantee support for all RGB hardware, every motherboard, every USB lighting accessory, or every manufacturer application. It neither ships manufacturer libraries nor sends direct HID commands. A device that is not exposed to Windows as an available LampArray cannot be controlled by LumaTherm.

Another Dynamic Lighting controller can make a device unavailable. Enable Dynamic Lighting in Windows and prioritize LumaTherm over conflicting background controllers. For GIGABYTE devices, close only the RGB Fusion page if it owns the device, then retry discovery; LumaTherm does not manipulate GIGABYTE software.

## Evidence boundary

The source code and packaging tests establish the software contract. Read-only discovery and final manual acceptance (physical color transitions, ownership restoration, install/uninstall, power events, and soak behavior) are distinct records. Consult [hardware validation](hardware-validation.md) for the current evidence scope; do not interpret compatibility language as unrecorded physical-device acceptance.
