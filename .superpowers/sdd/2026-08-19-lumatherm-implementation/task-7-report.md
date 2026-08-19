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
