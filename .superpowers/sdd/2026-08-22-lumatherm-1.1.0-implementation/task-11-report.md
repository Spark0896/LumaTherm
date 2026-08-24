# Task 11 — Typed, localized, configurable tray

## Outcome

Replaced the positional tray contract with `TrayCommandKind` and typed `TrayMenuEntry` values. The live menu now follows persisted tray flags, the current runtime temperature/mode snapshot, saved settings, and localization changes. `NotifyIconTrayPlatform` rebuilds a fresh `ContextMenuStrip` for every state, dispatches by command kind, and disposes replaced/current menus and handlers.

Production composition supplies the saved `SettingsViewModel.LiveSettings` source and owns one `TrayIconService`; service disposal removes runtime, localization, settings, and platform subscriptions exactly once. No window-activation dependency was introduced, and the minimize/deactivate regression keeps the loaded window ownership boundary alive until explicit exit.

The required RU/EN tray keys already existed with equal dictionary parity, so no resource file changes were necessary.

## Files changed

- `src/LumaTherm.App/Services/ITrayIconPlatform.cs`
- `src/LumaTherm.App/Services/NotifyIconTrayPlatform.cs`
- `src/LumaTherm.App/Services/TrayIconService.cs`
- `src/LumaTherm.App/Composition/ProductionAppServices.cs`
- `tests/LumaTherm.App.Tests/Services/TrayIconServiceTests.cs`
- `tests/LumaTherm.App.Tests/Ui/MainWindowLifecycleTests.cs`
- `.superpowers/sdd/2026-08-22-lumatherm-1.1.0-implementation/task-11-report.md`

## TDD evidence

All edits used repository-root `apply_patch` artifacts followed by `git apply --unidiff-zero --recount` because the Windows sandbox ACL helper could not open the assigned worktree.

### RED 1 — typed/localized menu state

Command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter TrayIconServiceTests
```

Exit code: `1`.

Expected failure: the compiler reported that `TrayCommandKind` did not exist, `TrayMenuEntry` had no three-argument typed constructor, and `BuildMenuState` had no localized three-argument overload. The initial test draft also produced `CS9113` for an unused STA fixture parameter; that test-only setup noise was removed before GREEN.

### GREEN 1 — menu state

Same command, exit code `0`: `19` passed, `0` failed, `0` skipped.

### RED 2 — live localization and typed routing

Same command, exit code `1`: `20` passed and `2` failed out of `22`.

Expected failures:

- saved/language refresh expected `Выход` but retained `Exit`;
- typed Open/Toggle commands were not subscribed and the deterministic wait timed out.

### GREEN 2 — live localization and typed routing

Same command, exit code `0`: `22` passed, `0` failed, `0` skipped.

### RED 3 — fresh WinForms menus and disposal

Same command, exit code `1` initially because the production platform lacked the injectable two-resource constructor needed to exercise a real `NotifyIcon`. After adding only that constructor, the behavioral RED ran: `22` passed and `1` failed out of `23`; expected typed order was `[Exit, ToggleMode, Open]`, while the positional implementation produced `[Open, ToggleMode, Exit]`.

### GREEN 3 — fresh WinForms menus and disposal

Same command, exit code `0`: `23` passed, `0` failed, `0` skipped.

### RED 4 — saved settings source

Same command, exit code `1` with `CS1729`: `TrayIconService` did not yet accept the saved-settings provider/change source required to refresh menu preferences and apply a saved language without waiting for another runtime snapshot.

### GREEN 4 — saved settings source

Same command, exit code `0`: `23` passed, `0` failed, `0` skipped.

### Ownership/subscription regression

Command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "TrayIconServiceTests|MainWindowLifecycleTests"
```

Final exit code: `0`: `25` passed, `0` failed, `0` skipped. An initial test-fixture attempt subclassed the XAML window and failed resource lookup before behavior ran; the corrected test uses the real `MainWindow` and invokes its protected deactivation hook reflectively.

## Final verification

### Focused

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter TrayIconServiceTests
```

Exit code: `0`. `23` passed, `0` failed, `0` skipped. Restore was already current; no warnings or other noise.

### Full solution tests

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit code: `0`. `480` passed, `0` failed, `0` skipped:

- Core: `106`
- Infrastructure: `95`
- App: `218`
- Smoke: `19`
- Packaging: `42`

Restore was already current. Output was localized in Russian but contained no warnings or errors.

### Release build

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit code: `0`. Build succeeded with `0` warnings and `0` errors; restore was already current.

### Diff hygiene

`git diff --check` exited `0` with no output.

## Self-review

- Typed entries and kind switches eliminate label parsing and positional command dispatch.
- Exit is appended enabled for every settings combination; all-hidden yields exactly Exit.
- Temperature is taken from each actual snapshot and uses `—` when absent.
- RU/EN labels, device/status text, and active/inactive toggle text flow through `ILocalizationService`; existing resource parity is covered by the full App test suite.
- Runtime snapshots, saved settings, and language events each rebuild from current state.
- Replaced and final WinForms menus/items detach their click handlers and are disposed.
- Service disposal removes all four external event sources once and disposes the platform once; later fake events produce no callbacks.
- Minimize/deactivate do not close the loaded window or cross the modeled lighting-ownership boundary; explicit exit does.
- No Task 12+, installer, update, About, or visual-pass scope was added.

## Concerns

None.
