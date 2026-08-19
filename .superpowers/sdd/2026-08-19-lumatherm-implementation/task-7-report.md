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
