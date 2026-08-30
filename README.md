# LumaTherm

> English · [Русский](README.ru.md)

LumaTherm changes compatible Windows Dynamic Lighting colors from GPU temperature: cold blue, warm yellow, and hot red. It uses NVIDIA NVML first and MSI Afterburner shared memory as an optional read-only fallback.

## Features

- Smooth, editable temperature/color profile with 60-second history and tray control.
- Optional autostart, notifications, and English/Russian/system language selection.
- Direct output through Windows `LampArray`; no vendor DLLs or raw HID writes.
- GitHub stable-release check over HTTPS. It opens a release page/download; it does not download or install updates itself.
- Free and open source under the [MIT License](LICENSE).

## Requirements

LumaTherm 1.1.0 requires x64 Windows 11 (22H2 or later), an NVIDIA driver, and a compatible Windows Dynamic Lighting / LampArray device. A clean Windows 11 installation needs no .NET Runtime, GCC, or MSI Afterburner because the app is self-contained. MSI Afterburner is only an optional read-only fallback sensor. A device must be exposed by Windows as an available LampArray; support for every RGB product or manufacturer application is not promised.

## Screenshots

Screenshots will be linked here after final manual hardware acceptance is recorded. This repository does not present unrecorded visual evidence as a completed result.

## Setup quick start

1. Download `LumaTherm-1.1.0-win-x64-setup.exe` and `SHA256SUMS.txt` only from [GitHub Releases](https://github.com/Spark0896/LumaTherm/releases).
2. Verify the SHA-256 value as described in [installation](docs/installation.md#verify-sha-256).
3. Run setup as Administrator and explicitly approve import of the bundled **public** certificate. It is needed to register the Windows lighting identity; no private key is imported.
4. Start LumaTherm from the chosen shortcut, select a LampArray device, and enable thermal synchronization only after Windows reports it available.

The installer has no post-install launch entry and does not automatically start the application.

## Portable registration

The portable zip contains a self-contained application plus a sparse package identity. Extract it to a permanent local folder, verify its hashes, and use an elevated PowerShell:

```powershell
.\install.ps1 -ConfirmCertificateImport
```

Portable registration is explicit and certificate-gated. It verifies signatures, checksums, and its signed payload anchor before registering the identity against the exact external folder. Do not move or delete that folder while registered; see [portable deployment](docs/portable.md).

## Dynamic Lighting priority

Enable Dynamic Lighting in Windows and prioritize LumaTherm above competing background controllers. If discovery is unavailable, close only the RGB Fusion page in GIGABYTE Control Center if it holds the device, then retry. LumaTherm does not edit, stop, or inspect GIGABYTE software.

## Privacy and troubleshooting

Settings and logs stay under `%LOCALAPPDATA%\LumaTherm`. Update checks request only the [latest GitHub release endpoint](https://api.github.com/repos/Spark0896/LumaTherm/releases/latest). The response must be a published stable semantic-version release; no personal data is sent and no update is silently installed.

Read [troubleshooting](docs/troubleshooting.md) for sensor, LampArray, setup, portable, settings, and update guidance. [Compatibility](docs/compatibility.md) and [hardware validation](docs/hardware-validation.md) explain the evidence boundary.

## Build and contribute

Source builds use the SDK pinned in `global.json` (8.0.423):

```powershell
dotnet restore LumaTherm.sln -p:NuGetAudit=false
dotnet test LumaTherm.sln -c Release -p:NuGetAudit=false
```

See [development](docs/development.md), [contributing](CONTRIBUTING.md), [security](SECURITY.md), [changelog](CHANGELOG.md), and [license](LICENSE). Maintainers should use [releasing](docs/releasing.md); it covers external certificates, manual gates, checksums, and rollback.
