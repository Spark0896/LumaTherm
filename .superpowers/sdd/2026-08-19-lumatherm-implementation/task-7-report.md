# Task 7 report — Testable Application ViewModels and Live History

## RED → GREEN evidence

1. Added the App test project plus ViewModel tests before any application production code. The required initial command was run:

   ```powershell
   & "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels
   ```

   It failed as expected because `src/LumaTherm.App/LumaTherm.App.csproj` did not exist. The environment also could not reach NuGet's vulnerability endpoint, so its `NU1900` audit warning was promoted to an error. Subsequent restore/test commands use `-p:NuGetAudit=false`; packages were already available locally.

2. Implemented the smallest App project, Core startup contract, MVVM primitives, MainViewModel, SettingsViewModel, and test fakes. Initial GREEN:

   ```powershell
   & "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels --no-restore -p:NuGetAudit=false
   ```

   Output: `18 passed`.

3. Added the profile synchronization contract before its API. RED showed `MainViewModel` lacked `SynchronizeProfile`; after implementation the focused test passed.

4. During self-review, added two behavior assertions before their fixes. RED output showed Active snapshots did not set `IsModeEnabled` and queued profile updates could survive disposal. GREEN after the minimal fixes: `20 passed`.

## Delivered files

- `src/LumaTherm.Core/System/IStartupService.cs`
- `src/LumaTherm.App/LumaTherm.App.csproj`, `Program.cs`
- `src/LumaTherm.App/ViewModels/{ObservableObject,RelayCommand,MainViewModel,SettingsViewModel}.cs`
- `tests/LumaTherm.App.Tests/LumaTherm.App.Tests.csproj`
- `tests/LumaTherm.App.Tests/ViewModels/{MainViewModelTests,SettingsViewModelTests}.cs`
- `LumaTherm.sln`

## Final verification

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels --no-restore -p:NuGetAudit=false
# 20 passed, 0 failed

& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore -p:NuGetAudit=false
# Core: 61 passed; Infrastructure: 58 passed; App: 20 passed

& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore -p:NuGetAudit=false
# Build succeeded, 0 warnings, 0 errors
```

Fix round 2 implementation commit: `7f79a1d` (`fix: reconcile view models with runtime commits`).

`git diff --check` also passed.

## Self-review

- Runtime notifications use the injected `SynchronizationContext`; tests remain deterministic with no context.
- History deduplicates by real reading timestamp and trims only the oldest entries beyond 120.
- Settings validates before any side effect; startup runs only when changed and a startup failure leaves runtime/store/live settings untouched.
- Async commands contain exceptions and prevent reentry; disposal removes runtime/settings subscriptions and ignores already queued callbacks.

## Concern

The first restore cannot access NuGet's audit feed in this environment. This is environmental only; all final commands completed warning-free with audit disabled.

## Fix round 1 — reviewer blockers

### RED → GREEN evidence

The following test-first changes were made against commit `c147671`:

1. Added direct `RuntimeSnapshot.Range` projection, suspended-state authority, runtime-outcome toggle, queued snapshot disposal, single-persistence/rollback, shared ordering recorder, SaveCommand reentry, and post-dispose profile-wiring tests. The focused RED command failed as expected with missing `MainViewModel.CurrentRange` and the obsolete SettingsViewModel constructor that still required `ISettingsStore`.

   ```powershell
   & "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels --no-restore -p:NuGetAudit=false
   ```

2. Implemented direct nullable range projection; preserved the last known enabled state for `Suspended`; used `CurrentSnapshot` after a toggle instead of assigning the requested value; removed VM-level `ISettingsStore` persistence; and added startup compensation when the runtime persistence owner fails. GREEN: `27 passed`.

3. Self-review added the missing `ArgumentException` runtime-failure rollback case. Its focused RED showed the rollback call was missing (`startup:true`, `runtime.fail`, expected `startup:false`); removing the post-side-effect validation catch produced GREEN.

### Final verification

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels --no-restore -p:NuGetAudit=false
# 28 passed, 0 failed

& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore -p:NuGetAudit=false
# Core: 61 passed; Infrastructure: 58 passed; App: 28 passed

& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore -p:NuGetAudit=false
# Build succeeded, 0 warnings, 0 errors
```

### Self-review

- `CurrentRange` uses the runtime-provided classification without recomputing it.
- Suspended snapshots retain the explicit saved/requested mode; a differing runtime result wins a completed toggle.
- `ThermalRuntime.UpdateSettingsAsync` is the sole persistence owner. Shared-recorder tests prove normal ordering and exactly one persistence.
- Any post-startup runtime failure compensates autostart and leaves live settings/profile events unchanged; rollback failure is visible and user-safe.
- `SaveCommand` exposes/recovers execution state and contains unexpected errors; queued callbacks and profile synchronization are ignored after disposal.

Fix implementation commit: `ec48693` (`fix: harden view model state and settings saves`).

## Fix round 2 — authoritative runtime state

### RED → GREEN evidence

New Core/App tests first failed to compile because `RuntimeSnapshot` lacked `IsModeEnabled` and `ThermalRuntime` lacked `CurrentSettings`. After adding the additive snapshot bit, committed settings authority, observer isolation, and ViewModel reconciliation, focused Core and App tests passed.

An existing Core background-loop test then correctly failed because its old expectation was that a throwing subscriber produced a faulted snapshot. It was rewritten to assert the new contract: a throwing subscriber does not fault or stop the loop. GREEN focused counts: Core Runtime `25 passed`; App ViewModels `35 passed`.

### Contract changes and self-review

- Every production snapshot carries `_settings.IsModeEnabled`; MainViewModel binds this bit directly, including initial and suspended states.
- `CurrentSettings` changes immediately after durable save, before post-save work. A committed runtime update that later throws is reconciled as committed by SettingsViewModel without autostart rollback.
- Snapshot and profile observers are isolated. Committed settings remain committed and expose the precise Russian post-save warning rather than a false save-failure message.
- Tests cover suspended enabled/disabled and settings changes, opposite toggle outcome, post-commit throwing runtime, startup initial failure, shared state/order behavior, queued disposal, and direct production subscriber isolation.

### Final verification

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --filter FullyQualifiedName~Runtime --no-restore -p:NuGetAudit=false
# 25 passed

& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels --no-restore -p:NuGetAudit=false
# 35 passed

& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore -p:NuGetAudit=false
# Core: 63 passed; Infrastructure: 58 passed; App: 35 passed

& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore -p:NuGetAudit=false
# Build succeeded, 0 warnings, 0 errors
```
