# Troubleshooting

## No GPU temperature

Confirm the NVIDIA driver is installed. From a source checkout with the pinned .NET SDK installed, run this read-only smoke check from the repository root (a local `.dotnet\dotnet.exe` can also be used):

```powershell
dotnet run --project tools\LumaTherm.Smoke -c Release -- sensor --json
```

If NVML is unavailable, the application can try MSI Afterburner shared memory. Start MSI Afterburner and enable its shared-memory interface if you intend to use that fallback. The reader checks the producer's Unix polling timestamp and rejects data more than five seconds old; configure polling at five seconds or less. A readable shared-memory region alone does not prove a fresh reading. Do not treat a fallback failure as permission to install vendor lighting software or issue hardware writes.

## No LampArray or unavailable lighting

Enable Windows Dynamic Lighting, unplug/reconnect only if Windows normally requires it, and use the read-only check:

```powershell
dotnet run --project tools\LumaTherm.Smoke -c Release -- lights --json
```

Prioritize LumaTherm above conflicting controllers. If GIGABYTE Control Center is open, close only its RGB Fusion page and retry discovery. A device Windows does not expose as a LampArray is outside the supported output contract. If discovery lists it with `available: false`, **Waiting for Windows lighting control** means Windows has not granted ownership; the device is still discovered and LumaTherm waits for control to return. Restarting the app or writing registry priority does not prove that Windows has granted control in the current session.

## Setup or portable registration fails

Verify the release SHA-256 and the bundled portable `SHA256SUMS.txt`. Launch setup normally from the intended administrator account and let it request same-account elevation; already elevated launches and another administrator's credentials are rejected before changes. For portable registration, elevate PowerShell for that same intended account, preserve the extracted folder, and explicitly approve import of the bundled public certificate. A signer mismatch, bad checksum, non-administrator token, declined prompt, or moved portable directory is an intentional stop condition. See [installation](installation.md) and [portable](portable.md).

## The app closes or does not retain settings

Check `%LOCALAPPDATA%\LumaTherm\logs`. Corrupted settings are quarantined and recovery defaults restored, so mode and autostart remain disabled until you enable them. A temporary read or permission failure instead stops startup with an error and preserves the existing file; resolve that access problem before retrying. A first run with no settings file enables mode and autostart. Do not edit `settings.json` while the app is running. If you need a reset, exit the app and keep a copy of the directory before changing it.

## Updating

Use a stable GitHub Release, verify its hashes, then run the newer setup. The update check only reads GitHub release metadata and opens a release download; it does not silently download or install anything. For portable deployments, unregister first, replace the folder with a verified archive, and register again.

## Startup, tray, and profile editing (current source)

Start with Windows applies immediately. If Windows reports a user-disabled task,
open Windows startup settings and enable LumaTherm there. A policy-controlled task
requires a policy change by the administrator. Keep a registered portable folder
in place.

With Minimize to tray enabled, minimize or close hides the main window. Click the
tray icon to restore it; use Exit in its menu to stop the app. Check Windows hidden
tray icons if it is not visible.

Default colors restores blue/green/red without resetting other preferences. Drag
points, use arrows/Delete or Add point/Remove. Correct invalid temperature
(0–120°C) and smoothing (0.1–5 seconds) before saving or testing. Test Apply saves
the draft; Cancel/close ends the temporary test and restores the prior mode.
