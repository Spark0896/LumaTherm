# Task 12 report — MSIX packaging and reversible release scripts

## Safe-stage result

Implemented the x64 MSIX manifest/package project, deterministic approved assets contract, repeatable self-contained release pipeline, and reversible per-user install/uninstall scripts. Implementation commit: `2f562de` (`build: add signed MSIX and portable release`). Safe tool-resolution fix: `f2858be` (`fix: resolve pinned packaging tools safely`). Signing-security review fixes: `8ebaff3` and `c9e1ca1` (`fix: use machine trust for package verification`).

The first controller-gated real `build-release.ps1` invocation ran against round 2 and stopped at SignTool `/pa` verification after restore, tests, publish, MakeAppx packing, and signing succeeded. The generated certificate was trusted in CurrentUser/TrustedPeople, but Windows still reported an untrusted root. A separately authorized elevated diagnostic proved the same exact public signer certificate in LocalMachine/TrustedPeople makes both Get-AuthenticodeSignature and SignTool `/pa` succeed. The corrected Full rerun, final complete artifact set, and final hashes remain pending; this report does not claim the signed release is complete.

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
- Full mode cleans only its owned `dist`, publish, and package-layout locations; generated outputs/signing material are ignored. It does not use production GCC/RGB Fusion, raw HID, vendor DLL, lighting, autostart, package installation, or registry operations. Its only trust operation is the exact temporary LocalMachine certificate bootstrap described in the controller checkpoint below, and Full refuses to start without explicit Administrator elevation.
- Installer requires exactly the five named release artifacts, checks every checksum, and requires the signer thumbprint to equal sibling `LumaTherm.cer`. A `Valid` signature installs without a trust mutation. A matching `NotTrusted` signature requires Administrator elevation and explicit confirmation, imports only that CER into LocalMachine TrustedPeople when absent, then re-runs Authenticode and requires `Valid` before installation. Invocation-owned trust is retained only after successful installation and removed on verification/install failure. It never enables autostart.
- Uninstaller confirms unless forced, targets one exact package and only the `LumaTherm` portable Run value, preserves user data by default, and removes only the distributed CER thumbprint from LocalMachine TrustedPeople when explicitly requested with Administrator elevation.

## Fresh safe verification

```text
dotnet test tests/LumaTherm.Packaging.Tests/LumaTherm.Packaging.Tests.csproj -c Release --no-restore -p:NuGetAudit=false
PASS: 40 passed, 0 failed.

dotnet test LumaTherm.sln -c Release --no-restore -p:NuGetAudit=false
PASS: Core 71 + Infrastructure 91 + App 138 + Packaging 40 = 340 passed, 0 failed.

dotnet build LumaTherm.sln -c Release --no-restore -p:NuGetAudit=false
PASS: 0 warnings, 0 errors.

git diff --check
PASS before source commit; no whitespace errors.
```

## Controller-gated real release checkpoint

From a non-elevated PowerShell in the worktree root, the precise UAC checkpoint command is:

```powershell
Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Verb RunAs -Wait -ArgumentList '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "C:\Users\User\Documents\project\monitoring_color\.worktrees\lumatherm-v1\scripts\build-release.ps1"'
```

Expected certificate-store delta: one transient code-signing certificate with subject `CN=LumaTherm Local` is created under `Cert:\CurrentUser\My` and exported only to a unique ignored `packaging/local-signing/run-<guid>/` directory. After signing, the exact public certificate is imported temporarily into `Cert:\LocalMachine\TrustedPeople` only when that thumbprint was not already present, so SignTool `/pa` can verify the package. The nested cleanup independently removes and verifies absence of the invocation-owned LocalMachine/TrustedPeople entry and the generated CurrentUser/My entry, never removes a pre-existing machine-trust certificate, then removes only the owned run directory. The final report must record the transient thumbprint only after verified cleanup. The expected outputs are `dist/LumaTherm-1.0.0-win-x64.msix`, `dist/LumaTherm-1.0.0-portable-win-x64.zip`, `dist/LumaTherm.cer`, `dist/SHA256SUMS.txt`, `dist/install.ps1`, and `dist/uninstall.ps1`; corrected Full SignTool verification, exact final sizes, and SHA-256 values must be appended after the approved rerun.

## Self-review and pending evidence

- Installer/uninstaller verification and Administrator-status overrides require both `-AuditOnly` and `LUMATHERM_PACKAGING_TEST=1`; those audit paths cannot reach package, registry, certificate, or filesystem mutation. The release tool-path override is separately restricted to `-Mode Plan` plus `LUMATHERM_PACKAGING_TEST=1`; Full mode always resolves and validates the pinned BuildTools package. Its Administrator test seam can only force `NonAdmin` rejection and cannot bypass elevation.
- Tests exercise real XML parsing, PNG headers/hashes/regeneration, SHA-256, fixture certificates, sibling resolution, and executable PowerShell plans. No broad script source-grep test substitutes for behavior.
- Password values are absent from stdout/stderr and are cleared from release variables in `finally`; generated passwords are cryptographically random per run.
- Observed in the first real checkpoint: MakeAppx packing and SHA-256 signing succeeded; the remaining failure was specifically `/pa` trust validation. Pending by instruction: the corrected elevated Full rerun, final SignTool verification, complete artifact sizes/hashes, and corrected machine-store cleanup evidence. No install/uninstall is authorized at this checkpoint.

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

## Review fix round 2 — recursive-tree and installer rollback safety

Source fix: `22c389c` (`fix: close packaging mutation safety gaps`).

- C2 descendant RED: allowed output roots containing child junctions passed ancestor-only checks. PrepareLayout reached recursive layout deletion and published-tree copying; uninstall planned recursive user-data removal. GREEN: recursive operations enumerate one physical directory level at a time, reject each reparse point before it can be traversed, and recheck immediately before every recursive delete, copy, archive input, and owned signing-directory cleanup. Real junction regressions prove refusal while preserving external sentinels and stale target content.
- Installer rollback RED: after importing the exact TrustedPeople certificate, an Authenticode exception/non-Valid result or Add-AppxPackage failure could leave invocation-owned trust behind. GREEN: post-import verification and installation are enclosed by ownership-aware `try`/`finally`; failures remove only the exact invocation-owned trust entry, successful installation retains it so the package remains runnable, and a pre-existing trust entry is never removed. Audit tests prove re-verification-failure and install-failure cleanup ordering plus successful retention without touching the real certificate store or package registry.
- Release-directory RED: an extra sibling omitted from SHA256SUMS.txt was ignored. GREEN: installer enumerates the release directory and requires exactly the five case-exact artifacts plus `SHA256SUMS.txt`, with no extra file or directory.
- Fresh safe verification after this round: Packaging 36/36; Core 71 + Infrastructure 91 + App 138 + Packaging 36 = 336/336; Release build 0 warnings and 0 errors; deterministic asset parity 1/1; real non-mutating Plan resolved pinned `10.0.26100.8249` absolute x64 MakeAppx/SignTool; `git diff --check` reported no whitespace errors apart from checkout line-ending notices.
- Round 2 expected the matching public certificate in CurrentUser/TrustedPeople. The real checkpoint and elevated diagnostic below proved that store choice was insufficient on this machine and is superseded by round 3's LocalMachine/TrustedPeople contract.
- Full mode, certificate mutation, package install/uninstall, registry changes, production launch, and hardware access were not run.

## Real checkpoint failure and review fix round 3 — machine trust

- Real round-2 checkpoint: no-argument Full restore, all tests, self-contained publish, MakeAppx pack, and SHA-256 sign succeeded. SignTool `verify /pa /v` then failed with an untrusted-root result while exact generated thumbprint `D84116356ACC8BA4759DAAC57BEC8317BCE85750` was transiently present in CurrentUser/TrustedPeople. Script cleanup left no exact-thumb entry in CurrentUser/My or CurrentUser/TrustedPeople. The incomplete signed MSIX remained at 73,982,091 bytes with SHA-256 `DA6972713B51CA0600F465BEC15B8401006AFE69B2E44BD8143A4FD0E2B15BEA`.
- Authorized diagnostic: the exact extracted public signer certificate was temporarily imported into LocalMachine/TrustedPeople under UAC elevation. Get-AuthenticodeSignature returned `Valid`, SignTool `/pa` exited `0`, and the invocation-owned machine entry was removed in `finally`. `artifacts/signing-diagnostics/machine-trust-result.json` records the thumbprint, Valid status, zero exit code, no failure, and `ownedEntryPresentAfter=false`; post-checks found no exact-thumb entry in LocalMachine/TrustedPeople, CurrentUser/My, or CurrentUser/TrustedPeople.
- Root cause: Windows' `/pa` policy on this host did not accept the self-signed leaf from CurrentUser/TrustedPeople, but did accept the identical certificate from LocalMachine/TrustedPeople. This was a trust-store scope mismatch, not a package-schema, signing-key, certificate-profile, or cleanup failure.
- RED: Plan named CurrentUser/TrustedPeople; Full lacked a pre-mutation Administrator gate; installer/uninstaller audit plans lacked machine-store and privilege-ordering contracts. GREEN in `c9e1ca1`: Plan names exact `Cert:\LocalMachine\TrustedPeople` and the UAC prerequisite; Full rejects non-admin before release writes and temporarily owns only the exact missing machine-trust entry; installer/uninstaller use the exact distributed CER thumbprint in the machine store only after Administrator preflight and explicit intent. Existing failure cleanup, success retention, pre-existing trust preservation, reparse guards, and exact-release checks remain green.
- Fresh safe verification: Packaging 39/39; Core 71 + Infrastructure 91 + App 138 + Packaging 39 = 339/339; Release build 0 warnings and 0 errors; deterministic asset parity 1/1; real non-mutating Plan resolves pinned `10.0.26100.8249` absolute x64 tools and explicitly reports the machine trust store; `git diff --check` has no whitespace errors apart from checkout line-ending notices.
- The corrected Full rerun, installer, uninstaller, registry, production app, and hardware were not executed in this fix round. The next action remains the controller-approved UAC checkpoint command above.

## Review fix round 4 — bounded junction helper processes

- RED: all three real `cmd.exe /c mklink` test paths used parameterless `WaitForExit()` and could hang the Packaging suite indefinitely if the helper process stalled.
- GREEN in `c7a5302`: a shared test-only bounded process host applies explicit 10-second command and 5-second termination/drain deadlines to every junction helper, attempts `Kill(entireProcessTree: true)` on timeout, waits only within the post-kill bound, drains redirected streams within a finite bound, and always disposes the process. Timeout failures identify the executable and exact deadline.
- The regression launches a real parent/child PowerShell tree whose child holds an exclusive file lock. At the three-second deadline the helper terminates the complete tree, returns within eight seconds, and the test immediately reacquires the lock, proving no child process retained it.
- Fresh verification: affected junction/timeout tests 5/5; Packaging 40/40; Core 71 + Infrastructure 91 + App 138 + Packaging 40 = 340/340; Release build 0 warnings and 0 errors; `git diff --check` clean apart from checkout line-ending notices.
- No production script changed, and Full/install/uninstall were not run. The controller-approved UAC checkpoint remains unchanged.
