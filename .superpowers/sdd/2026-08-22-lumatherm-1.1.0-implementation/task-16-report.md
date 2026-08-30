# Task 16 report — local pre-publication acceptance

Date: 2026-08-30

Status: broader Task 16 remains `NEEDS_CONTEXT`; the fusion-manifest recovery is complete locally. Publication steps 8–11 were not started. No tag, remote, push, repository, release, or upload was created.

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
