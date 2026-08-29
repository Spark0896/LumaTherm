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
