# Troubleshooting

## No GPU temperature

Confirm the NVIDIA driver is installed, then run the read-only smoke check:

```powershell
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- sensor --json
```

If NVML is unavailable, the application can try MSI Afterburner shared memory. Start MSI Afterburner and enable its shared-memory interface if you intend to use that fallback. Do not treat a fallback failure as permission to install vendor lighting software or issue hardware writes.

## No LampArray or unavailable lighting

Enable Windows Dynamic Lighting, unplug/reconnect only if Windows normally requires it, and use the read-only check:

```powershell
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- lights --json
```

Prioritize LumaTherm above conflicting controllers. If GIGABYTE Control Center is open, close only its RGB Fusion page and retry discovery. If Windows still does not expose an available LampArray, that device is outside the supported output contract.

## Setup or portable registration fails

Verify the release SHA-256 and the bundled portable `SHA256SUMS.txt`. For registration, use an elevated PowerShell, preserve the extracted folder, and explicitly approve import of the bundled public certificate. A signer mismatch, bad checksum, non-administrator token, declined prompt, or moved portable directory is an intentional stop condition. See [portable](portable.md).

## The app closes or does not retain settings

Check `%LOCALAPPDATA%\LumaTherm\logs`. Corrupted settings are quarantined and defaults restored, so mode remains disabled until you enable it. Do not edit `settings.json` while the app is running. If you need a reset, exit the app and keep a copy of the directory before changing it.

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
