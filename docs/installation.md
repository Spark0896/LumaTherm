# Installation

## Before installing

LumaTherm 1.2.0 supports x64 Windows 11 (22H2 or later), an NVIDIA driver, and a Windows Dynamic Lighting-compatible LampArray device. A clean Windows 11 installation does **not** need .NET, GCC, or MSI Afterburner. The NVIDIA driver and compatible LampArray hardware are still required. MSI Afterburner is only an optional read-only temperature fallback.

Get the setup executable and `SHA256SUMS.txt` only from [GitHub Releases](https://github.com/Spark0896/LumaTherm/releases). Do not infer release availability from this source repository; a final release is available only after it is published there.

## Verify SHA-256

From the directory containing the download:

```powershell
Get-FileHash .\LumaTherm-1.2.0-win-x64-setup.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Compare the hexadecimal hash with the line named `LumaTherm-1.2.0-win-x64-setup.exe`. Stop if it differs.

## Install

1. Run `LumaTherm-1.2.0-win-x64-setup.exe` as Administrator.
2. Read the certificate prompt. This locally signed release must import its bundled **public** certificate into `LocalMachine\Root` (Trusted Root Certification Authorities) to register the Windows lighting identity. No private key is imported. Declining stops setup before replacement or registration.
3. Choose shortcuts if desired. Leave **Launch LumaTherm** selected to open the app after installation as your regular Windows user. Silent installation does not open a window.
4. A new installation selects a compatible LampArray and starts thermal synchronization automatically. Setup enables Dynamic Lighting, puts LumaTherm first for current and future devices, and disables foreground takeover so thermal sync continues when you switch apps. You can change these choices in **Windows lighting settings**.

Setup verifies the lighting preferences and saves their previous values locally. Uninstall restores values still matching the installer's configuration; a provider order changed later by the user is preserved. An unrecognized Windows preference format causes a visible installation failure instead of reporting successful configuration.

Setup offers to restart Windows. The current lighting session can retain its old
controller order after the preferences are written; restarting starts a new session
with the saved configuration. Save your work before accepting the restart.

The **Start with Windows** switch is enabled on a fresh installation. Saved preferences
are preserved on upgrades. For an installed copy it
controls the package startup task; Windows can also show this entry in
**Settings → Apps → Startup**. Autostart launches LumaTherm in the background,
so keep at least one tray-menu action enabled if you want a convenient way to
reopen the window.

At removal, the uninstaller unregisters only the exact LumaTherm identity and removes the exact bundled certificate. It separately asks whether to remove `%LOCALAPPDATA%\LumaTherm` settings and logs.

For a moveable folder deployment, use [portable](portable.md) instead.
