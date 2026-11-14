# Installation

## Before installing

LumaTherm 1.2.1 supports x64 Windows 11 (22H2 or later), an NVIDIA driver, and a Windows Dynamic Lighting-compatible LampArray device. A clean Windows 11 installation does **not** need .NET, GCC, or MSI Afterburner. The NVIDIA driver and compatible LampArray hardware are still required. MSI Afterburner is only an optional read-only temperature fallback.

Get the setup executable and `SHA256SUMS.txt` only from [GitHub Releases](https://github.com/Spark0896/LumaTherm/releases). Do not infer release availability from this source repository; a final release is available only after it is published there.

## Verify SHA-256

From the directory containing the download:

```powershell
Get-FileHash .\LumaTherm-1.2.1-win-x64-setup.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Compare the hexadecimal hash with the line named `LumaTherm-1.2.1-win-x64-setup.exe`. Stop if it differs.

## Install

1. Sign in with the administrator account that will run LumaTherm and double-click `LumaTherm-1.2.1-win-x64-setup.exe` normally. Let setup request Windows elevation for that same account.
2. Read the certificate prompt. This locally signed release must import its bundled **public** certificate into `LocalMachine\Root` (Trusted Root Certification Authorities) to register the Windows lighting identity. No private key is imported. Declining stops setup before replacement or registration.
3. Choose shortcuts if desired. Setup starts background monitoring as your regular Windows user. **Open LumaTherm** also opens the window. Silent installation starts monitoring in the tray.
4. A new installation selects a compatible LampArray and starts thermal synchronization automatically. Setup enables Dynamic Lighting, puts LumaTherm first for current and future devices, and disables foreground takeover so thermal sync continues when you switch apps. You can change these choices in **Windows lighting settings**.

The sparse lighting identity and preferences belong to one Windows user. Setup verifies that its original, unelevated user and elevated user have the same SID before replacement or certificate import. A standard-user account supplying another administrator's credentials is unsupported. An already elevated launch (including **Run as administrator**), UAC-disabled full-token sessions, and SYSTEM deployments are also rejected because the intended unelevated user cannot be established. Start setup normally from the intended administrator account with UAC enabled. Silent setup has the same account requirement; `/ALLOWCERTIMPORT` approves certificate import only and does not bypass the user check or Windows elevation. Use the same account when uninstalling.

Setup verifies the lighting preferences and saves their previous values locally. Uninstall restores values still matching the installer's configuration; a provider order changed later by the user is preserved. An unrecognized Windows preference format causes a visible installation failure instead of reporting successful configuration.

Setup preserves positive brightness settings and changes an explicit zero to 50% so compatible LEDs can light up. It then checks that LumaTherm can send the thermal color in the original Windows user session. If Windows still withholds control, setup offers a restart. Missing hardware and a disabled saved sync mode are reported in the setup log. Windows reports whether it accepted the command. Physical LED output depends on the device and its firmware.

The **Start with Windows** switch is enabled on a fresh installation. Saved preferences
are preserved on upgrades. For an installed copy it
controls the package startup task; Windows can also show this entry in
**Settings → Apps → Startup**. Autostart launches LumaTherm in the background,
so keep at least one tray-menu action enabled if you want a convenient way to
reopen the window.

At removal, the uninstaller unregisters only the exact LumaTherm identity and removes the exact bundled certificate. It separately asks whether to remove `%LOCALAPPDATA%\LumaTherm` settings and logs.

For a folder deployment, use [portable](portable.md) instead.
