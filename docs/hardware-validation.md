# Hardware validation

## Purpose

This document separates the implemented software contract from evidence produced by manual hardware acceptance. It does not manufacture device observations, screenshots, SHA-256 values, install results, or release-publication claims.

## Safe read-only discovery

These commands may inspect local state but do not take lighting ownership or set a color:

```powershell
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- sensor --json
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- sensor --skip-nvml --json
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- lights --json
```

Capture the actual command, timestamp, operating-system version, anonymized device result where appropriate, and exit code at the time of a validation session. A missing MSI Afterburner reading is actionable only when that optional fallback was deliberately configured and expected.

## Manual acceptance record

Final acceptance has not yet been recorded in this repository. When authorised, record the observed result for physical cold/warm/hot transitions, smooth simulation, controller ownership restoration, package install/first run, tray and autostart, sensor-loss recovery, suspend/resume, five-minute soak, uninstall, release hashes, and visual comparison. Each result must identify the command or user action, timestamp, environment, observed outcome, and any failure; do not extrapolate one result to untested hardware.

## 2026-09-11 installation evidence

The final same-version installation validation completed on the local Windows host at 22:10 MSK with installer exit code `0`. The installed identity was `LumaTherm_1.1.0.0_x64__jzd30fs6ag6cm`, status `Ok`, and exposed the `com.microsoft.windows.lighting` extension. The installed executable signature was `Valid`; its signer was the one-time local release certificate `1710B8B5AD333BCD630106CA6D7DB036F044B320`. Both Desktop and Start-menu shortcuts were created.

The validated public artifacts were `LumaTherm-1.1.0-win-x64-setup.exe` (`FFD546E27F583588C925B4AE782108690B364023CDFCD03E9A2AB9DA82CEDB6D`) and `LumaTherm-1.1.0-portable-win-x64.zip` (`68B1498CF60BC06D47D8AFDEF99A98F2DBA2EF074D91D27DBC505B45E7552215`). The temporary signing PFX, private key, and temporary machine trust entry were removed after signing.

This verifies packaging, registration, and first process launch. It is not physical RGB acceptance: the first start detected an unfinished earlier session and used its safety path to begin the runtime disabled, so the final visual/background-minimize check remains pending explicit user activation of the mode in the app.

### Completed physical acceptance

After LumaTherm was placed above the background Dynamic Lighting controller in Windows settings, the user confirmed that the GIGABYTE device responded to LumaTherm. The thermal mode was enabled, NVML supplied the GPU reading, and the application was minimized to the tray. A 30-second background observation found the LumaTherm process responsive with no visible main window and no new LampArray disconnect event. The user then confirmed that the physical lighting did not reset after minimization. This validates the foreground/tray handoff on this host; the required Windows priority configuration is retained as part of the troubleshooting guidance.

Before a physical write, explain that LumaTherm will temporarily take Windows Dynamic Lighting control and will release it afterward, then obtain explicit user confirmation. If no LampArray is found, enable Dynamic Lighting, prioritize LumaTherm, and close only the RGB Fusion page if necessary before retrying. Do not add unsupported GIGABYTE HID writes.
