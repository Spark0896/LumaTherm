# Task 9 Report — Thermal Core Settings UI

## Status

Task 9 is implemented and committed on `feature/lumatherm-v1`.

- Reviewed base: `b03b541`
- Feature commit: `70766d3` (`feat: add thermal profile settings UI`)
- No physical lighting writes, real autostart changes, vendor-app operations, or hardware probes were performed.

## Delivered behavior

- Added the compiled `SettingsView` with the approved 57/43 two-column hierarchy, stacked narrow layout, three color cards, three temperature slider/text editors, smoothing editor, three application switches, direct LampArray/GPU rows, conditional device selector, tray preview, Reset, and the sole cyan primary Save action.
- Replaced the settings placeholder with real Dashboard/Settings command navigation while preserving the 76 px rail, 68 px title bar, dashboard view, shell metrics, and Task 8 workspace gradient.
- Added native `ColorPickerService`: current RGB is preselected; `AllowFullOpen` and `FullOpen` are enabled; the active/main WPF HWND is supplied through `IWin32Window` when available; the dialog is deterministically disposed; cancel returns `null`.
- Extended `SettingsViewModel` with async picker commands, hex projections, direct LampArray discovery/selection states, non-fatal hardware status, and stable preferred-device persistence through `IThermalRuntime.UpdateSettingsAsync`.
- Production lighting wording and dependencies remain Windows LampArray-only. No GCC/RGB Fusion query, launch, modification, requirement, raw HID write, or closed vendor DLL was added. GPU text identifies NVML primary and MSI Afterburner optional fallback.

## Witnessed RED → GREEN ledger

The paused work already recorded the initial missing-`IColorPickerService` RED and cold-picker GREEN 1/1. Resumption first established a green picker/device baseline of 4/4 without discarding that work.

| Behavior | Witnessed RED | GREEN evidence |
|---|---|---|
| Zero LampArray devices | `LightingHardwareStatus` missing at compile time | 1/1 passed with non-fatal direct-Windows status |
| One available device | expected stable id `lamp-a`, actual `null` | 1/1 passed; compact selector and direct status |
| Saved unavailable id with another available device | selector was hidden | 1/1 passed; unavailable placeholder remains selected |
| Discovery failure | status remained “not checked” | 1/1 passed; editable/live state preserved and validation untouched |
| Compiled Settings view | `SettingsView` type missing | 1/1 passed with real compiled controls and resolved bindings |
| Culture-safe numeric editing | expected `ru-RU`, actual `en-US` | 1/1 passed; exact ranges and LostFocus binding also verified |
| Real shell navigation | settings data context and navigation commands missing | 1/1 passed; compiled content and selected automation state switch |
| Narrow layout | 979 px did not enter compact mode | 2/2 passed at the 979/980 boundary with editor/action bounds |
| Owned native picker | `ColorPickerService` missing | 1/1 passed; visible WPF owner produced a non-zero HWND |
| Saved unavailable id with zero discovery results | selector hid the retained id | 1/1 passed; the unavailable choice remains visible |
| Choice after unavailable saved id | stale unavailable item/status remained | 1/1 passed; selecting an available device removes stale state |

Picker cancel and compiled validation/accessibility coverage were added and passed 2/2. Compiled unavailable-device selector rendering passed 1/1.

## Persistence and external-call ordering

`IThermalRuntime.UpdateSettingsAsync` remains the only settings persistence/live-publication authority. `SettingsViewModel` does not depend on or call `ISettingsStore`.

- Ordinary valid Save: `runtime.persist` → `vm.commit`; persistence count exactly 1.
- Changed autostart: `startup:true` → `runtime.persist` → `vm.commit`.
- Runtime rejection after changed autostart: `startup:true` → `runtime.fail` → compensating `startup:false`; live settings/dashboard profile remain last-good.
- Startup rejection: `startup:true` only; runtime is not invoked and live settings remain last-good.
- Runtime plus compensation failure: the same three attempted calls occur and a user-safe rollback warning is surfaced.
- Selected color: only the matching editable color changes, then the same validated Save path invokes runtime exactly once with that candidate.
- Picker cancel: no editable change, runtime call, persistence, or startup action.
- Invalid crossed thresholds: no runtime/startup side effect; `LiveSettings` and the synchronized dashboard profile remain unchanged.
- Reset: edits defaults only; runtime count remains zero until explicit Save, after which runtime is invoked once.

## LampArray device-selection states

- Zero devices, no saved id: empty list, selector collapsed, “Windows LampArray not found” non-fatal status.
- One available device: automatically selected, compact selector collapsed, direct Windows LampArray connected status.
- Multiple available devices: selector visible; saved stable id is selected and a new selection is included in the runtime candidate.
- Saved unavailable id: synthetic unavailable entry remains visible and selected even when discovery returns zero devices.
- User chooses an available replacement: stale synthetic entry is removed; name/status switch to the direct LampArray device; selector collapses if only one available device remains.
- Discovery exception: user-safe occupied/unavailable status; editable settings, live settings, and validation state are preserved.

## Compiled UI, accessibility, and responsive checks

- Instantiated compiled WPF controls on the deterministic STA fixture; required command/value/validation/device bindings resolve against a real `SettingsViewModel` fixture.
- Temperature sliders use exactly `0..120`; smoothing uses exactly `0.1..5.0`; numeric text uses `UpdateSourceTrigger=LostFocus` and inherited `ru-RU` numeric culture.
- Color actions are real Buttons with label, swatch, hex, Russian automation name, and async command.
- Every slider, numeric editor, application toggle, selector, Reset, and Save has a Russian automation name and remains keyboard focusable.
- Validation renders above the editor in `#FF858B`, binds its accessible name to the user-safe Russian message, and uses assertive WPF live-region metadata.
- Dashboard/Settings buttons are command-bound and keyboard reachable; their `AutomationProperties.ItemStatus` moves with the visible compiled content.
- At 979 px the columns stack inside a vertical scroller; at 980 px the two-column layout remains active. Temperature editors and Save/Reset retain positive bounds without horizontal clipping.

## Fresh verification before feature commit

```text
Focused Settings|Ui: 63 passed, 0 failed, 0 skipped
Full solution: Core 71 + Infrastructure 58 + App 85 = 214 passed, 0 failed, 0 skipped
Debug build: succeeded, 0 warnings, 0 errors
git diff --check: clean (only Git LF→CRLF working-copy notices)
```

Self-review also searched the Task 9 App/test scope for `async void`, `ISettingsStore`, GCC/RGB Fusion/Control Center dependencies, raw-HID entry points, and closed-feature calls; no matches were present.

## Remaining concerns

- Automated build/layout tests do not constitute the final rendered visual comparison. The controller should run the planned two-screen rendered comparison gate against `thermal-core-final.html`.
- Task 11 composition must supply `ColorPickerService`, `LightingDeviceDiscoveryService`, and `MainWindow.SettingsDataContext`, and choose when to execute the discovery command. Task 9 deliberately does not take composition-root ownership.
- The native dialog is not opened interactively in automation; its owner-handle seam is tested, while the real `ColorDialog` configuration/disposal path is compiled and self-reviewed.

## Fix round 1/5 — review blockers

- Fix commit: `a77d958` (`fix: harden settings mutation flow`)
- The evidence and call ordering below supersede the original pre-fix persistence-order notes above.

### Blocker 1 — runtime is the validation authority

Covering test: `Save_InvalidCandidate_IsRejectedByRuntimeBeforeAnyStartupSideEffect` with a fake that mirrors production `ThermalRuntime.UpdateSettingsAsync` by calling `settings.Validate()` at its boundary before recording persistence.

```text
RED command: dotnet test LumaTherm.App.Tests.csproj --filter FullyQualifiedName~Save_InvalidCandidate_IsRejectedByRuntimeBeforeAnyStartupSideEffect
RED output: 0 passed, 1 failed; expected runtime UpdateCalls 1, actual 0 (ViewModel rejected first).
GREEN output: 1 passed, 0 failed.
```

`SettingsViewModel` no longer calls `candidate.Validate()`. It sends the candidate to `IThermalRuntime.UpdateSettingsAsync`, translates a runtime `ArgumentException` to safe Russian validation, and leaves startup, `LiveSettings`, and the synchronized dashboard profile unchanged when the runtime rejects precommit.

Revised autostart ordering:

- Valid change: `runtime.persist(candidate)` → `startup:true/false` → `vm.commit`.
- Runtime failure or invalid candidate: `runtime.fail/reject` only; startup is not touched.
- Startup failure after runtime success: `runtime.persist(candidate)` → `startup.fail` → `runtime.persist(last-good)`; ViewModel live state remains last-good.
- If runtime compensation also fails, the candidate may remain runtime-persisted while ViewModel live state remains last-good; the UI reports `Не удалось изменить автозапуск. Не удалось вернуть настройки.` This unavoidable double-failure is covered and remains a surfaced recovery concern rather than being hidden.

### Blocker 2 — one serialized settings-mutation pipeline

Covering test: `SaveAndPicker_ShareOneSerializedMutationPipelineWithoutOverlappingPersistence`.

```text
RED command: dotnet test LumaTherm.App.Tests.csproj --filter FullyQualifiedName~SaveAndPicker_ShareOneSerializedMutationPipelineWithoutOverlappingPersistence
RED output: 0 passed, 1 failed; expected picker calls 0 while Save was held, actual 1.
GREEN output: 1 passed, 0 failed.
```

Save and all three picker commands now enter one shared `SemaphoreSlim`-serialized pipeline. The test holds Save inside the runtime, starts a picker, and proves the picker remains outside until release, maximum concurrent runtime updates is 1, two distinct candidates persist exactly once each in deterministic order, and the final `LiveSettings` equals the picked candidate. Same-command reentry remains finite through `AsyncRelayCommand`; exception paths release the shared gate in `finally`. Runtime-first startup compensation remains covered by the startup-failure tests.

### Blocker 3 — compiled Thermal Core control templates

Covering test: `SettingsView_UsesThermalTemplatesWithCompiledInteractionStates`.

```text
RED command: dotnet test LumaTherm.App.Tests.csproj --filter FullyQualifiedName~SettingsView_UsesThermalTemplatesWithCompiledInteractionStates
RED output: 0 passed, 1 failed; named ThermalSliderTrack was null under the default WPF template.
GREEN output: 1 passed, 0 failed.
```

The compiled Settings view now supplies Thermal Core templates for sliders, tiny application switches, numeric editors, color cards, Reset, and Save. Runtime assertions verify dark `#363D43` tracks, cyan fill/thumb/switch states, 4/6/8/9 px radii, dark editor/reset surfaces, cyan Save surface, real `PART_Track` min/max/value synchronization, and hover/pressed/keyboard-focus/disabled triggers. No emoji or substitute icon was introduced. Final screenshot comparison remains controller-owned.

### Fresh verification after all fix-round changes

```text
Focused Settings|Ui: 66 passed, 0 failed, 0 skipped
Full solution: Core 71 + Infrastructure 58 + App 88 = 217 passed, 0 failed, 0 skipped
Debug build: succeeded, 0 warnings, 0 errors
git diff --check: clean (only Git LF→CRLF working-copy notices)
```

The deferred minor binding/LostFocus review finding was not folded into this blocker round.
