# Task 5 Report: LampArray Discovery, Control, and Write Throttling

## Status

Implemented Task 5 on `feature/lumatherm-v1` from base `feb6979`. Core remains BCL-only at `net8.0`; Infrastructure and its test host target exactly `net8.0-windows10.0.22621.0` with `TargetPlatformMinVersion` `10.0.22621.0`.

## Witnessed TDD Evidence

### RED

Tests were created before production lighting code. The first prescribed command was blocked during restore by sandbox denial of `C:\Users\User\AppData\Roaming\NuGet\NuGet.Config`; this was not accepted as TDD evidence. The valid RED was then witnessed using already-restored assets:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~Lighting"
```

Exit code: `1`. Expected feature-missing failures included:

- `CS0234`: `LumaTherm.Core.Lighting` does not exist.
- `CS0234`: `LumaTherm.Infrastructure.Lighting` does not exist.
- `CS0246`: `ILampArrayPlatform` and `ILampArrayHandle` could not be found.

After assigning Infrastructure the required Windows TFM, the portable Infrastructure test host correctly rejected its project reference. The test host was therefore updated to the same Windows TFM; Core and Core.Tests stayed portable.

### GREEN

After the minimal contracts, controller, platform adapter, and gate were implemented:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~Lighting"
```

Exit code: `0`. Results: Core Lighting `2/2`; Infrastructure Lighting `11/11`; total `13/13`. The real Windows adapter compiled as part of the focused run. Tests used only the complete in-memory `ILampArrayPlatform`/`ILampArrayHandle`; no test instantiated `WindowsLampArrayPlatform` or accessed physical lighting.

## Files and Implementation

- `src/LumaTherm.Core/Lighting/LightingDeviceInfo.cs`: stable discovery DTO.
- `src/LumaTherm.Core/Lighting/ILightingController.cs`: discovery, connection, color, release, event, and async-disposal contract.
- `src/LumaTherm.Core/Lighting/LightingCommandGate.cs`: deterministic duplicate suppression, literal 100 ms boundary behavior, and reset.
- `src/LumaTherm.Infrastructure/Lighting/ILampArrayPlatform.cs`: complete platform and handle seams, including platform lifetime ownership.
- `src/LumaTherm.Infrastructure/Lighting/LampArrayLightingController.cs`: lists every available device, selects exact saved ID then exact `GIGABYTE Device` name then VID/PID HID then first available, enables and routes color to the selected handle, disconnects on removal, disables before switching, and releases/disposes idempotently.
- `src/LumaTherm.Infrastructure/Lighting/WindowsLampArrayPlatform.cs`: owns a `DeviceWatcher`, forwards added/removed/updated discovery events, tracks removal availability safely across watcher callbacks, creates handles through `LampArray.FromIdAsync`, maps stable ID/name/count/availability, writes `Windows.UI.Color`, and stops/unsubscribes the watcher idempotently.
- `src/LumaTherm.Infrastructure/LumaTherm.Infrastructure.csproj`: exact Windows TFM and minimum platform version.
- `tests/LumaTherm.Infrastructure.Tests/LumaTherm.Infrastructure.Tests.csproj`: matching Windows test-host TFM required for the project reference.
- `tests/LumaTherm.Core.Tests/Lighting/LightingCommandGateTests.cs`: literal colors and timestamps for duplicate, 99 ms, 100 ms, and reset outcomes.
- `tests/LumaTherm.Infrastructure.Tests/Lighting/LampArrayLightingControllerTests.cs`: complete in-memory behavior tests for discovery, saved ID, name/HID/fallback priority, enable/color routing, no-device failure, removal/disconnect notification, disable-before-enable switching order, disconnected color failure, idempotent release, and idempotent disposal.

## Final Verification

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore
```

Exit code: `0`. Core `36/36`; Infrastructure `52/52`; total `88/88`, with no failures or skips.

```powershell
& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore
```

Exit code: `0`; warnings `0`; errors `0`. This separately compiled the real Windows LampArray platform at the exact target TFM.

## Self-review

- Checked each binding ruling against a behavior assertion and independently derived literal outcome.
- Mutation review: changing duplicate/timing boundaries, any selection tier, selected color target/value, removal clearing, switch order, release count, or disposal count breaks at least one test.
- Controller policy and Windows API responsibilities remain separated: controller knows selection/lifecycle policy; adapter alone references WinRT.
- Connection state is protected from watcher-thread callbacks, connection/write/release/disposal operations are serialized, and watcher subscription/stop are idempotent.
- `git diff --check` reports no whitespace errors (only the repository's expected LF-to-CRLF checkout notices for modified project files).

## Concerns

- Physical LampArray behavior was intentionally not exercised. Runtime validation against `VID_048D&PID_5702` remains a manual hardware step.
- `DevicesChanged` is forwarded on the DeviceWatcher callback thread; a future UI subscriber must marshal to its dispatcher, as is standard for infrastructure events.

## Fix Round 1/5 — Important/spec findings

### Root causes

1. `DiscoverAsync` projected only handles passing `IsAvailable`, losing stable unavailable IDs needed by settings.
2. `ConnectAsync` selected from one snapshot and published after `Enable` without rechecking availability or whether a device event invalidated that snapshot.
3. Windows enumeration wrote directly into a last-writer-wins cache, so an old asynchronous snapshot could overwrite a newer removal.
4. `DisposeAsync` performed handle disable before platform disposal without failure aggregation/finalization; a disable exception skipped watcher disposal after `_disposed` was already set.
5. `DiscoverAsync` did not participate in the controller operation gate, allowing platform disposal during its await; the Windows adapter had the same direct-call lifecycle gap.

### Witnessed RED

Controller tests were changed/added before controller production changes:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~LampArrayLightingControllerTests"
```

Exit code `1`: `5` intended failures, `10` passes. The failures independently reproduced unavailable discovery omission, disposal overtaking blocked discovery, blocked-enable removal returning `true`, synchronous remove/re-add returning `true`, and platform disposal count remaining `0` after a throwing handle disable.

The pure availability-state tests were then added before extracting the cache state:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~LampArrayAvailabilityStateTests"
```

Exit code `1`: expected `CS0246` because `LampArrayAvailabilityState` did not yet exist.

### GREEN and implementation

- Discovery now returns all platform device infos, including literal `IsAvailable = false`; connection selection still filters to available handles.
- Discovery, connection, write, release, and disposal share the controller operation gate and recheck disposal after awaits.
- Connection captures the device-event generation before discovery, enables without holding the state lock, and publishes only when both generation and availability remain valid. Stale/removed handles are disabled and return `false`.
- Controller disposal clears connection state first, always attempts platform disposal, preserves a single original exception, aggregates dual failures deterministically, and remains idempotent.
- New internal `LampArrayAvailabilityState` records watcher observations with generations and applies enumeration snapshots only when they are not older than the per-device entry. Tests cover both removal tombstone preservation and a genuinely newer reappearance.
- `WindowsLampArrayPlatform` uses that state and serializes direct `FindAllAsync`/`DisposeAsync` calls, with disposed rechecks after WinRT awaits. Physical LampArray was not instantiated by tests.

Focused final command:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~Lighting"
```

Exit code `0`: Core Lighting `2/2`; Infrastructure Lighting `17/17`; total `19/19`.

### Final verification

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore
```

Exit code `0`: Core `36/36`; Infrastructure `58/58`; total `94/94`, no failures or skips.

```powershell
& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore
```

Exit code `0`: warnings `0`, errors `0`; the real Windows adapter compiled at `net8.0-windows10.0.22621.0`.

### Self-review and concerns

- Mutation audit: reinstating discovery filtering, omitting either post-enable check, removing generation comparison, restoring last-writer cache behavior, skipping platform disposal after disable failure, or removing discovery serialization breaks a focused test.
- Controller policy, pure availability state, and WinRT adapter responsibilities remain separate. Core remains BCL-only.
- The two deferred minor findings were not changed.
- Remaining concern is unchanged: runtime HID behavior requires a deliberate manual hardware validation; automated tests never write physical lighting.

## Fix Round 2/5 — Bounded concurrency-test barriers

### Finding and test-only fix

The discovery concurrency test awaited `FindAllStarted` directly, and the blocked-enable test relied on the overall test-run cancellation token. Neither supplied a finite per-test deadline, so a regression that stopped either operation from reaching its barrier could hang the test process.

Only `tests/LumaTherm.Infrastructure.Tests/Lighting/LampArrayLightingControllerTests.cs` changed. Each concurrency test now owns a five-second `CancellationTokenSource`. That deadline is applied to:

- discovery barrier entry;
- blocked `FindAllAsync` continuation through the controller operation token;
- discovery operation completion;
- disposal completion;
- enable barrier entry;
- blocked enable continuation;
- connection operation completion.

The blocked-enable continuation remains in `finally`, so an assertion/cancellation path releases the worker while the worker's own wait is also independently bounded. No production file changed.

### Exact verification

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~LampArrayLightingControllerTests"
```

Exit code `0`: controller tests `15/15`.

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~Lighting"
```

Exit code `0`: Core Lighting `2/2`; Infrastructure Lighting `17/17`; total `19/19`.

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore
```

Exit code `0`: Core `36/36`; Infrastructure `58/58`; total `94/94`, no failures or skips.

```powershell
& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore
```

Exit code `0`: warnings `0`, errors `0`.

### Self-review and concerns

- Audited every wait in both barrier tests, including waits inside the in-memory fake callbacks, not only the test-method awaits.
- A missing barrier entry, non-returning controller operation, or non-returning disposal now fails within five seconds instead of hanging indefinitely.
- Production diff is empty for this round; physical-lighting safety and prior behavior are unchanged.
- Remaining concern remains the intentional manual hardware validation for the physical HID target.
