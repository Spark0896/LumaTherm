# Task 13 report — safe smoke phase

## Scope and boundary

Implemented Steps 1–5 only. No signed-release script, package installation, registry/autostart operation, sign-out, suspend/resume, GCC/RGB Fusion inspection, process control, raw HID, vendor DLL, or physical light write was performed. The only live hardware commands were `sensor`, forced-fallback `sensor --skip-nvml`, and `lights` discovery.

## Test-first evidence

Initial smoke-project scaffolding had no executable entry point, so the first focused command stopped at compiler error `CS5001` (no suitable `Main`). The scaffold was changed to a library so that the tests could execute before any smoke-command implementation.

Witnessed RED command:

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Smoke.Tests\LumaTherm.Smoke.Tests.csproj -p:NuGetAudit=false
```

Result: exit 1; 7 failed, 0 passed. Every failure was the intended `InvalidOperationException: SmokeCommand does not exist yet.` No production smoke-command implementation existed at that point.

Focused GREEN command (after implementation):

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Smoke.Tests\LumaTherm.Smoke.Tests.csproj -p:NuGetAudit=false
```

Result: exit 0; 7 passed, 0 failed. The tests cover sensor output and forced fallback selection, discovery without ownership, denial without `--confirm-light-write`, ordered cycle colors, release after a second-write failure, and smoothed simulation release.

Fresh full GREEN command:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln -c Release -p:NuGetAudit=false
```

Result: exit 0; Core 71 + Infrastructure 91 + App 138 + Packaging 40 + Smoke 7 = 347 passed, 0 failed. The implicit Release build completed with no warnings/errors.

`scripts/build-release.ps1`, clean/rebuild artifact hashing, and package-signing verification were intentionally not run: the controller ruling reserves visible-UAC/transient-trust signing for a separate checkpoint.

## Read-only device evidence

All three commands ran from source commit `0ec3a81f00eba05039b2e9130ff8836d8ee0783b` with `-c Release -p:NuGetAudit=false`.

```powershell
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -p:NuGetAudit=false -- sensor --json
```

Exit `0`:

```json
{"status":"pass","provider":"NVML","gpu":"NVIDIA GeForce RTX 5070","temperatureC":71}
```

```powershell
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -p:NuGetAudit=false -- sensor --skip-nvml --json
```

Exit `1`:

```json
{"status":"error","message":"MSI Afterburner \u043D\u0435 \u043F\u0440\u0435\u0434\u043E\u0441\u0442\u0430\u0432\u0438\u043B \u0442\u0435\u043C\u043F\u0435\u0440\u0430\u0442\u0443\u0440\u0443 GPU. \u0423\u0431\u0435\u0434\u0438\u0442\u0435\u0441\u044C, \u0447\u0442\u043E Afterburner \u0437\u0430\u043F\u0443\u0449\u0435\u043D \u0438 shared memory \u0432\u043A\u043B\u044E\u0447\u0435\u043D\u0430."}
```

This is an observed, actionable fallback failure, not a PASS: MSI Afterburner must be running with shared memory enabled before retrying.

```powershell
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -p:NuGetAudit=false -- lights --json
```

Exit `0`:

```json
{"status":"pass","devices":[{"id":"\\\\?\\HID#VID_048D&PID_5702&MI_00#a&30f63cd2&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}","name":"GIGABYTE Device","lampCount":1,"available":true}]}
```

Timestamp for the recorded read-only evidence: `2026-08-20T19:40:47.5837960+03:00`. Windows version was not collected because the authorized live probe set was limited to the three smoke commands.

## Files and commits

- Smoke tool, solution integration, and tests: `0ec3a81f00eba05039b2e9130ff8836d8ee0783b` (`test: add safe hardware smoke diagnostics`).
- README and hardware evidence: `047477e64c1c75099b8c435211620ffb9e6f3362` (`docs: record smoke evidence and hardware gates`).
- New tool files: `tools/LumaTherm.Smoke/Program.cs`, `tools/LumaTherm.Smoke/SmokeCommand.cs`, and `tools/LumaTherm.Smoke/LumaTherm.Smoke.csproj`.
- New tests: `tests/LumaTherm.Smoke.Tests/LumaTherm.Smoke.Tests.csproj` and `tests/LumaTherm.Smoke.Tests/SmokeCommandTests.cs`.

## Self-review

- `sensor` constructs no LampArray adapter and uses the production NVML-first/MAHM-fallback source arrangement; `--skip-nvml` constructs MAHM-only provider wiring.
- `lights` calls only `DiscoverAsync`; it does not call `ConnectAsync`, `SetColorAsync`, `Enable`, or `Disable`.
- `cycle`/`simulate` require the explicit flag and exact interactive `YES`; `--non-interactive` was removed after review. Prompts use stderr and JSON stdout has one terminal result. Ctrl+C cancellation reaches `ReadLineAsync(CancellationToken)`. Release/dispose calls are isolated behind a finite two-second deadline; success is emitted only after cleanup.
- Simulation uses the existing `ColorEngine` and `ThermalProfile.Default`; there is no duplicate interpolation/smoothing implementation.
- The forced MAHM failure and the uncollected Windows version are reported as such. Documentation marks all physical/install acceptance checks pending.

## Remaining controller gates

1. Visible-UAC signed release rebuild, release hashes, and artifact validation.
2. Explicit user confirmation before the first `cycle --confirm-light-write` or `simulate --confirm-light-write`; visually verify colors/transition and ownership restoration.
3. Installed-app first-run/dashboard/settings/tray/single-instance/autostart/sign-out checks.
4. Sensor-loss/recovery, suspend/resume, five-minute tray soak/log bound, uninstall, and final visual comparison.
5. Diagnose the optional MAHM fallback only after the controller permits external application-state checks; rerun forced fallback then record its actual result.

## Review fix round 1/5

Witnessed RED added to the initial record: `--non-interactive` produced exit `0` where rejection exit `2` was required; prompt-aware tests also failed because the dedicated prompt-writer overload did not exist. A prior RED test-host process did not terminate and held the Debug copy of the smoke DLL; it was inspected read-only and not terminated. Release isolation avoided that stale Debug lock.

The fix removes the public bypass, routes prompts to stderr, makes `ReadLineAsync` cancellation-aware, validates the 0.1-second minimum duration, uses literal independently derived smoothing frames `#50C8FF`, `#50DFFF`, `#4FFFF2`, and emits one JSON result after bounded release/dispose. Non-cooperative synchronous cleanup is run on a background task with a two-second deadline; timeout gives a single error and skips concurrent lighting disposal. This cannot force a stuck platform call to stop in-process, but it avoids any foreground-process orphan or false PASS.

Fresh review verification:

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Smoke.Tests\LumaTherm.Smoke.Tests.csproj -c Release -p:NuGetAudit=false
& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln -c Release -p:NuGetAudit=false
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln -c Release -p:NuGetAudit=false
```

Results: Smoke `17/17`; build exit `0`, warnings `0`, errors `0`; full Release Core 71 + Infrastructure 91 + App 138 + Packaging 40 + Smoke 17 = `357/357`. `[Environment]::OSVersion.VersionString` observed `Microsoft Windows NT 10.0.26200.0`. No physical write, signing, install, UAC, or external app control occurred.

Controller-authorized stale test cleanup: following read-only verification of path, start time, parent and command line, only stale RED-run Debug VSTest trees were terminated. Root `23820` (created 19:56:14) contained testhost `8920`, test exe `17888`, and conhost `6468`; root `16772` (created 19:56:46) contained testhost `10512`, test exe `24704`, and conhost `9132`. All command lines were under `tests\LumaTherm.Smoke.Tests\bin\Debug`. `taskkill /PID 23820 /T /F` and `taskkill /PID 16772 /T /F` succeeded; a one-second bounded post-kill process check found none of those PIDs. No unrelated dotnet process was touched. The harness had abandoned active child test trees after a timeout; the revised test fakes block only five seconds and command cleanup returns a two-second timeout error rather than waiting indefinitely.
