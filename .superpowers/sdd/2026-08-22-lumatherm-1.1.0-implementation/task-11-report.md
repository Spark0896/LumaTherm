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

## Fix Round 1 — Important findings

### Outcome

Replaced the window-event-only ownership assertion with a production-facing lifecycle regression. It uses the real `ProductionAppServices.WpfUiSession`, `MainWindow`, `TrayIconService`, `ThermalRuntime`, and `LampArrayLightingController` over deterministic hardware seams. Ordinary deactivation and minimize-to-tray leave the runtime and LampArray ownership untouched; explicit tray/application shutdown stops the runtime, disables the owned handle, and disposes the LampArray platform.

`TrayIconService` construction is now transactional across event subscriptions, initial menu application, and visibility. Resource lookup, menu application, or visibility failure detaches every publisher and disposes the owned platform exactly once while preserving the original exception. Production composition constructs all other adapters/delegates first, creates the explicitly named `ownedPlatform` last, and transfers it immediately to the exception-safe service constructor.

### TDD evidence — constructor initialization rollback

RED command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter TrayIconServiceTests
```

Exit code: `1`. `23` passed and `3` failed out of `26`. Each literal failure injection (localization resource lookup, platform menu application, and platform visibility) observed `3` retained platform subscribers where `0` were required; the owned platform was also not disposed.

GREEN command: the same command.

Exit code: `0`. `26` passed, `0` failed, `0` skipped. Restore was current and there were no warnings.

### TDD evidence — real lifecycle ownership boundary

RED command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter MainWindowLifecycleTests
```

Exit code: `1`. The compiler reported `CS0122` because the concrete production `ProductionAppServices.WpfUiSession` was private, so the test could not exercise the real production session boundary. The follow-on member-access diagnostics were consequences of the same inaccessible type.

Before that valid RED, one ACL-workaround patch did not apply and an unchanged two-test baseline passed; it was discarded as evidence. The first applied test scaffold also had a misplaced test-class brace and unused test-double event warnings; those test-only errors were corrected before the valid `CS0122` RED above.

GREEN command: the same command.

Exit code: `0`. `2` passed, `0` failed, `0` skipped. The production session was exposed only as `internal` to the existing friend test assembly; lifecycle behavior itself remained unchanged.

### Refactor/focused verification

Command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "TrayIconServiceTests|MainWindowLifecycleTests"
```

Exit code: `0`. `28` passed, `0` failed, `0` skipped. Restore was current and there were no warnings.

The lifecycle regression separately observes the normal visible/deactivated state and the hidden/minimized state. At both boundaries it asserts zero runtime `StopAsync`/`DisposeAsync` calls, zero LampArray handle disables, and zero LampArray platform disposals. After typed Exit it observes one runtime stop, one handle disable, and one platform disposal; final session teardown invokes runtime disposal once.

### Full solution verification

Command:

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit code: `0`. `483` passed, `0` failed, `0` skipped:

- Core: `106`
- Infrastructure: `95`
- App: `221`
- Smoke: `19`
- Packaging: `42`

Restore was current. Output was localized in Russian and contained no warnings or errors.

### Release build verification

Command:

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit code: `0`. Build succeeded with `0` warnings and `0` errors; restore was current.

### Fix-round self-review

- Failure injection covers the three later initialization boundaries supported without test-only production hooks: localized menu construction, platform menu assignment, and `Visible = true`.
- Constructor rollback reuses the same subscription-detach and exactly-once platform-disposal paths as normal disposal.
- The original initialization exception remains the thrown exception; cleanup reporting stays contained by the existing error sink.
- Production creates the owned platform only after the other tray dependencies and delegates, leaving the exception-safe service constructor as the sole ownership-transfer boundary.
- The lifecycle test crosses real WPF window and real runtime/LampArray controller code; fakes are limited to OS/hardware edges and observable call counters.
- Typed routing, dispatcher/serialized operation behavior, and Task 12+ scope remain unchanged.
- `git diff --check` exited `0` with no output before report append.

### Fix-round concerns

None. The previously ledgered minor finding about a throwing real-platform `Dispose` remains intentionally outside this fix round.
