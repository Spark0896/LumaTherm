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

## Fix round 1 — lifecycle, shutdown ownership, and Apply coherence

### RED — Close/Open acquisition race

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 1; 15 passed, 2 failed, 0 skipped. Both deterministic blocked-`BeginLightingTestAsync` tests failed at `Assert.False`: Close completed before the pending Begin yielded a session. The two failures were `CloseAsync_WhileBeginIsBlocked_WaitsForLateSessionDisposal` and `ConcurrentDoubleOpenAndClose_SharesBeginAndLateDisposalBarrier`. An earlier test-harness compile attempt identified and corrected use of `WaitAsync` on `TaskCompletionSource` instead of its `Task`; no production code was changed before the behavior-level RED.

### GREEN — Close/Open acquisition race

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 0; 17 passed, 0 failed, 0 skipped; duration 639 ms. `CompleteCoreAsync` now waits on the same non-cancellable open gate before finishing cleanup, so a late session is disposed before Close/Apply/Cancel completes. The concurrent second Open is deterministically rejected after Close wins while only one Begin and one disposal occur.

### RED — authoritative Apply synchronization

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 1; no tests executed. The two new integration tests failed compilation with expected `CS1061` because `SettingsViewModel.ApplyLightingTestProfileAsync` did not exist. The tests require Apply to update the Settings editor and `LiveSettings`, flow through `ProfileSaved` to the dashboard, supply the applied profile to a reopened test, and survive a later ordinary Settings Save.

### GREEN — authoritative Apply synchronization

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests"
```

Result: exit code 0; 19 passed, 0 failed, 0 skipped; duration 689 ms. Production now routes the modal callback through the Settings mutation gate and existing `Commit`/`ProfileSaved` boundary. It updates runtime preferences from authoritative `LiveSettings`, refreshes the editable profile from the runtime result without discarding unrelated unsaved controls, and synchronizes dashboard state.

### RED — blocked shutdown cleanup ownership

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "Stop_WhenLightingTestUiCleanupIsBlocked"
```

Result: exit code 1; 0 passed, 1 failed, 0 skipped; duration 91 ms. `Stop_WhenLightingTestUiCleanupIsBlocked_RetainsOwnershipUntilCleanupCompletes` failed at `Assert.False(stop.IsCompleted)` because synchronous UI disposal returned, `AppHost.StopAsync` completed, and `AppHost.Ui` was cleared while the fake lighting cleanup remained blocked. A preceding malformed test-only patch produced compiler syntax noise and was repaired before this behavior-level RED; production remained unchanged until the failing assertion was observed.

### GREEN — blocked shutdown cleanup ownership

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "Stop_WhenLightingTestUiCleanupIsBlocked"
```

Result: exit code 0; 1 passed, 0 failed, 0 skipped; duration 75 ms. `IAppUiSession` now exposes asynchronous disposal. `AppHost` starts that disposal on the dispatcher, awaits its actual completion, and only then clears ownership. Production no longer has an ignored eight-second nested timeout: it awaits `PrepareCloseAsync`, calls `CloseAfterCleanup` only after that cleanup attempt completes, then releases the owned modal references and remaining UI resources.

### RED — outer application shutdown timeout

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "ShutdownPump_AfterObservationTimeout"
```

Result: exit code 1; no tests executed. The new finite delayed-cleanup test failed compilation with expected `CS0117` because `App.WaitWithDispatcherPumpUntilCompleted` did not exist. This establishes that `OnExit` had no policy for continuing after the first observation timeout. Two subsequent test-harness runs exposed an xUnit cancellation-token access on the STA thread and the fixture's missing WPF `SynchronizationContext`; both harness issues were corrected without changing production behavior further.

### GREEN — outer application shutdown timeout

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "ShutdownPump_AfterObservationTimeout"
```

Result: exit code 0; 1 passed, 0 failed, 0 skipped; duration 88 ms. `OnExit` now treats eight seconds as a dispatcher-pump observation interval rather than permission to abandon cleanup: it continues pumping until `AppHost.StopAsync` actually completes. The test uses 5 ms intervals and a 75 ms delayed cleanup, proving multiple elapsed intervals without making the suite wait eight seconds or hang.


### Final focused regression

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "LightingTestViewModelTests|LightingTestWindowTests|Stop_WhenLightingTestUiCleanupIsBlocked"
```

Result: exit code 0; 22 passed, 0 failed, 0 skipped; duration 950 ms. This includes deterministic concurrent/double Open/Close, blocked Begin, Apply coherence, blocked host cleanup, initial synchronous Closing cancellation, the production shutdown route through `LightingTestWindow.PrepareCloseAsync`, and continuation past an outer shutdown observation timeout. No production errors or warning noise remained.

### Full solution regression

Exact command:

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Result: exit code 0; 472 passed, 0 failed, 0 skipped:

- Core: 106 passed
- Infrastructure: 95 passed
- App: 210 passed
- Smoke: 19 passed
- Packaging: 42 passed

The five project results were interleaved by normal solution-level parallel execution. Restore was already current; no warnings, errors, or unexpected test noise appeared.

### Release build after fix

Exact command:

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Result: exit code 0; 0 warnings, 0 errors; elapsed time 00:00:00.86. Restore was already current.

### Fix design notes and self-review

- Completion and acquisition now share `_openGate`. Completion ignores caller cancellation only for the acquisition barrier, ensuring cleanup cannot be abandoned while Begin owns the gate; Apply still uses its supplied token for persistence.
- A session returned after Close was requested is disposed by Open before releasing the gate. Close then observes no owned session and cannot finish before that disposal. Tests prove one Begin/one disposal under concurrent double Open plus Close.
- The WPF shutdown path retains the modal window/ViewModel while cleanup is incomplete. `_isDisposing` prevents a blocked Begin continuation from showing the modal or foreground-error UI after shutdown has started.
- Removing the nested force-close timeout avoids dropping live ownership. Application exit continues to pump the dispatcher while awaiting `AppHost.StopAsync`; the host itself keeps the UI session referenced until its asynchronous disposal completes.
- Lighting-test Apply uses the same Settings mutation gate, runtime update, `LiveSettings`, editor refresh, and `ProfileSaved` synchronization boundary as authoritative settings changes. A later Settings Save therefore cannot restore the pre-Apply profile.
- `git diff --check` completed with exit code 0 and no whitespace errors before final solution verification.
- RU/EN resources were unchanged in this fix and the existing parity coverage passed. No Task 11 tray redesign, About, installer, or packaging behavior was added.
- Hardware concern remains unchanged: lifecycle and synchronization tests use deterministic fake lighting sessions; no physical Windows LampArray was available.
- The outer eight-second limit is now only an observation interval; each elapsed interval re-enters the dispatcher pump while keeping the host/UI ownership graph alive until cleanup completes.
