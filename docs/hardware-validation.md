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

## 2026-09-13 release acceptance

The final 1.1.0 installer completed on the local Windows 11 host with exit code `0`; the complete installer transcript is retained locally outside the repository. Windows reported package `LumaTherm_1.1.0.0_x64__jzd30fs6ag6cm` as `Ok` with application id `LumaTherm` and extension `com.microsoft.windows.lighting`. The installed executable at `C:\Program Files\LumaTherm\payload\app\LumaTherm.exe` had signature status `Valid`, signed by the local public release identity `CN=LumaTherm Local` with thumbprint `62A7CEF862A784C02046DF1A2572CAA2E421BC68`.

The installer created the requested Desktop shortcut and the common Start Menu shortcut. Its guarded same-version replacement preserved the existing settings file byte-for-byte (`01C6B811B2AEFE3CA044D75F02B8C1E52678F30906788946E8B3E48333D7CB95`). The public certificate remains in the machine trusted-root store while this locally signed installation is present; the uninstaller is limited to removing that exact bundled certificate. The temporary private key and build PFX were removed after signing.

The final artifacts are `LumaTherm-1.1.0-win-x64-setup.exe` (`BD8D1C8E251EEACE9AD30C0FBB09B64E43968390539D21057E572CC4BBC396C4`) and `LumaTherm-1.1.0-portable-win-x64.zip` (`AD655A1CBF1E6E48F34BE851D6473F9459ACDA2DBE61B0C3B1F332FDC07754CE`). Release verification passed 619 tests: 125 Core, 113 Infrastructure, 271 App, 19 Smoke, and 91 Packaging.

Windows Dynamic Lighting was enabled and LumaTherm was restored to first place in both the global and GIGABYTE-device background-controller priority lists. With the saved thermal mode enabled, an installed `--autostart` launch remained alive for a 30-second background observation with no main window and no new `lamp.disconnected` event; the last device event was `lamp.connected` for `GIGABYTE Device`. The packaged StartupTask was then enabled through the production UI and read back as enabled. This validates the actual background launch path and StartupTask state, but not a physical reboot of this exact build.

The production UI detected an NVIDIA GeForce RTX 5070 through NVML and the `GIGABYTE Device` through LampArray. The interactive lighting-test window was exercised at 75°C, displayed its saturated preview, and released control back to the enabled thermal mode when closed. Fresh dashboard, settings, and lighting-test captures are stored under `docs/screenshots/`. The user had previously confirmed physical lighting ownership after prioritising LumaTherm; no unsupported raw GIGABYTE HID path was used.

Suspend/resume, sensor-loss recovery, a five-minute soak, and uninstall cleanup were not physically exercised in this final session. Their contracts are covered by automated tests; they must not be presented as additional hardware observations.

Before a physical write, explain that LumaTherm will temporarily take Windows Dynamic Lighting control and will release it afterward, then obtain explicit user confirmation. If no LampArray is found, enable Dynamic Lighting, prioritize LumaTherm, and close only the RGB Fusion page if necessary before retrying. Do not add unsupported GIGABYTE HID writes.
