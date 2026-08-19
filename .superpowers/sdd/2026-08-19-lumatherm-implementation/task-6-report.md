# Task 6 Report: Thermal Runtime, Recovery, and Power-Safe State Machine

## Status and implementation

Implemented the BCL-only Core runtime contracts and orchestration state machine:

- `RuntimeStatus`, `RuntimeSnapshot`, and `IThermalRuntime`, including nullable `ThermalRange` projection from the active `ColorEngine`.
- A deterministic `ProcessOnceAsync` 100 ms render tick with independent 500 ms healthy sampling, held-target smoothing, 10 Hz/duplicate lighting gating, lighting reconnect/write recovery, and snapshots raised outside runtime gates.
- Missing-source freeze plus exact retries after 1 s, 2 s, 5 s, 10 s, and 15 s repeatedly; a single safety-release attempt exactly five seconds after the first miss; recovery resets retry state and resumes the held smoothing engine.
- Persist-before-observe mode/settings changes, serialized start/disable/suspend/resume/stop, idempotent stop/disposal, resource-disposal failure aggregation, and committed shutdown that cannot be abandoned by late cancellation.
- A `PeriodicTimer` background loop using the injected `TimeProvider`, with self-stop handling for synchronous event reentrancy and deferred cancellation-source disposal to avoid loop self-awaits.

Files:

- `src/LumaTherm.Core/Runtime/RuntimeStatus.cs`
- `src/LumaTherm.Core/Runtime/RuntimeSnapshot.cs`
- `src/LumaTherm.Core/Runtime/IThermalRuntime.cs`
- `src/LumaTherm.Core/Runtime/ThermalRuntime.cs`
- `tests/LumaTherm.Core.Tests/Runtime/ThermalRuntimeTests.cs`

## Witnessed RED to GREEN evidence

### Initial runtime contract and state machine

RED command:

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ThermalRuntimeTests
```

Expected compile failure was observed before production runtime files existed: `CS0234` for missing `LumaTherm.Core.Runtime` and `CS0246` for missing `ThermalRuntime`.

After the minimal contracts/state machine, the same command passed `9/9`. Later deterministic lifecycle and failure-boundary cycles expanded the focused suite to `17/17`.

### Settings mode transition and repeated disposal

RED: the two-test focused command failed `2/2`. `UpdateSettings_WhenItDisablesMode_PersistsBeforeRelease` expected one release and observed zero; `DisposeAsync_CanBeCalledTwiceWithoutRepeatingCleanup` threw `ObjectDisposedException` from the disposed lifecycle semaphore.

GREEN: the same two-test command passed `2/2` after applying the full-settings mode transition and making cleanup idempotent.

### Shutdown cancellation race

RED: `Stop_CancellationAfterShutdownBegins_DoesNotAbandonCleanup` failed with `OperationCanceledException` at the process-gate wait after `_stopped` had already transitioned.

GREEN: the focused test passed `1/1` after making post-transition shutdown/release cleanup non-cancelable while keeping initial gate acquisition cancellable.

### Background event self-stop

RED: `SnapshotChanged_FromBackgroundLoop_CanStopThatLoopWithoutSelfDeadlock` reproduced a loop awaiting itself; the focused command remained hung beyond 30 seconds and its owned test processes were terminated. The regression was then bounded by running the virtual-clock callback on a task with a five-second linked test deadline.

GREEN: the bounded focused command passed `1/1` in 33 ms after loop-context-aware stop handling.

### Failed five-second safety release

RED: `MissingReading_WhenSafetyReleaseFails_DoesNotCrashOrRepeatRelease` failed with the fake controller's `InvalidOperationException: release failed` propagating from `ProcessOnceAsync`.

GREEN: the same test passed `1/1`; monitoring now reports `SensorUnavailable`, records the release failure message, and does not repeat the safety-release attempt.

## Deterministic behavior coverage

- Disabled mode performs zero reads/writes.
- A one-second cold-to-hot ramp produces 5–10 distinct gradual writes, never jumps directly to the literal hot red, polls at most every 500 ms, and stays within ten writes per second.
- Duplicate colors produce one write across eleven render ticks.
- Literal provider read times are `0, 500, 1500, 3500, 8500, 18500, 33500 ms`, proving 1/2/5/10/15/15-second missing-source delays.
- The color remains frozen with no intervening writes, release happens once at five seconds even while the next provider retry is later, and recovery resets the next delay to one second.
- Connection and write failures recover on the next render tick without changing the sensor schedule.
- Virtual-time timer tests prove double start creates one loop, suspend removes it, resume creates one loop only when enabled, and stop leaves zero timers/reads.
- Tests cover persist-before-release/snapshot ordering, idempotent lifecycle/disposal, late-cancellation cleanup, direct event reentrancy, and background-loop self-stop.

## Final verification

Commands:

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ThermalRuntimeTests
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore
& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore
git diff --check
```

Final expected evidence is recorded immediately before commit: focused runtime `17/17`; full Core `53/53` and Infrastructure `58/58` (`111/111` total); build warnings `0`, errors `0`; whitespace check clean.

## Self-review

- Re-read every Task 6 ruling against an observable test; no wall-clock sleeps are used. The only real-time values are finite five-second failure deadlines around concurrency barriers.
- Core project references remain unchanged and BCL-only.
- Lifecycle operations share one semaphore; render processing has a separate semaphore so loop cancellation/await never waits on its own lifecycle gate.
- Snapshots are stored and raised only after render/lifecycle gates are released. Background event self-stop is explicitly covered.
- Mutation audit: wrong retry delay, repeated/missing release, smoothing jump, over-polling, over-writing, missing persistence ordering, abandoned disposal, leaked timer, or in-gate event delivery breaks at least one focused test.

## Concerns

No code-level concerns remain. Physical LampArray and live telemetry integration are intentionally outside this Core fake-clock test boundary and remain hardware validation work.
