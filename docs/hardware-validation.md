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

Before a physical write, explain that LumaTherm will temporarily take Windows Dynamic Lighting control and will release it afterward, then obtain explicit user confirmation. If no LampArray is found, enable Dynamic Lighting, prioritize LumaTherm, and close only the RGB Fusion page if necessary before retrying. Do not add unsupported GIGABYTE HID writes.
