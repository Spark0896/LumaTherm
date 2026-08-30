# Task 14 implementation report

Date: 2026-08-29

Base: `88329b94995031540333957f850bf886ea044f40`

Scope: sparse identity, upgrade-safe Inno installer definition, portable registration helpers, and a mutation-free release Plan.

## Outcome

Task 14 is implemented without performing a real release, signing operation, certificate import/store write, package registration/removal, install/uninstall, or hardware write. The legacy full-package manifest was replaced by one authoritative sparse manifest. Portable registration and removal are guarded by exact identity checks, checksum/signature verification, path containment and reparse-point rejection. The release builder has a machine-readable `Plan` mode and fails closed in `Full` mode when required tools or external signing inputs are absent.

## TDD evidence

The complete `superpowers:test-driven-development` skill and its `writing-good-tests.md` reference were read before editing. Work proceeded in verified RED -> GREEN slices, followed by focused refactoring and regression runs.

1. Manifest identity slice
   - RED: the focused manifest run reported 2 expected failures because the sparse and native identity manifests did not exist in their required locations.
   - GREEN: the manifest test class passed 6/6 cases, including the four asset theory cases.
2. Registration helpers and Inno contract
   - RED: 6 focused tests failed for missing helpers/installer behavior.
   - GREEN: the initial helper/installer slice passed 6/6.
3. Release builder
   - RED: after correcting the fixture so failures represented product behavior, 5 release-builder tests failed for missing Plan/fail-closed/portable/checksum behavior.
   - GREEN: the release test class passed 6/6.
4. Security and upgrade refinements
   - Isolated installer payload: installer assertion RED; release Plan order/payload-root assertion RED; focused GREEN 3/3.
   - Stale payload and shortcut cleanup: focused RED, then GREEN.
   - Literal stable Inno AppId syntax: focused RED, then GREEN.
   - `UnknownError` accepted only for the exact untrusted-root chain case: focused RED, then GREEN 2/2.
   - Silent uninstall preserves user data: focused RED, then GREEN.
   - User-data descendant reparse rejection and removal of direct Inno `DelTree`: 2 focused failures RED, then GREEN 2/2.
5. Test-host regression
   - A Windows PowerShell invocation via `-File` stalled only when the release builder ran under xUnit, while the identical direct Plan command completed normally. The bounded test host gained a narrowly opt-in `-Command` invocation used only by release tests; helper tests retain `-File`. The full packaging suite verifies the fix.

All focused loops used the packaging project command with xUnit filters as appropriate:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false --filter <focused test/class expression>
```

## Final verification

### Packaging tests

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false
```

Exit 0: 25 passed, 0 failed, 0 skipped, 25 total.

### Direct release Plan

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Mode Plan
```

Exit 0. The single compact JSON object reported:

- version `1.1.0`;
- setup `LumaTherm-1.1.0-win-x64-setup.exe`;
- portable archive `LumaTherm-1.1.0-portable-win-x64.zip`;
- sparse package `LumaTherm-1.1.0-sparse.msix`;
- checksum file `SHA256SUMS.txt`;
- stable AppId `{9F6F5FEA-A89E-4D1C-9D0C-6C7C9FB5D310}`;
- exactly 13 planned commands: restore, test, publish, sparse-package build, app/package signing and verification, portable assembly, installer compile/sign/verify, and checksum emission;
- allowed write roots restricted to `artifacts\release` and `dist` under the repository;
- SignTool available at `C:\Users\User\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.8249\bin\10.0.26100.0\x64\signtool.exe`;
- MakeAppx available at `C:\Users\User\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.8249\bin\10.0.26100.0\x64\makeappx.exe`;
- ISCC unavailable at `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`;
- `certificateSource: not-provided`, `licenseAvailable: false`, `privateKeyOutputs: []`;
- `certificateImport`, `packageRegistration`, `systemStoreWrite`, `install`, `uninstall`, `hardwareWrite`, and `signing` all `false`.

No secret value or private certificate path appeared in Plan output; planned sign commands use `<external-pfx>` and `<secure-password>` placeholders.

### Full solution tests

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: 543 passed, 0 failed, 0 skipped:

- Core: 123
- Infrastructure: 111
- Packaging: 25
- App: 265
- Smoke: 19

### Release build

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: build succeeded with 0 warnings and 0 errors.

### Parser and diff checks

```powershell
$paths = @('.\scripts\build-release.ps1','.\scripts\Register-LumaTherm.ps1','.\scripts\Unregister-LumaTherm.ps1','.\scripts\install.ps1','.\scripts\uninstall.ps1'); $failed = $false; foreach ($path in $paths) { $tokens = $null; $errors = $null; [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $path), [ref]$tokens, [ref]$errors) | Out-Null; if ($errors.Count -gt 0) { $failed = $true; Write-Output "$path FAIL"; $errors | ForEach-Object { Write-Output $_.Message } } else { Write-Output "$path OK" } }; if ($failed) { exit 1 }
git diff --check
```

Parser exit 0: all 5 scripts OK. Diff check exit 0 with no whitespace errors. Git emitted only line-ending notices that LF will become CRLF for `scripts/install.ps1`, `scripts/uninstall.ps1`, and `src/LumaTherm.App/LumaTherm.App.csproj` when Git next rewrites those files.

## Security audit

- Native and sparse manifests use exact identity parity: name/package `LumaTherm`, publisher `CN=LumaTherm Local`, application ID `LumaTherm`; sparse version is `1.1.0.0`, architecture x64, external content is enabled, and only `com.microsoft.windows.lighting` is declared.
- Registration accepts only paths resolved beneath the explicit portable root, rejects reparse points along each path, requires the exact sparse-package filename and app executable, and rejects missing, unexpected, duplicate, traversing, or checksum-mismatched files before any privileged write.
- The sparse package Authenticode signer thumbprint must exactly match bundled `LumaTherm.cer`. `UnknownError` is allowed only when certificate-chain status consists solely of `UntrustedRoot`; all other signature states fail closed.
- Public-certificate import requires administrator preflight and explicit confirmation. Test overrides require both `LUMATHERM_PACKAGING_TEST=1` and `-AuditOnly`; they cannot drive mutations.
- Registration invokes exactly `Add-AppxPackage -Path $identityPackage -ExternalLocation $applicationDirectory`, then verifies the exact installed name and publisher.
- Unregistration queries and removes only exact name `LumaTherm` and publisher `CN=LumaTherm Local`, rejects ambiguity/lookalikes, and uses no wildcard.
- User data is preserved during install, upgrade, normal uninstall, and silent uninstall. Visible uninstall offers explicit opt-in cleanup through the guarded unregister helper. Cleanup rejects a reparse point at the local root, target, or any descendant before recursive removal.
- Inno uses one stable explicit AppId, an isolated checksummed payload, exact executable close filter, stale payload/shortcut cleanup, unregister-before-replace and register-after-install sequencing, and raises clear errors when helper execution fails.
- Full release requires a PFX outside repository/dist, opens it with ephemeral key storage, exports only a public `.cer`, never copies/logs a PFX, requires all external tools, and verifies exact signer thumbprints after signing.
- Deterministic portable ZIP timestamps and sorted SHA-256 output are behavior-tested. The public checksum file covers exactly the setup EXE and portable ZIP; the portable archive contains its own checksum coverage for every other file.
- Official Inno Setup documentation was consulted to confirm `function UninstallSilent: Boolean;` is a supported Pascal scripting function. ISCC compilation remains deferred because ISCC is not installed.

## External blockers and deferred execution

- Inno Setup 6 compiler (`ISCC.exe`) is not installed at the resolved location.
- No external release PFX or password was supplied. Full mode intentionally refuses PFX files inside the repository or distribution roots.
- The repository has no `LICENSE`, so `licenseAvailable` is false and Full mode fails closed until approved license text exists.
- Consequently the real Full release pipeline was not executed. Task 16 must provide approved external signing inputs, ISCC, and LICENSE, then perform actual publish/sign/verify/package/install lifecycle validation.

## Self-review

The final diff is limited to the Task 14 files named in the brief plus this report. The legacy `packaging/AppxManifest.xml` is deleted so there is one authoritative sparse manifest. No generated build/test outputs are staged. No private keys, passwords, certificate-store changes, package registrations/removals, installer/uninstaller executions, or hardware writes were produced during Task 14.

## Fix Round 1 — independent security review

Date: 2026-08-29

The independent review rejected the first implementation with one Critical and seven Important findings. Each finding was reproduced and corrected without running a real signer, certificate-store operation, package registration/removal, installer/uninstaller, or hardware operation.

### Verified RED -> GREEN evidence

The initial security regression class was run with:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false --filter FullyQualifiedName~Task14SecurityRegressionTests
```

Initial RED: exit 1, 19 failed and 4 passed of 23. Failures named the absent signed payload anchor, executable signer validation, explicit silent consent, exact post-registration verification/rollback, ancestor reparse rejection, ambiguous unregister rejection, honest Plan metadata, transactional success, and PFX indirection rejection. The four downstream-failure cases initially passed only because the old parameter binder rejected `TestTransaction`; their assertions were tightened to require the exact injected failure message before implementation.

First GREEN attempt: 21 passed, 2 failed of 23. Diagnostic assertions exposed two root causes:

- audit-only registration planning incorrectly continued into post-registration verification;
- Windows PowerShell 5.1 materialized the JSON candidate array as one object with array-valued properties.

After minimal fixes, the two focused cases passed 2/2. The complete security regression class then passed 23/23. Expanded rollback, missing-tool, and consent cases brought that class to 29/29.

A second focused RED added exact publish-output and accurate Full write-domain requirements:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false --filter "FullyQualifiedName~PlanNamesEveryArtifactToolCommandAndForbiddenSideEffect|FullyQualifiedName~PortableAssemblyRejectsUnexpectedPublishOutputsBeforeCreatingZip"
```

RED: exit 1, 2 failed of 2. GREEN after the minimal builder changes: exit 0, 2 passed of 2.

### Review finding resolutions

1. Portable trust chain (Critical)
   - `LumaTherm.exe` Authenticode status and signer thumbprint are independently verified against bundled `LumaTherm.cer`.
   - `PayloadHashes.json` is generated after application signing, included inside the subsequently signed sparse MSIX, and read only after the sparse signer/thumbprint check.
   - The signed anchor exactly covers the app, certificate, helpers, README and LICENSE. The sparse package authenticates itself; the rewriteable internal checksum is treated as transport integrity only.
   - Replaced executable plus rewritten checksums, wrong executable signer, changed anchored content, unexpected content and invalid executable signatures all fail before registration.
2. Explicit certificate consent
   - Visible Inno setup shows a certificate-specific prompt naming the public certificate and `LocalMachine\TrustedPeople`, defaulting to No.
   - Silent setup refuses unless the caller supplies exact `/ALLOWCERTIMPORT`; only an approved path passes `-ConfirmCertificateImport` to the helper.
   - Helper tests execute visible accept/decline and silent accept/refuse paths.
3. Post-registration verification
   - After exact `Add-AppxPackage`, registration verifies one exact name/publisher, version `1.1.0.0`, application ID `LumaTherm`, exact lighting extension and effective external location.
   - Real external-location verification uses WinRT `Package.EffectiveExternalLocation`, the documented package identity API for external-location packages, and compares its path with the resolved application directory.
   - Any mismatch rolls back the exact newly added package and fails.
4. Transactional public artifacts
   - Portable ZIP and installer are built under private `artifacts\release\public-staging`.
   - Setup, portable ZIP and checksums are promoted together only after compilation, signing, Authenticode/thumbprint verification and checksum validation.
   - Compile, sign, verify and checksum failure injection proves no final-named releasable partials appear and a prior `dist` remains intact.
5. Reparse containment and PFX indirection
   - Release and portable path validation now walks every existing component from the volume root and rejects any junction/symlink/reparse point before writes or privileged operations.
   - External PFX paths reject reparse indirection, resolve the actual item, and enforce the resolved path remains outside repository and dist.
   - Junction tests preserve external sentinels for release-tree, portable-ancestor and PFX-indirection cases.
6. Honest, read-only Plan
   - SDK discovery is filesystem-only at the pinned NuGet package location. Plan runs no msbuild or external tool process.
   - Snapshot tests prove the controlled fixture is unchanged.
   - `planWriteRoots` and legacy `allowedWriteRoots` are empty; `fullBuildWriteRoots` separately enumerates release/dist and every project `bin/obj`; `cacheWriteRoots` identifies the NuGet package cache.
7. Trust rollback
   - A newly imported exact thumbprint is removed after package/executable reverify, Add-AppxPackage, or post-verification failure.
   - Preexisting trust is never removed. Tests cover all three failure boundaries plus the preexisting certificate case.
8. Restored material coverage
   - Coverage now includes exact/extra checksums and signed anchor entries, ambiguous unregister input, missing SignTool/MakeAppx/ISCC/PFX, invalid package/app signatures, self-contained single-file publish arguments and exact output, downstream cleanup/promotion, Inno consent/upgrade failure routing, and preserved opt-in settings cleanup.
   - Packaging case count increased from 25 to 55: 30 added cases including theory rows. No security-equivalent test was removed.

### Fresh final verification

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false
```

Exit 0: 55 passed, 0 failed, 0 skipped.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Mode Plan
```

Exit 0. JSON reports the exact three artifact names and stable AppId, `toolDiscoveryStrategy: filesystem-only`, empty `planWriteRoots`/`allowedWriteRoots`, explicit Full project/cache write domains, 15 ordered stages including signed-anchor emission and final promotion, SignTool and MakeAppx available, ISCC unavailable, no private-key outputs, no supplied certificate, no LICENSE, and every forbidden Plan side effect including process/file writes false.

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: 573 passed, 0 failed, 0 skipped:

- Core: 123
- Infrastructure: 111
- Packaging: 55
- App: 265
- Smoke: 19

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: 0 warnings, 0 errors.

All five PowerShell scripts parse without errors. `git diff --check` reports no whitespace errors; the only noise is the existing LF-to-CRLF notice for `packaging/LumaTherm.iss`.

### Remaining external blockers and safety statement

ISCC remains unavailable, no approved external PFX/password was supplied, and the repository still has no LICENSE. Therefore the real Full release and Inno compilation remain correctly deferred to Task 16. This fix round performed only test-fixture/audit simulations and the mutation-free Plan; it did not execute real signing, certificate import/store removal, Add/Remove-AppxPackage, installer/uninstaller, or hardware writes.

## Fix Round 2 — material executable coverage

Date: 2026-08-29

The scoped re-review found no new production security defect. It required three mutation-sensitive executable checks that the first fix round had not fully restored. The complete TDD and `writing-good-tests.md` instructions were re-read before editing.

### Publisher/certificate mismatch gate

The new Full-mode integration fixture supplies a real external temporary X509 PFX, present fake SDK/Inno tools, and a safe copied `whoami.exe` at the production dotnet boundary. If the publisher gate is bypassed, the fake external process is reached and emits its unique invalid-argument sentinel; the fixture also observes the repository release/public trees.

Mutation RED: the real subject-equality gate was temporarily removed, then:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release --no-restore -p:NuGetAudit=false --filter FullyQualifiedName~FullRejectsPublisherCertificateMismatchBeforeAnyToolOrPublicWrite
```

Exit 1: 1 failed of 1. The result was `Release restore failed` instead of `Publisher does not match`, proving that the mutation reached the external tool boundary. The production gate was immediately restored. GREEN: the same command exited 0 with 1/1 passed; the tool sentinel, `artifacts`, and `dist` were all absent.

### Real asset-generator parity

The new asset integration test parses every PNG reference from the real sparse manifest, requires the exact four approved filenames, copies only the authoritative logo XAML into a unique temporary repository root, executes the real `LumaTherm.AssetBuilder` through the pinned repository dotnet host, requires an exact generated PNG file set, and compares every generated PNG byte-for-byte with its committed counterpart. The GUID output directory is deleted after every run.

Mutation RED: `StoreLogo.png` generation was temporarily changed from 50x50 to 49x50. The focused command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release --no-restore -p:NuGetAudit=false --filter FullyQualifiedName~AssetBuilderReproducesEveryManifestReferencedPngByteForByte
```

Exited 1 with 1/1 failed and a byte mismatch at PNG width byte position 19 (`50` expected, `49` actual). The generator mutation was immediately reversed. GREEN: the identical command exited 0 with 1/1 passed.

### Executable Inno consent boundary

The source-token-only consent test was replaced. `Resolve-LumaThermInstallerConsent.ps1` now provides a pure executable policy boundary and generates the committed `LumaTherm.Consent.iss`. The actual installer includes that generated boundary and obtains the exact registration command from it. Tests execute eight policy rows:

- visible Default and Decline refuse;
- visible Accept approves;
- silent mode refuses without a caller token even if a visible decision says Accept;
- silent mode accepts exact `/ALLOWCERTIMPORT` case-insensitively;
- `/ALLOWCERTIMPORT=1` and a leading-space variant refuse;
- only approval returns the exact registration command ending in `-ConfirmCertificateImport`.

RED before implementation:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false --filter "FullyQualifiedName~InstallerConsentPolicyRequiresVisibleAcceptanceOrExactSilentOptIn|FullyQualifiedName~GeneratedInnoConsentBoundaryMatchesTheCommittedInclude"
```

Exit 1: 9 failed of 9 because the executable policy/generator script did not exist. GREEN after the minimal boundary/include wiring: exit 0, 9/9 passed. Generated include parity is exact after newline normalization. The stale legacy assertion that required the registration argument to remain in the main `.iss` source was removed; its behavior is now covered by these executable rows, generated parity, and actual compiler validation.

### Official compiler acquisition and actual `.iss` compilation

No local ISCC was found in either standard Program Files location, PATH, or the user profile. Official sources confirmed that Inno Setup 6.7.3 supports `/PORTABLE=1`. The immutable official release was acquired from:

`https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe`

The downloaded SHA-256 exactly matched the published value:

`9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`

The first portable invocation populated only its unique temp root, but PowerShell left `$LASTEXITCODE` blank. Work stopped at that boundary and the recovery was explicitly approved. Before retry, the first root had no `unins*.exe`, association/uninstall registry reference, PATH reference, or related running process. The same hash-verified file was then invoked with:

```powershell
Start-Process -Wait -PassThru -WindowStyle Hidden <verified-official-installer> /PORTABLE=1 /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=<unique-temp-compiler> /TASKS=""
```

Exit code was exactly 0. Every extracted object remained under the new temp root, no reparse point or uninstaller was created, and targeted association/uninstall registry state, CurrentUser/LocalMachine Root and TrustedPeople certificate stores, and process/user/machine PATH snapshots were unchanged.

Compiler identity evidence was the hash-verified Inno Setup 6.7.3 distribution and the `ISCC /?` banner `Inno Setup 6 Command-Line Compiler`; this ISCC build reports file `ProductVersion` as `0.0.0.0`, which is recorded rather than replaced with an inferred file-resource value.

With `LUMATHERM_TEST_ISCC` set only for the test process, the actual script was compiled by the test boundary equivalent to:

```powershell
ISCC.exe /Qp /O<unique-temp-output> /DPayloadRoot=<unique-temp-payload> packaging\LumaTherm.iss
```

The focused compiler test exited 0 and found `LumaTherm-1.1.0-win-x64-setup.exe`. That output was deleted in `finally` and was never executed. The 12 combined publisher, asset, consent, generator-parity, and compile-only cases passed 12/12.

After testing, both exact compiler/download temp roots and three verified-empty test-parent directories were removed. Final checks found zero compiler/setup processes, temp remnants, registry references, or PATH references. The earlier certificate-store comparison remained unchanged. Inno was not persisted and PATH/system associations were not modified.

### Fresh final verification

Packaging with the verified temporary ISCC enabled only for actual compile validation:

```powershell
$env:LUMATHERM_TEST_ISCC = '<verified-temp-ISCC>'
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false
```

Exit 0: 66 passed, 0 failed, 0 skipped. The case count increased from 55 to 66: publisher gate +1, asset generator +1, the former single source-token consent case replaced by eight behavior rows (+7 net), generated parity +1, and actual compile boundary +1.

Direct mutation-free Plan:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Mode Plan
```

Exit 0. The Plan still reports the exact artifacts/stable AppId, filesystem-only discovery, empty Plan/allowed write roots, 15 stages, SignTool and MakeAppx available, default persistent ISCC unavailable, no PFX, no LICENSE, no private-key outputs, and every forbidden side effect false.

The full solution command was executed, but the task tool stream repeatedly omitted only the Packaging footer/final exit after all test processes ended. Completion was therefore established without extrapolation by fresh explicit project exits: Packaging 66, Core 123, Infrastructure 111, App 265, and Smoke 19, all exit 0. Aggregate: 584 passed, 0 failed, 0 skipped across every solution test project.

Release build:

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: 0 warnings, 0 errors.

All six PowerShell scripts parse without errors and `git diff --check` reports no whitespace errors. No real LumaTherm signing, certificate import/store removal, package registration/removal, installer/uninstaller execution, or hardware write occurred. Remaining external Full-release blockers are unchanged: no persistent ISCC installation, no approved external PFX/password, and no repository LICENSE.

## Fix Round 3 - enforced official Inno compile gate (2026-08-30)

### Finding and durable boundary

The prior `ActualInnoScriptCompilesWhenOfficialCompilerIsProvided` test returned success when `LUMATHERM_TEST_ISCC` was absent and accepted any existing executable path when present. It was removed. The checked-in `scripts/Test-LumaThermInnoCompile.ps1` is now the mandatory release-validation boundary; it has no optional compiler environment variable and its `OfficialInstallerPath` parameter is mandatory.

The gate pins the immutable official Inno Setup 6.7.3 release URL and SHA-256 internally:

- URL: `https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe`
- SHA-256: `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`

It rejects a missing or mismatched artifact before creating its validation root or starting a process. After verification it creates one contained GUID temp root, copies the artifact into that owned root, verifies the copy against the same pinned hash to close the check/use race, and executes only that owned copy with `/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART`. It rejects reparse components, extracted reparse points, and an extracted uninstaller. The compiler is therefore provenance-anchored to the exact hash-verified official distribution, not to an arbitrary `ISCC.exe` path.

The gate compiles the real `packaging/LumaTherm.iss` with a harmless isolated payload and output, requires exactly `LumaTherm-1.1.0-win-x64-setup.exe`, records that it was not executed, and deletes the setup together with the exact validation root in `finally`. Extraction and compilation are each bounded to 120 seconds and their exact launched processes are stopped on timeout before cleanup.

The persistent Task 14/Task 16/CI enforcement command is:

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\Test-LumaThermInnoCompile.ps1 -OfficialInstallerPath <downloaded-unmodified-innosetup-6.7.3.exe>
```

Omitting `-OfficialInstallerPath`, supplying an arbitrary compiler, or supplying any artifact other than the pinned distribution fails the command. Ordinary clean-Windows tests do not download or install Inno and instead execute the safe failure paths.

### RED -> GREEN evidence

Covering files are `tests/LumaTherm.Packaging.Tests/InnoCompileGateTests.cs` and `scripts/Test-LumaThermInnoCompile.ps1`. `Task14SecurityRegressionTests.cs` no longer contains the conditional compiler test.

RED, before the entrypoint existed:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~InnoCompileGate"
```

Exit 1: 0 passed, 2 failed. Both failures reported that the expected mandatory-parameter/hash-mismatch contracts were absent. This converts the previous unset-environment silent pass and arbitrary-path trust into executable failures.

GREEN after the minimal boundary, including final provenance assertions: the identical command exited 0 with 2/2 passed. `InnoCompileGateFailsWhenOfficialInstallerInputIsOmitted` proves omission fails. `InnoCompileGateRejectsUnpinnedExecutableBeforeExecutionAndCleansTemp` supplies a fake `.exe`, requires the exact immutable URL and pinned SHA-256 in the mismatch result, proves the sentinel was not created, and proves the validation-root snapshot is unchanged.

During implementation, the first GREEN attempt exposed that this Windows PowerShell host did not provide `Get-FileHash`; the gate now uses the repository's direct .NET SHA-256 pattern. Two controlled official attempts then hit the 120-second extraction bound before output. Each failed closed: the exact launched process was stopped, validation temp was removed, the wrapper removed only its GUID download root, and no LumaTherm setup existed. Read-only process evidence showed that the correct arguments reached the bootstrapper. Official setup source/command-line documentation identified separate startup-prompt and non-administrative-mode controls; `/SP-` and `/CURRENTUSER` made the portable extraction deterministic and explicitly non-admin. A subsequent run reached ISCC and exposed Windows PowerShell's empty `Start-Process` exit-code observation with redirected streams; the compiler boundary now uses a bounded direct .NET process with an exact numeric exit code. These were root-cause fixes, not relaxed assertions.

### Official compiler success and cleanup

The final approved temp-only acquisition again used the immutable URL above. The independently calculated download hash and the gate's internal hash both exactly matched the pinned value. The gate returned exit 0 with:

- compiled script: `packaging/LumaTherm.iss`;
- compiler provenance: `ISCC.exe extracted from the pinned hash-verified official distribution`;
- extracted compiler SHA-256: `0a8757031b33777e4c9cbffee40f11a5062b36d25cbe144c1db73b6102b80ad7`;
- expected setup artifact verified: true;
- setup artifact executed: false;
- validation temp root cleaned: true.

The outer exact download root was also removed. A final process/temp scan found no `ISCC`, Inno Setup 6.7.3, LumaTherm setup process, or `LumaTherm-inno-*` root. Inno was not persisted, PATH was not changed, and the compiled LumaTherm installer was never run.

### Fresh verification

Complete Packaging:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false
```

Exit 0: 67 passed, 0 failed, 0 skipped. The count changed from 66 to 67 because the one conditional compiler case was replaced by two mandatory-gate behavior cases.

Direct mutation-free Plan exited 0 with the same stable AppId and exact artifacts, filesystem-only discovery, empty Plan/allowed write roots, 15 planned stages, SignTool and MakeAppx available, persistent ISCC unavailable, no PFX or LICENSE, no private-key output, and every forbidden side effect false.

Full solution:

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: Packaging 67, Core 123, Infrastructure 111, App 265, Smoke 19; aggregate 585 passed, 0 failed, 0 skipped.

Release build:

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: 0 warnings, 0 errors. PowerShell parsing and `git diff --check` also pass. No real LumaTherm signing, certificate-store mutation, package registration/removal, LumaTherm install/uninstall, compiled-setup execution, or hardware write occurred. The only external executable mutation was the explicitly approved official Inno `/PORTABLE=1 /CURRENTUSER` extraction inside owned temp, fully cleaned afterward. Full release remains blocked on a persistent/explicit ISCC input, approved external PFX/password, and repository LICENSE; the new validation gate makes the official compiler input omission a deliberate failure rather than a skipped test.

## Fix Round 4 - containment boundary and clean JSON stdout (2026-08-30)

### Scope and root-cause evidence

The open finding named `scripts/Test-LumaThermInnoCompile.ps1:65-66`: a separately emitted directory separator would leave only a trimmed parent prefix, accept a sibling such as `C:\Temp-sibling`, and place `\` on stdout before the gate's JSON result. The covering file is `tests/LumaTherm.Packaging.Tests/InnoCompileGateTests.cs`; it executes the real `Assert-ContainedPath` function body through the PowerShell AST rather than grepping its source.

Three focused behavior cases now protect the contract:

- `ContainmentRejectsSiblingThatSharesTheParentPrefix` requires the sibling-prefix path to fail with the gate's containment error.
- `ContainmentRejectsTheExactParentBecauseGateCallersRequireDescendants` makes the existing caller semantics explicit: every gate call supplies a strict descendant, so the owned root itself is rejected.
- `CaseInsensitiveContainedPathLeavesStdoutAsExactlyOneJsonDocument` proves a Windows case-variant descendant is accepted and stdout is exactly `{"contained":true}` plus the host newline, parseable as one JSON document with no preceding pipeline object.

On this Windows PowerShell host, the checked-in trailing `+` expression was parsed as continuation, so the three tests initially passed against HEAD. No contrary evidence was hidden. To verify that the tests catch the exact reviewer-described regression before changing production code, the helper was temporarily mutated to the claimed behavior: assign only the trimmed parent, then evaluate `DirectorySeparatorChar` as its own statement.

### Verified RED -> GREEN evidence

The exact mutation run was:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release --no-restore -p:NuGetAudit=false --filter 'FullyQualifiedName~Containment|FullyQualifiedName~CaseInsensitiveContainedPath'
```

RED: exit 1, 0 passed and 3 failed of 3. The sibling and exact-parent probes both returned exit `0` instead of rejection. The JSON assertion reported this exact decoded output after trimming the final host newline:

```text
Expected: {"contained":true}
Actual:   \\r\n{"contained":true}
```

That is one literal backslash and CRLF before the JSON object, matching the open finding exactly. The temporary mutation was then replaced, not retained.

The minimal production change constructs `parentPrefix` explicitly with `[string]::Concat`, normalizes both platform separator characters, compares with `OrdinalIgnoreCase`, and rejects equality before the boundary-prefixed descendant check. No containment call writes a value to the pipeline.

GREEN: the identical focused command exited 0 with 3/3 passed. The complete focused class command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release --no-restore -p:NuGetAudit=false --filter 'FullyQualifiedName~InnoCompileGateTests'
```

Exited 0 with 5 passed, 0 failed, 0 skipped. This includes the two pre-existing mandatory official-input/hash-rejection gates plus all three containment/output cases.

### Fresh final verification

Complete packaging suite:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false
```

Exit 0: 70 passed, 0 failed, 0 skipped. The suite increased from 67 to 70 only through the three focused regressions above.

Direct mutation-free Plan:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Mode Plan
```

Exit 0. Stdout was exactly one compact JSON document. It parsed successfully and retained version `1.1.0`, setup `LumaTherm-1.1.0-win-x64-setup.exe`, portable archive `LumaTherm-1.1.0-portable-win-x64.zip`, sparse package `LumaTherm-1.1.0-sparse.msix`, checksum file `SHA256SUMS.txt`, stable AppId `{9F6F5FEA-A89E-4D1C-9D0C-6C7C9FB5D310}`, 15 planned commands, filesystem-only discovery, empty Plan/allowed write roots, no private-key outputs, and every forbidden side effect false. SignTool and MakeAppx were available; persistent ISCC, PFX and LICENSE remained unavailable.

Full solution:

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: Packaging 70, Core 123, Infrastructure 111, App 265, Smoke 19; aggregate 588 passed, 0 failed, 0 skipped.

Release build:

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit 0: 0 warnings and 0 errors. `scripts/Test-LumaThermInnoCompile.ps1` parsed without PowerShell errors. `git diff --check` exited 0; its only output was the existing LF-to-CRLF notices for the two modified code/test files.

### Safety and remaining concern

This round performed only test-host process execution, the mutation-free release Plan, .NET tests/build, and parser/diff inspection. It did not download or run the official Inno bootstrapper, run ISCC, execute a generated LumaTherm installer, sign artifacts, touch a certificate store, register/remove a package, install/uninstall LumaTherm, or access lighting hardware.

The only material concern is recorded above: this host did not reproduce the ambiguous trailing-operator behavior until the exact claimed mutation was applied. The final code no longer depends on that parsing/layout nuance, and the mutation-sensitive behavior tests prove the security and stdout contracts directly. Full release blockers remain the missing persistent ISCC input, approved external PFX/password, and repository LICENSE.
