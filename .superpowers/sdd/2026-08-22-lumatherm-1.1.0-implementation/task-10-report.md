# Task 10 implementation report

## Scope

- Added a draft `LightingTestViewModel` that owns exactly one Task 5 lighting-test session, serializes every slider temperature update, computes a live draft-profile color, and keeps preference persistence behind an injected save callback.
- Added Apply, Cancel, ordinary Close, open/set/save failure, cancellation, double-close, in-flight update, and mode-off restoration coverage with guaranteed session disposal.
- Added a themed modal `LightingTestWindow` using the reusable unlimited `ThermalProfileEditor`, existing panel/button/chrome visual language, an exact 0–120 °C slider, numeric temperature, saturated preview brush, accessible actions, and RU/EN dynamic resources.
- Connected Task 9's `LightingTestRequested` boundary to an owned production modal lifecycle. A pending/visible window suppresses duplicates, and UI-session shutdown awaits cleanup before forcing the permitted close.
- Kept Task 11 and later tray/About/packaging work out of scope.

## TDD evidence

### RED — lifecycle and async-closing contracts

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 1. No tests executed because compilation failed at `LightingTestViewModelTests.cs(192,20)` with expected `CS0246`: `LightingTestViewModel` did not exist. Core, Infrastructure, and App production projects built first; restore reported all projects up to date. No unrelated warning/error noise appeared.

### GREEN stabilization 1 — test-project diagnostics

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 1; no tests executed. Production compiled, then the warnings-as-errors test project identified only harness issues: two missing `System.IO.IOException` resolutions, two unused fake-event `CS0067` diagnostics, and xUnit1051 cancellation-token analyzer diagnostics. The harness gained the missing import, explicit no-op fake events, lifecycle overloads, and a documented narrow analyzer suppression because these tests deliberately exercise default and independently cancelled tokens.

### GREEN stabilization 2 — overload analyzer behavior

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 1; no tests executed. The xUnit analyzer continued to flag no-argument lifecycle calls because the production overload group also accepts a cancellation token. No compiler or production behavior failures remained. The suppression was moved to the complete lifecycle test file with its intentional-token rationale.

### GREEN — focused tests

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 0; 15 passed, 0 failed, 0 skipped; duration 665 ms. Restore was already current; no warnings, errors, or unexpected runtime noise.

### Full solution tests

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Result: exit code 0; 465 passed, 0 failed, 0 skipped:

- Core: 106 passed
- Infrastructure: 95 passed
- App: 203 passed
- Smoke: 19 passed
- Packaging: 42 passed

The five project results were interleaved by normal solution-level parallel execution. Restore was already current; no warnings, errors, or unexpected test noise appeared.

### Release build

Exact command:

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Result: exit code 0; 0 warnings, 0 errors; elapsed time 00:00:01.64. Restore was already current.

## Design notes

- `OpenAsync` is protected by a gate and shares the owned session, so repeated or concurrent calls cannot start duplicate Task 5 sessions.
- Slider changes synchronously enqueue the selected value against the active session and serialize asynchronous sends in assignment order. `TemperatureUpdate` provides a deterministic observation boundary for tests and close cleanup.
- Preview color is independently mapped from the modal's cloned draft profile. Editing the reusable profile editor never invokes the persistence callback.
- Apply awaits outstanding temperature work, invokes the supplied profile-save callback, and disposes in `finally`; save failures propagate only after disposal. Cancel and ordinary Close skip persistence and share the same idempotent completion task.
- A Close racing a pending Open marks completion first; if `BeginLightingTestAsync` subsequently returns a session, Open disposes it immediately instead of showing or leaking it.
- The first synchronous WPF `Closing` always cancels. Code-behind awaits ViewModel cleanup and only then reissues Close with the permit flag set. Repeated close gestures during cleanup remain cancelled.
- Production composition creates the modal from the current Settings draft profile, updates runtime preferences only through the injected callback, keeps committed mode ownership in Task 5, prevents duplicate windows, and pumps the dispatcher during synchronous application shutdown cleanup.
- All new visible labels use dynamic resource keys added in exact RU/EN parity. The existing localization parity test passed in the full suite.

## Self-review

- Mutation check: removing the open gate breaks the exactly-once test; removing/disordering temperature sends breaks the selected-value/in-flight tests; using the persisted profile for preview breaks the draft-color assertion; persisting from the slider breaks the no-save test; omitting any completion disposal breaks Cancel, Apply failure, mode-off, double-close, and async WPF close tests.
- Task 5 restoration was re-read: session disposal clears active test ownership before restoring disabled/suspended/enabled runtime state, so the window's awaited disposal satisfies the no-session-after-disappearance rule even if later release work reports an error.
- `git diff --check` completed with exit code 0 and no whitespace errors before full verification.
- Review found no Task 11 tray service/rendering, About, installer, or packaging changes.
- Hardware concern: automated coverage uses deterministic fake sessions plus the already-tested Task 5 runtime contract; no physical Windows LampArray device was available in this task run.

## Worktree/ACL note

The sandbox helper could not initialize deny-read ACLs for this worktree. Per the brief, patch files were created with `apply_patch` in the repository root and applied to the specified worktree with `git apply`; all task source changes in the worktree came from those patches.
