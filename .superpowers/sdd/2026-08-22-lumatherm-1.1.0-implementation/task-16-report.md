# Task 16 report — local pre-publication acceptance

Date: 2026-08-30

Status: broader Task 16 remains `NEEDS_CONTEXT`; the post-fix signed release artifacts have now been rebuilt and cryptographically audited locally through a freshly authorized temporary `LocalMachine\TrustedPeople` transaction. Installer/portable lifecycle, GUI/hardware acceptance, screenshots, and publication remain pending. Publication steps 8–11 were not started. No tag, remote, push, repository, release, or upload was created.

## Verified baseline

- Base `f255bee5cf1a6f21f96661a0aaf9e09c250c1e86`; initial worktree clean.
- Release build: 0 warnings/errors.
- Tests: Core 123, Infrastructure 111, App 265, Smoke 19, Packaging 76; aggregate 594 passed, 0 failed, 0 skipped.
- Official Inno 6.7.3 SHA-256 `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`; compile-only gate passed; extracted ISCC SHA-256 `0a8757031b33777e4c9cbffee40f11a5062b36d25cbe144c1db73b6102b80ad7`.

## Signed-build findings

Temporary certificate transactions never persisted a password or private-key path and removed their PFX, private key, CER and exact trust entry in `finally`. Pre-existing public trust `17DE7A023246766D898A26DA2BFC8D797B48B987` was unchanged.

Real MakeAppx validation exposed sparse desktop identity requirements. The current user-approved sparse contract is `rescap:Capability Name="runFullTrust"`, `uap10:RuntimeBehavior="win32App"`, `uap10:TrustLevel="mediumIL"`, no `EntryPoint`, and `/nv` for identity-only sparse-package creation. It does not use `windowsApp` or `Windows.FullTrustApplication`.

## Fusion-manifest recovery (complete)

The installer rollback root cause was the invalid Fusion child element `<msix:identity>`. The shipping `src/LumaTherm.App/app.manifest` now uses the local element `<msix xmlns="urn:schemas-microsoft-com:msix.v1" ... />`.

### TDD RED

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj -c Release -p:NuGetAudit=false --filter "FullyQualifiedName~FusionManifestUsesTheMsixV1ElementAcceptedByActivationContextParsing"
```

Exit 1 as expected: `Assert.Single() Failure: The collection was empty`; 0 passed, 1 failed. Production manifest was unchanged for this run.

### Focused GREEN

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~FusionManifestUsesTheMsixV1ElementAcceptedByActivationContextParsing|FullyQualifiedName~NativeAndSparseManifestsDeclareTheSameExactIdentity"
```

Exit 0: 2 passed, 0 failed, 0 skipped.

### Unsigned Release and Windows Fusion validation

```powershell
& .\.dotnet\dotnet.exe publish .\src\LumaTherm.App\LumaTherm.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o $publishDir
[FusionManifestProbe]::ValidateEmbeddedManifest($exe)
Get-AuthenticodeSignature -LiteralPath $exe
```

`FusionManifestProbe` was an inline, non-persisted P/Invoke helper that called `CreateActCtxW` with `ACTCTX_FLAG_RESOURCE_NAME_VALID` (`0x8`) and embedded manifest resource ID 1. `mt.exe` was not installed, so this used the Windows Fusion parser directly without launching the GUI.

```text
Published files: 1 (LumaTherm.App.exe)
Authenticode status: NotSigned
CreateActCtxW embedded manifest resource #1: VALID
```

The temporary publish directory was verified under the system temp root and removed in `finally`. No installer, UAC, signing, certificate, registration, publication, tag, push, release, or upload action was performed.

### Final automated verification

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj -c Release --no-restore -p:NuGetAudit=false
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release --no-restore -p:NuGetAudit=false
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release --no-build -p:NuGetAudit=false
git diff --check
```

- Packaging: 80 passed, 0 failed, 0 skipped.
- Release build: 0 warnings, 0 errors.
- Full suite: Core 123, Infrastructure 111, App 265, Smoke 19, Packaging 80; aggregate 598 passed, 0 failed, 0 skipped.
- `git diff --check`: exit 0 with no whitespace errors.

## Machine evidence

- Existing `LumaTherm_1.0.1.0_x64__jzd30fs6ag6cm` remains installed/running; user desktop shortcut remains; no Start Menu shortcut was found.
- Settings schema 1 remains unchanged, SHA-256 `4DDC31323D58625CBC875F6110558C7D15E806CFE0A096E7A9BA088D46574B3F`.
- Read-only probes: NVML passed at 67°C on RTX 5070; Afterburner fallback had no reading; one-lamp `GIGABYTE Device` was discovered with `available:false` while GCC was running.

Installer/portable lifecycle, final signed artifacts/checksums, lighting writes/background behavior, tray/autostart, recovery/soak, RU/EN screenshots, and documentation remain pending. Resume only when the user is ready to approve the exact UAC prompt.

## Post-manifest-fix signed rebuild slice — stopped at the no-trust gate

This slice started from clean HEAD `365a5f91e355261bf4565a375ed49ba99014175c`. It did not install, register, launch, stop, or remove LumaTherm; did not invoke UAC; did not write lighting hardware; did not mutate any certificate trust store; and did not tag, push, publish, upload, or otherwise change a remote.

### Shipping-tool preflight

Read-only discovery found the SDK `SignTool.exe` and `MakeAppx.exe`. The default installed Inno path was absent, but the previously verified extracted compiler remains available at `$env:LOCALAPPDATA\Temp\LumaTherm-task16-tools\compiler\ISCC.exe`; its fresh SHA-256 is `0A8757031B33777E4C9CBFFEE40F11A5062B36D25CBE144C1DB73B6102B80AD7`, matching the earlier verified value, so no Inno installation is required.

The shipping script requires both `SignTool verify /pa /v` exit 0 and `Get-AuthenticodeSignature.Status -eq 'Valid'` immediately after signing the application. A no-store preflight tested that exact Windows trust behavior before allowing the Full build to replace `dist`:

```powershell
# Executed as one guarded in-memory/temp transaction; the generated password and
# absolute PFX/private-key path were never printed or persisted in this report.
$certificate = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=LumaTherm Local', $rsa,
    [Security.Cryptography.HashAlgorithmName]::SHA256,
    [Security.Cryptography.RSASignaturePadding]::Pkcs1).CreateSelfSigned(...)
[IO.File]::WriteAllBytes($externalPfx, $certificate.Export('Pkcs12', $generatedPassword))
& $signTool sign /fd SHA256 /f $externalPfx /p $generatedPassword $unsignedProbe
& $signTool verify /pa /v $unsignedProbe
Get-AuthenticodeSignature -LiteralPath $unsignedProbe
# finally: dispose certificate/key, clear password, validate the temp-root boundary,
# and recursively remove the exact transaction directory.
```

Exact result on a copied, initially `NotSigned` LumaTherm PE:

```text
SignTool sign exit: 0
SignTool verify /pa /v exit: 1
Get-AuthenticodeSignature: UnknownError
Status message: certificate chain terminated in a root that is not trusted by the trust provider
Subject: CN=LumaTherm Local
Generated/signed thumbprint: F272D0FE9A74667E546A3498A089C44149C4AFD3
Signer matches generated certificate: true
Generated certificate entries in CurrentUser\My, CurrentUser\Root, CurrentUser\TrustedPeople: 0
Temporary private material present after finally cleanup: false
```

The pre-existing trust thumbprint `17DE7A023246766D898A26DA2BFC8D797B48B987` remained untouched: `CurrentUser\TrustedPeople=1`, `CurrentUser\My=0`, `CurrentUser\Root=0`. Neither the preflight signer `F272D0FE9A74667E546A3498A089C44149C4AFD3` nor the signer of the older artifacts described below exists in those three stores. A recursive repository scan found 0 files with `.pfx`, `.p12`, `.pvk`, or `.key` extensions.

Because a newly generated self-signed certificate could not satisfy the shipping script's mandatory Windows Authenticode verification without trust, the requested Full shipping build was initially not invoked. The narrow `CurrentUser\TrustedPeople` checkpoint was subsequently authorized and executed as recorded below; that exact store proved insufficient on this host.

### Pre-existing `dist` audit (not post-fix release output)

The files already in `dist` predate the manifest-fix commit: their timestamps are approximately 20:36 +03:00, while commit `d89bb9f50a26b066f08cc8082c59cb881dd66c7c` was created at 22:35:13 +03:00. They were not replaced in this slice and must not be published.

```text
E8C550B6A20DE9F7C4186F6A227D5DBC96A5FDA19C037045E4F2888B10420957 *LumaTherm-1.1.0-portable-win-x64.zip
84D89D522FFBFA4F7D7AC169C7C121F630F6AFD4C9E52D3C63B3142E74EE7F1D *LumaTherm-1.1.0-win-x64-setup.exe
SHA256SUMS.txt SHA-256: F212E2D1ED65666C0F6D532DA1A039AF7761882574C16EBA447F69B061A3F1BC
```

Both release-file lines match the actual SHA-256 values exactly. The portable contains exactly `app/LumaTherm.exe`, `LICENSE`, `LumaTherm-1.1.0-sparse.msix`, `LumaTherm.cer`, `README.md`, `Register-LumaTherm.ps1`, `SHA256SUMS.txt`, and `Unregister-LumaTherm.ps1`; it contains no PFX/private/password/secret-named entry.

The embedded app, sparse MSIX, and installer all carry signer `74A22D3D35B208D7E2F862F7D4A757C8EF5EEBC9`, subject `CN=LumaTherm Local`, but Windows reports `UnknownError` for all three because the self-signed chain is not trusted. The sparse package itself has the expected identity contract: `LumaTherm`, publisher `CN=LumaTherm Local`, version `1.1.0.0`, application ID `LumaTherm`, executable `LumaTherm.exe`, `uap10:RuntimeBehavior="win32App"`, `uap10:TrustLevel="mediumIL"`, no `EntryPoint`, and exactly one `rescap:Capability Name="runFullTrust"`.

`CreateActCtxW` with embedded manifest resource ID 1 rejected the actual portable `LumaTherm.exe` with the Windows side-by-side configuration error, confirming that these are stale pre-fix artifacts. Temporary extraction used for this read-only audit was removed in `finally` (`ArtifactAuditTempPresentAfterCleanup=False`). Therefore there are no post-fix artifact hashes or post-fix signature-success claims for this slice.

## Authorized CurrentUser signed-build continuation — verification blocked

The user authorized a narrower trust transaction: generate a new temporary self-signed `CN=LumaTherm Local` code-signing certificate, add only its public certificate to `Cert:\CurrentUser\TrustedPeople`, run the Full shipping build, then remove only the exact generated thumbprint and all private material in `finally`. `LocalMachine`, UAC, application installation/registration/launch, hardware writes, and public actions remained forbidden and were not used.

The transaction started from clean commit `fa7ea7256d4c5ffcd34c636b9578ebf27ff38f22`. The generated password and absolute PFX path were process-local and were never printed or written to documentation. The cached official Inno compiler was supplied through the shipping script's controlled Full-mode override after a fresh SHA-256 match:

```powershell
$env:LUMATHERM_PACKAGING_TEST = '1'
& .\scripts\build-release.ps1 `
    -Mode Full `
    -CertificatePath $externalTemporaryPfx `
    -CertificatePassword $processLocalGeneratedPassword `
    -Publisher 'CN=LumaTherm Local' `
    -InnoSetupPath $verifiedExtractedIscc
```

Exact signing transaction evidence:

```text
Signer thumbprint: 90D96901132C8B488C5798626A52E9B8B3BA2927
Temporary CurrentUser\TrustedPeople entry count during build: 1
Temporary trusted certificate HasPrivateKey: false
ISCC SHA-256: 0A8757031B33777E4C9CBFFEE40F11A5062B36D25CBE144C1DB73B6102B80AD7
Restore: exit 0
Tests: Core 123, Infrastructure 111, App 265, Smoke 19, Packaging 80
Aggregate tests: 598 passed, 0 failed, 0 skipped
Self-contained publish: exit 0
Application signing: exit 0
Application SignTool verify /pa /v: exit 1
Failure: certificate chain terminated in a root certificate not trusted by the trust provider
Shipping script failure: Application Authenticode verification failed (build-release.ps1 line 303)
Temporary private material present after finally cleanup: false
Temporary signer count in CurrentUser\TrustedPeople after cleanup: 0
Protected 17DE7A023246766D898A26DA2BFC8D797B48B987 count after cleanup: 1
```

The script stopped before exporting the portable public CER, building/signing the sparse MSIX, compiling/signing the installer, writing checksums, or promoting public artifacts. The only new partial build output is `artifacts\release\publish\LumaTherm.exe`:

```text
SHA-256: 0F5EF25420C42BE08C4CCD1AF5E081C4385B5B2F562DC28F492C02BC1B45A083
Signer: 90D96901132C8B488C5798626A52E9B8B3BA2927, CN=LumaTherm Local
Authenticode after required trust cleanup: UnknownError (untrusted self-signed root)
CreateActCtxW embedded manifest resource #1: VALID
```

This partial executable is not a release artifact. No post-fix sparse MSIX, installer, portable ZIP, or `SHA256SUMS.txt` was produced. Transactional promotion preserved the stale pre-fix `dist`; its hashes remain `E8C550B6A20DE9F7C4186F6A227D5DBC96A5FDA19C037045E4F2888B10420957` for the portable ZIP, `84D89D522FFBFA4F7D7AC169C7C121F630F6AFD4C9E52D3C63B3142E74EE7F1D` for the installer, and `F212E2D1ED65666C0F6D532DA1A039AF7761882574C16EBA447F69B061A3F1BC` for the checksum file. Those stale files remain non-publishable.

### Systematic trust diagnosis

The protected public certificate `17DE7A023246766D898A26DA2BFC8D797B48B987` has the standard code-signing profile: critical Digital Signature key usage, non-critical Code Signing EKU, and Subject Key Identifier. To rule out the first generated certificate's different extension profile, a second minimal probe created an in-memory certificate with exactly those three extension OIDs (`2.5.29.15`, `2.5.29.37`, `2.5.29.14`), added only its public half to `CurrentUser\TrustedPeople`, and signed a copied initially-unsigned LumaTherm PE:

```text
Probe signer: 329DC2FE1C92029BBBBB46D6D9E76860305F660F
SignTool sign exit: 0
SignTool verify /pa /v exit: 1
Get-AuthenticodeSignature: UnknownError
Signer match: true
Public-only trust entry present during verification: true
Failure: certificate chain terminated in a root certificate not trusted by the trust provider
Probe trust count after cleanup: 0
Probe private material present after cleanup: false
Protected 17DE7A023246766D898A26DA2BFC8D797B48B987 count after cleanup: 1
```

This reproduces the same failure independently of certificate extension profile and identifies the environmental gate: on this host, `SignTool verify /pa` does not accept a self-signed end-entity certificate placed only in `CurrentUser\TrustedPeople` as a trust anchor. Continuing would require a different trust mechanism outside the authorized store (for example, an explicitly authorized CurrentUser root transaction) or a signing certificate that already chains to a trusted root. The slice therefore stopped before any broader trust mutation. A final repository scan found 0 `.pfx`, `.p12`, `.pvk`, or `.key` files, and the idle .NET build servers were shut down with `dotnet build-server shutdown`.

## LocalMachine trust continuation — blocked before UAC

A later continuation requested a temporary public-only import of a newly generated exact `CN=LumaTherm Local` certificate into `LocalMachine\TrustedPeople`, followed by a Full build and exact cleanup in the same guarded transaction. The attempted elevated transaction was rejected by the execution security gate before `CreateProcess`; Windows UAC was never displayed and no part of the transaction ran. The rejection required a new direct user authorization after explicit disclosure that `LocalMachine\TrustedPeople` is a machine-wide security-boundary change, even when the certificate is temporary and cleanup is guaranteed.

Post-rejection read-only evidence:

```text
UAC displayed: false
Temporary certificate/PFX/CER created: false
lumatherm-machine-sign-* temp directories: 0
Repository .pfx/.p12/.pvk/.key files: 0
Matching helper/build/sign processes: 0
CurrentUser\TrustedPeople protected 17DE7A023246766D898A26DA2BFC8D797B48B987 count: 1
LocalMachine\TrustedPeople protected 17DE7A023246766D898A26DA2BFC8D797B48B987 count: 1
New trust entries: 0
Worktree before this report update: clean at 42b592edf7124937da0ebfc3c184c510deb62507
```

No workaround, indirect elevation, alternate store, UAC automation, or retry was attempted. The exact action-time checkpoint remains before generating private material and before starting the elevated helper: obtain a new direct user confirmation specifically authorizing the temporary machine-wide `LocalMachine\TrustedPeople` public-certificate import after the risk disclosure, while preserving both pre-existing `17DE7A023246766D898A26DA2BFC8D797B48B987` entries and removing only the newly generated exact thumbprint in `finally`.

## Fresh LocalMachine-authorized signed release build — complete

The user subsequently provided fresh action-time authorization for the temporary machine-wide public-certificate transaction. Two manually confirmed UAC transactions were used; UAC was never automated. Both generated passwords and absolute private PFX paths remained process-local, outside the repository and `dist`, and were never printed or persisted in documentation.

### First LocalMachine attempt and systematic retry evidence

The first new signer was `CF6960D0DF770664359C0B167CAC705A293A9429`. Its public certificate was the only new entry added to `LocalMachine\TrustedPeople`; it had no private key. The preflight succeeded (`SignTool sign=0`, `SignTool verify /pa=0`, `Get-AuthenticodeSignature=Valid`), proving that the authorized machine store satisfied the shipping verification policy.

The Full script then stopped during its solution test gate: Core 123, Infrastructure 111, App 265, and Smoke 19 passed; Packaging reported 79 passed and 1 failed. `BoundedProcessTestHostTests.TimeoutKillsChildTreeAndReleasesItsFileLockWithinTheBound` reached its three-second timeout before the child PowerShell process created `child.lock`, so the final `File.Open` observed `FileNotFoundException`. The transaction did not reach publish/sign/package/promotion. Its `finally` evidence was `removed=1; remaining=0`; private material was removed and both protected `17DE7A023246766D898A26DA2BFC8D797B48B987` entries remained unchanged.

No test or production file was edited. Three sequential focused reruns of the exact failing test each passed (`1 passed, 0 failed`, approximately three seconds each), supporting transient concurrent process-start contention rather than a product regression. A second fresh Full shipping transaction was then run without code changes.

### Successful Full shipping build

The successful signer is `CE29DF766EF5D8070BB802C14A1D5A446BDD3823`, subject `CN=LumaTherm Local`. Its exact public-only certificate was temporarily added to `LocalMachine\TrustedPeople`; preflight again returned `SignTool sign=0`, `SignTool verify /pa=0`, and `Get-AuthenticodeSignature=Valid`. The verified extracted Inno compiler SHA-256 was `0A8757031B33777E4C9CBFFEE40F11A5062B36D25CBE144C1DB73B6102B80AD7`.

The shipping command was:

```powershell
$env:LUMATHERM_PACKAGING_TEST = '1'
& .\scripts\build-release.ps1 `
    -Mode Full `
    -CertificatePath $externalTemporaryPfx `
    -CertificatePassword $processLocalGeneratedPassword `
    -Publisher 'CN=LumaTherm Local' `
    -InnoSetupPath $verifiedExtractedIscc
```

Exact build result:

```text
Restore: exit 0
Core: 123 passed, 0 failed, 0 skipped
Infrastructure: 111 passed, 0 failed, 0 skipped
App: 265 passed, 0 failed, 0 skipped
Smoke: 19 passed, 0 failed, 0 skipped
Packaging: 80 passed, 0 failed, 0 skipped
Aggregate: 598 passed, 0 failed, 0 skipped
Self-contained publish: exit 0
Application sign/verify: exit 0 / exit 0
MakeAppx sparse package build: exit 0
Sparse package sign/verify: exit 0 / exit 0
Inno installer compile: exit 0
Installer sign/verify: exit 0 / exit 0
Public artifact promotion: complete
Independent signed-artifact audit: PASS
```

### Release hashes and contents

Public `dist\SHA256SUMS.txt` matches both files exactly:

```text
2806F326BAEB1E8DE73F92D6585C98E54C3B4189BC9ED4AD4A89884E89BB73C5 *LumaTherm-1.1.0-portable-win-x64.zip
FD4099FBBBCB011C6E3269CE60A8A390FE07D7841D14AF9249E40FB4AAAC5759 *LumaTherm-1.1.0-win-x64-setup.exe
```

`SHA256SUMS.txt` itself has SHA-256 `D43A317D1B00DB97356709DC34FA2AA933451D0C97DED83F30D5655248913D0E`.

The portable archive contains exactly eight files and its seven payload checksum lines all match:

```text
04A3ADA69FD386F7C08AF6FD046ACCB65B3359CFE692A1B2D07E033272306E66 *app/LumaTherm.exe
C87870CE7D3DCD02AACF7F861A679C27BE90AFA74A53301FD3922EFF44FD9048 *LICENSE
4381A50C968CFBC416A10A0BB3CF98A6EFC273115FFBE6CC1000F6253BE133FF *LumaTherm-1.1.0-sparse.msix
FB2EEF83E82A11A153F13321BA111329EC99687FCC27BAE167E5DD9F0D31C00C *LumaTherm.cer
892A0F98BF036D4E93DF3310B4670BCDF4FFA79A8373A68D0186C09BCC778235 *README.md
C493193EB7788E6B72A646100152D88CDFD719970FA6414558BCFA69F4C5B857 *Register-LumaTherm.ps1
CD88B8DD364A4C79C608855960711CD5A7E0C3F6536ADF52D5105C723B6A7C09 *Unregister-LumaTherm.ps1
```

The portable contains no PFX/private/password/secret-named entry. The sparse `PayloadHashes.json` contains six anchored payloads and every hash matches the corresponding portable file. The public `LumaTherm.cer` has no private key and matches signer `CE29DF766EF5D8070BB802C14A1D5A446BDD3823`.

### Signatures and manifest contracts

While the exact temporary machine trust was present, `Get-AuthenticodeSignature` returned `Valid` and fresh `SignTool verify /pa /v` returned exit 0 for all three signed payloads:

```text
LumaTherm.exe                              Valid  CE29DF766EF5D8070BB802C14A1D5A446BDD3823
LumaTherm-1.1.0-sparse.msix                Valid  CE29DF766EF5D8070BB802C14A1D5A446BDD3823
LumaTherm-1.1.0-win-x64-setup.exe          Valid  CE29DF766EF5D8070BB802C14A1D5A446BDD3823
```

The signatures are not timestamped. After required trust cleanup, all three still report the same embedded signer but have expected status `UnknownError` because the temporary self-signed trust anchor is gone; importing the bundled public certificate is therefore required before Windows can report `Valid` on another machine.

`CreateActCtxW` with embedded manifest resource ID 1 returned `VALID` for the actual portable application and the exact installer payload input. The actual signed sparse MSIX contains `AppxManifest.xml`, `PayloadHashes.json`, and `AppxSignature.p7x` and has the required contract: identity `LumaTherm`, publisher `CN=LumaTherm Local`, version `1.1.0.0`, architecture `x64`, application ID `LumaTherm`, executable `LumaTherm.exe`, `uap10:RuntimeBehavior="win32App"`, `uap10:TrustLevel="mediumIL"`, no `EntryPoint`, and exactly one `rescap:Capability Name="runFullTrust"`.

### Final cleanup and scope

The successful elevated helper exited 0 with `removed=1; remaining=0`. The failed-at-test signer `CF6960D0DF770664359C0B167CAC705A293A9429` and successful signer `CE29DF766EF5D8070BB802C14A1D5A446BDD3823` are absent from both `CurrentUser\TrustedPeople` and `LocalMachine\TrustedPeople`. The protected `17DE7A023246766D898A26DA2BFC8D797B48B987` remains exactly once in each store. There are 0 `lumatherm-machine-sign-*` temp directories and 0 repository files with `.pfx`, `.p12`, `.pvk`, or `.key` extensions. Temporary audit extraction was removed, and idle .NET build servers were shut down.

No installer or application was launched. No installation, sparse-package registration, old-version removal, hardware write, GUI action, tag, remote, push, release, upload, or publication occurred. Those remain separate action-time checkpoints.

## Authorized installer acceptance — FAILED at sparse-package registration

The user later gave action-time authorization to run the freshly verified installer, update the existing 1.0.1 installation, preserve settings, enable Desktop and Start Menu shortcuts, and launch the installed application for a process/window check. Hardware RGB writes, live thermal-mode changes, and publication remained excluded.

Preflight re-read `dist\LumaTherm-1.1.0-win-x64-setup.exe` as SHA-256 `FD4099FBBBCB011C6E3269CE60A8A390FE07D7841D14AF9249E40FB4AAAC5759`, an exact match for the verified release. The old package was `LumaTherm_1.0.1.0_x64__jzd30fs6ag6cm` (Status Ok). Settings were `C:\Users\User\AppData\Local\LumaTherm\settings.json`, length 542, SHA-256 `4DDC31323D58625CBC875F6110558C7D15E806CFE0A096E7A9BA088D46574B3F`.

Windows UI automation was unavailable before any interaction: its required reset/retries failed with `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`. No UI or UAC dialog was automated. The deterministic shipping-installer fallback was:

```powershell
Start-Process `
    -FilePath '.\dist\LumaTherm-1.1.0-win-x64-setup.exe' `
    -ArgumentList @(
        '/SILENT',
        '/SUPPRESSMSGBOXES',
        '/ALLOWCERTIMPORT',
        '/TASKS="desktopicon,startmenuicon"',
        '/NORESTART',
        '/CLOSEAPPLICATIONS') `
    -Verb RunAs `
    -WindowStyle Normal `
    -Wait
```

The user manually approved UAC. The installer process exited 0, but independent acceptance checks found an incomplete state:

```text
Old AppX identity 1.0.1 removal: succeeded
New AppX identity 1.1.0 registration: absent
Installed Inno version/location: 1.1.0 / C:\Program Files\LumaTherm\
Installed app SHA-256: 04A3ADA69FD386F7C08AF6FD046ACCB65B3359CFE692A1B2D07E033272306E66
Installed sparse MSIX SHA-256: 4381A50C968CFBC416A10A0BB3CF98A6EFC273115FFBE6CC1000F6253BE133FF
Installed CER SHA-256: FB2EEF83E82A11A153F13321BA111329EC99687FCC27BAE167E5DD9F0D31C00C
Installed payload hashes match verified release: true
Start Menu target: C:\Program Files\LumaTherm\payload\app\LumaTherm.exe
Desktop target/arguments: C:\Windows\explorer.exe / shell:AppsFolder\LumaTherm_jzd30fs6ag6cm!LumaTherm
Desktop shortcut updated: false
Settings SHA-256 after attempt: 4DDC31323D58625CBC875F6110558C7D15E806CFE0A096E7A9BA088D46574B3F
Settings preserved exactly: true
LumaTherm process: absent
Application launch attempted: false
```

The AppX operational log contains the successful 1.0.1 removal at 23:24:16–23:24:17 but no 1.1.0 Add event. Source inspection confirms that Inno post-install calls `Register-LumaTherm.ps1 -PortableDirectory "{app}\payload" -NonInteractive -ConfirmCertificateImport`; that helper imports only the bundled public signer into `LocalMachine\TrustedPeople`, registers the sparse package with external location, verifies the contract, and rolls package/certificate changes back on failure.

For systematic isolation, the exact installed helper was prepared for one elevated run with output captured only in an external temporary log. The user canceled that UAC before the elevated process started. `Start-Process` returned `The operation was canceled by the user`; no registration action ran and no temporary log remained. No automatic retry or alternate trust-store workaround was attempted.

Final read-only cleanup/state evidence:

```text
Get-AppxPackage -Name LumaTherm: no package
CE29DF766EF5D8070BB802C14A1D5A446BDD3823 in CurrentUser\TrustedPeople: 0
CE29DF766EF5D8070BB802C14A1D5A446BDD3823 in LocalMachine\TrustedPeople: 0
Protected 17DE7A023246766D898A26DA2BFC8D797B48B987 in CurrentUser\TrustedPeople: 1
Protected 17DE7A023246766D898A26DA2BFC8D797B48B987 in LocalMachine\TrustedPeople: 1
Temporary registration logs: absent
LumaTherm process: absent
```

This is an installer acceptance failure despite exit code 0: the exact 1.1.0 Program Files payload/uninstaller is present, but the required package identity is absent and the Desktop shortcut targets that missing identity. Directly launching the Program Files executable was deliberately skipped because it would not validate the required package identity/extension contract.

The exact next action-time checkpoint is a manually approved UAC run of the installed shipping `Register-LumaTherm.ps1` for `C:\Program Files\LumaTherm\payload`, followed by independent identity/version/external-location/shortcut verification. Root cause inside the registration transaction remains unresolved because the diagnostic helper never started. No hardware write, mode change, tag, push, release, upload, or publication occurred.
