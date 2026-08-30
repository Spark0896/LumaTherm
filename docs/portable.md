# Portable deployment

The portable archive is intentionally not a registration-free executable. It contains `app\LumaTherm.exe`, `LumaTherm-1.1.0-sparse.msix`, `LumaTherm.cer`, registration helpers, a signed payload anchor, and `SHA256SUMS.txt`. The sparse identity is required for Windows lighting integration and must be registered explicitly.

## Install from the archive

1. Download `LumaTherm-1.1.0-portable-win-x64.zip` and the release `SHA256SUMS.txt` from [GitHub Releases](https://github.com/Spark0896/LumaTherm/releases).
2. Before extraction, from the download directory, verify the archive hash:

```powershell
Get-FileHash .\LumaTherm-1.1.0-portable-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Compare the result with the line named `LumaTherm-1.1.0-portable-win-x64.zip`; stop if it differs.
3. Extract the zip to a permanent local folder. Do not use a temporary download directory, removable media you expect to move, or a path that will later be renamed.
4. In an elevated PowerShell in the extracted folder, verify its internal hashes and register it:

```powershell
Get-Content .\SHA256SUMS.txt
.\install.ps1 -ConfirmCertificateImport
```

The helper verifies all internal checksums, package and executable signatures, the signed payload anchor, exact identity metadata, and the external location. If the certificate is not already trusted, it imports the bundled public certificate to `LocalMachine\TrustedPeople` only after explicit confirmation. Administrative elevation is required for that import.

## Run, move, and remove

Start `app\LumaTherm.exe` only after successful registration. The registered identity points at this exact external folder. Do not move, rename, or delete it while registered. First unregister it from the current folder, move or extract a fresh archive, then register again.

To remove the identity, use an elevated PowerShell in the same portable folder:

```powershell
.\uninstall.ps1
```

The command asks for confirmation unless `-Force` is supplied. `-RemoveUserData` additionally removes the guarded `%LOCALAPPDATA%\LumaTherm` directory. Removing the folder without unregistering can leave an unusable identity, so do not do it.

`-AuditOnly` plans verification/registration for test use and does not register a package or import a certificate.
