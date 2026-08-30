# Task 16 report — local pre-publication acceptance

Date: 2026-08-30

Status: broader Task 16 remains `NEEDS_CONTEXT`; the fusion-manifest recovery is complete locally, but the authorized post-fix signed rebuild stopped at the shipping script's application Authenticode verification because `CurrentUser\TrustedPeople` did not establish trust for the new self-signed signer on this host. Publication steps 8–11 were not started. No tag, remote, push, repository, release, or upload was created.

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
