# LumaTherm

LumaTherm changes Windows Dynamic Lighting fan colors from the GPU temperature: cold blue, warm yellow, and hot red. It uses NVIDIA NVML first and MSI Afterburner shared memory as an optional fallback.

## Requirements

Windows 11 with Dynamic Lighting enabled and a compatible LampArray device are required. The only lighting path is `Windows.Devices.Lights.LampArray`; LumaTherm does not use vendor DLLs or raw HID writes. NVIDIA hardware is supported through NVML; MSI Afterburner may provide a read-only fallback temperature when its shared memory is available.

## Installation and first run

The controller-gated package will be installed with `dist/install.ps1`. On first launch, confirm that mode and autostart are off. The dashboard shows the current GPU temperature and a 60-second history. Enable mode only after Dynamic Lighting has discovered the device.

## Daily use

Edit threshold/color profiles in Settings; temperatures must satisfy cold < warm < hot. Closing the window hides LumaTherm to the tray. The tray menu can open, toggle, or exit the app. Autostart is optional and should remain off until explicitly enabled.

LumaTherm releases Dynamic Lighting when mode is disabled or the app exits. Keep GIGABYTE Control Center available for its normal use. If discovery is unavailable, enable Windows Dynamic Lighting, prioritize LumaTherm above conflicting background controllers, and if necessary close only the RGB Fusion page before rediscovering. LumaTherm does not edit, stop, or inspect GIGABYTE software.

Logs are stored under `%LOCALAPPDATA%\LumaTherm\logs`. Use Apps & Features or the packaged uninstaller to remove the installed app. The current hardware acceptance, tray behavior, autostart, recovery, suspend/resume, soak, uninstall, and visual color checks remain controller-gated; see `docs/hardware-validation.md`.

## Safe diagnostics

The smoke CLI is read-only for `sensor` and `lights`:

```powershell
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- sensor --json
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- sensor --skip-nvml --json
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- lights --json
```

`cycle` and `simulate` can write physical lighting only with `--confirm-light-write` and an interactive `YES`. They are intentionally not run until the controller obtains explicit user confirmation.
