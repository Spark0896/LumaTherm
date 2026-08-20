# Task 12 report — MSIX packaging and reversible release scripts

## Safe-stage result

Implemented the x64 MSIX manifest/package project, deterministic approved assets contract, repeatable self-contained release pipeline, and reversible per-user install/uninstall scripts. Implementation commit: `2f562de` (`build: add signed MSIX and portable release`). Safe tool-resolution fix: `f2858be` (`fix: resolve pinned packaging tools safely`). Signing-security review fix: `8ebaff3` (`fix: secure local package signing workflow`).

The controller-gated real `build-release.ps1` invocation has intentionally not run yet. Therefore MakeAppx schema/signature evidence, final artifact sizes/hashes, and the transient certificate-store thumbprint remain pending; this report does not claim the signed release is complete.

## Witnessed RED -> GREEN evidence

- Manifest/project RED: 4 of 8 initial tests failed on the absent manifest, package project, public folder, and solution entries. GREEN: exact x64 identity, `LumaTherm.exe`, `runFullTrust`, `com.microsoft.windows.lighting`, `LumaThermStartup` with `Enabled="false"`, Properties/Dependencies/VisualElements, pinned private BuildTools, and solution inclusion passed.
- Script RED: 10 tests failed because release/install/uninstall scripts did not exist. GREEN exercises copied scripts in disposable repositories/release folders and uses no package, registry, certificate-store, or hardware mutation.
- Layout RED: stale layout cleanup passed but `LumaTherm.exe` was absent. GREEN normalizes the published single-file `LumaTherm.App.exe` to the manifest/portable name `LumaTherm.exe`, rejects ambiguous/missing executables, deletes stale layout content, and copies only publish/manifest/assets/public inputs.
- PowerShell compatibility RED: `Get-FileHash` was unavailable in the bounded Windows PowerShell test host. GREEN uses direct SHA-256 streams and uppercase hexadecimal output.
- Checksum REDs: culture-dependent ordering differed from ordinal ordering, and an unlisted `unexpected.exe` was accepted. GREEN uses ordinal-ignore-case sorting, rejects case ambiguity/unlisted files, and covers exactly MSIX, portable ZIP, CER, installer, and uninstaller.
- Certificate RED: Plan mode accepted an invalid PFX password. GREEN opens a real temporary fixture PFX ephemerally, checks Publisher/subject equality, rejects invalid passwords, and never emits password content.
- Package-scope RED: uninstall planned removal of `OtherProduct_...`. GREEN accepts only one exact `LumaTherm_...` full name and rejects ambiguity/out-of-identity input.
- App executable investigation: setting the production assembly name to `LumaTherm` caused App test discovery to hang past the 30-second blame deadline. The assembly change was removed; the release-boundary normalization above preserves the reviewed app assembly. Fresh App verification returned 138/138.
- Restore/output RED: release Plan lacked RID restore and artifact-rooted build arguments. GREEN restores `win-x64` and supplies `UseArtifactsOutput=true` plus an absolute `ArtifactsPath` for restore/test/publish.

## Manifest and approved assets

- Identity: `LumaTherm`, `CN=LumaTherm Local`, `1.0.0.0`, `x64`.
- Application: `LumaTherm.exe`, `Windows.FullTrustApplication`.
- Dynamic Lighting: exact `uap3:AppExtension Name="com.microsoft.windows.lighting"`, public folder `public`.
- Startup: exact `TaskId="LumaThermStartup"`, default `Enabled="false"`.
- Approved deterministic asset SHA-256:
  - `StoreLogo.png`: `243830D4498910D6740B8567B6B9BB4692E09F0423CB8EBB8473F9204D10B940` (50x50)
  - `Square44x44Logo.png`: `952FBFD4BE7E566E23C4AC02D999F6DFEA0FE3CF1F08F62BD83EC6C06B00C7E2` (44x44)
  - `Square150x150Logo.png`: `AC3C076773D321B12B1292654EFA3762A64104D326F23D32471269D73F2C3F56` (150x150)
  - `Wide310x150Logo.png`: `0DB9C40921D50CA1A08DCBD51C4593010A4830F893BBA0AFCBE50C1D2B47922C` (310x150)
- The focused suite regenerates all four assets with `LumaTherm.AssetBuilder` in an ignored fixture and proves byte-for-byte parity.

## Safe release/install/uninstall evidence

- Pinned BuildTools property resolved to `C:\Users\User\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.8249`.
- Plan mode selected absolute x64 `MakeAppx.exe` and `SignTool.exe` from `bin\10.0.26100.0\x64`.
- Publish contract includes self-contained `win-x64`, single file, native self-extraction, and disabled symbols.
- Full mode cleans only its owned `dist`, publish, and package-layout locations; generated outputs/signing material are ignored. No production GCC/RGB Fusion, raw HID, vendor DLL, lighting, autostart, package, registry, or certificate trust operation is used.
- Installer requires exactly the five named release artifacts, checks every checksum, and requires the signer thumbprint to equal sibling `LumaTherm.cer`. A `Valid` signature installs without a trust mutation. A matching `NotTrusted` signature requires explicit confirmation, imports only that CER into CurrentUser TrustedPeople when absent, then re-runs Authenticode and requires `Valid` before installation. It never enables autostart.
- Uninstaller confirms unless forced, targets one exact package and only the `LumaTherm` portable Run value, preserves user data by default, and removes only the distributed CER thumbprint when explicitly requested.

## Fresh safe verification

```text
dotnet test tests/LumaTherm.Packaging.Tests/LumaTherm.Packaging.Tests.csproj -c Release --no-restore -p:NuGetAudit=false
PASS: 29 passed, 0 failed.

dotnet test LumaTherm.sln -c Release --no-restore -p:NuGetAudit=false
PASS: Core 71 + Infrastructure 91 + App 138 + Packaging 29 = 329 passed, 0 failed.

dotnet build LumaTherm.sln -c Release --no-restore -p:NuGetAudit=false
PASS: 0 warnings, 0 errors.

git diff --check
PASS before source commit; no whitespace errors.
```

## Controller-gated real release checkpoint

From the worktree root, the precise command is:

```powershell
& "$PWD\scripts\build-release.ps1"
```

Expected certificate-store delta: one transient code-signing certificate with subject `CN=LumaTherm Local` is created under `Cert:\CurrentUser\My` and exported only to a unique ignored `packaging/local-signing/run-<guid>/` directory. After signing, the exact public certificate is imported temporarily into `Cert:\CurrentUser\TrustedPeople` only when that thumbprint was not already present, so SignTool `/pa` can verify the package. The nested cleanup independently removes and verifies absence of the owned TrustedPeople entry and the owned CurrentUser/My entry, never removes a pre-existing TrustedPeople certificate, then removes only the owned run directory. The final report must record the transient thumbprint only after verified cleanup. The expected outputs are `dist/LumaTherm-1.0.0-win-x64.msix`, `dist/LumaTherm-1.0.0-portable-win-x64.zip`, `dist/LumaTherm.cer`, `dist/SHA256SUMS.txt`, `dist/install.ps1`, and `dist/uninstall.ps1`; MakeAppx and SignTool verification, exact sizes, and SHA-256 values must be appended after the approved run.

## Self-review and pending evidence

- Installer/uninstaller verification overrides require both `-AuditOnly` and `LUMATHERM_PACKAGING_TEST=1`; those audit paths cannot reach package, registry, certificate, or filesystem mutation. The release tool-path override is separately restricted to `-Mode Plan` plus `LUMATHERM_PACKAGING_TEST=1`; Full mode always resolves and validates the pinned BuildTools package.
- Tests exercise real XML parsing, PNG headers/hashes/regeneration, SHA-256, fixture certificates, sibling resolution, and executable PowerShell plans. No broad script source-grep test substitutes for behavior.
- Password values are absent from stdout/stderr and are cleared from release variables in `finally`; generated passwords are cryptographically random per run.
- Pending by instruction: real MakeAppx schema validation, SHA-256 SignTool sign/verify, artifact sizes/hashes, and certificate-store create/remove evidence. No install/uninstall is authorized at this checkpoint.

## Safe fix round — restored BuildTools property fallback

- RED: a fresh real `build-release.ps1 -Mode Plan` failed with a null-method error because Plan queried only the artifact-rooted MSBuild intermediate path, while the already-restored package property existed in the normal project intermediate path.
- GREEN: tool resolution first queries the release artifact-rooted path (the path Full mode restores), then safely falls back to the normal restored project path. If neither produces the pinned property it now fails with an actionable restore message instead of dereferencing null.
- A real safe Plan regression resolves version `10.0.26100.8249` and the absolute x64 tool path; the refreshed focused/full/build results above include this test.

## Review fix round 1 — secure local signing and mutation boundaries

- C1 RED: a locally self-signed package could be signed but `/pa` verification had no coherent trust bootstrap, while the installer rejected `NotTrusted` before an informed trust decision. GREEN: the build uses a code-signing EKU/DigitalSignature certificate, temporarily trusts only the exact generated public certificate for `/pa`, and independently removes and verifies both owned store entries in `finally`. Installer Audit tests prove checksum and signer matching precede the explicit prompt, planned import, Authenticode re-verification, and install; the already-`Valid` path has no trust prompt or import.
- C2 RED: a real temporary junction beneath an allowed output root passed the lexical root check and allowed a source file outside the repository to be moved. GREEN: every sensitive recursive delete, move, export, or signing-directory write walks existing components from the physical repository root and refuses any reparse point. The regression preserves both an external sentinel and external executable and creates no package layout.
- I1 RED: Plan accepted an arbitrary SDK tool override without the test environment gate. GREEN: `-SdkBuildToolsPath` is accepted only by Plan with `LUMATHERM_PACKAGING_TEST=1`; Full rejects it before any mutation and otherwise validates the exact pinned package/version and x64 tools.
- I2 RED: local signing reused one fixed directory. GREEN: each invocation owns a unique `run-<guid>` signing directory, Plan only reports it, and Full creates/removes only that directory.
- I3 RED: uninstall could plan package removal before discovering a requested certificate mismatch or missing sibling CER. GREEN: exact package identity, user-data target, CER thumbprint, and store target are all preflighted before confirmation or the first mutation plan.
- I4 RED: the test host synchronously read redirected streams and had no bounded timeout. GREEN: stdout/stderr drains run asynchronously, waits are bounded, timeout kills the process tree, and drains are awaited; a real child-process timeout regression completes in under five seconds.
- M1 RED: installer accepted version globs and checksum extras. GREEN: it requires exact `LumaTherm-1.0.0-win-x64.msix`, exact casing and exactly five checksum names, then queries and reports one unique installed `LumaTherm` PackageFullName after Add-AppxPackage.
- Fresh safe verification after this round: Packaging 29/29; Core 71 + Infrastructure 91 + App 138 + Packaging 29 = 329/329; Release build 0 warnings and 0 errors; deterministic asset parity 1/1; real non-mutating Plan resolved the pinned absolute x64 tools; `git diff --check` reported no whitespace errors (only the repository's LF-to-CRLF checkout notices).
- No Full mode, certificate-store mutation, MSIX install/uninstall, registry mutation, production app launch, or hardware access was performed. The controller-approved real MakeAppx/SignTool checkpoint remains pending.
