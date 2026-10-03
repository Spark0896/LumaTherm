# Arctic redesign: verification record

Date: 2026-10-03. Scope: current Windows WPF source, all user-facing surfaces and
their function paths. The user selected the Arctic Minimal reference and explicitly
requested factory green instead of magenta. Impeccable 4.5.0 guided design and an
independent source/capture finish review.

## Result and evidence boundaries

The redesign and functional fixes are in source. A self-contained win-x64 build
was published locally with the pinned .NET SDK 8.0.423. The installed 1.1.0 payload
and GitHub release binaries were not replaced. The source-build screenshots must
not be interpreted as screenshots of the published 1.1.0 installer.

Final whole-suite status is recorded below after execution. Native observations
are separate from unit tests. Actual Windows reboot/login, physical LED visual
acceptance, real sleep/resume and every third-party RGB device were not tested.

## Function coverage

| Function | Verification and result |
| --- | --- |
| GPU monitoring/history | Actual NVIDIA GeForce RTX 5070 readings through NVML; live history and current color captured. Unit coverage includes missing/failing sensors, fallback and timestamp-based history projection. |
| Lighting discovery/selection | Actual available GIGABYTE LampArray identified; Refresh devices invoked. Automated discovery, unavailable/disconnected device, selection and runtime ownership paths remain covered. No universal device claim. |
| Thermal mode | Runtime engine, error recovery, saved mode and tray toggling covered automatically; live active mode shown in native captures; actual mode OFF/ON toggled successfully and original active state restored. |
| Profile/defaults | Factory 35°C #006BFF / 65°C #3CFF00 / 85°C #FF0000 asserted. Actual Add, numeric point crossing, Remove, Default colors and Save succeeded. Custom profiles remain valid. |
| Drag/keyboard editing | Real WPF STA tests exercise Mouse.Capture, marker movement and Delete followed by arrow editing with preserved adjacent focus. Synchronous collection observers validate sorted insertion. |
| Gradient/color mapping | All previews sample ColorEngine.Map. Tests cover anchors, HSV continuity, black/gray neighboring hue, smoothing independent of tick size and dense-label collision avoidance. |
| Color picker | Actual owned dark dialog captured in EN/RU; applying unchanged HEX succeeded. VM tests cover presets, RGB/HEX synchronization and invalid HEX rejection. |
| Numeric validation | Actual `200` and `abc` editor input blocked saving/test apply; loaded WPF binding tests cover invalid temperature and smoothing text, range errors and valid correction. A shared gate also blocks the sidebar Test route; native 200/abc were rejected there, and corrected 35 opened the test. |
| Lighting test | Actual slider at 65°C, Apply, Cancel and X succeeded. Tests cover draft-first initialization, edit synchronization, session restoration, apply failure/retry and closing while save is pending. Persistence failure was simulated in tests, not against the user's settings file. |
| Settings persistence/reset | Save, Default colors and language changes executed natively. Tests cover corruption/migration, draft isolation, reset, save rollback and startup serialization. Original system language and tray behavior were restored; requested green factory palette retained. |
| Tray behavior | Native minimize hid the window; left click restored it. Right-click menu stayed open for more than 2 seconds across 500 ms sensor updates. Native Exit terminated the process. Item-identity regression covers in-place menu updates. |
| Close/shutdown | Native normal close with tray disabled terminated the process; with tray enabled hiding and explicit exit were verified. Shutdown stops services while the WPF dispatcher is still available. |
| Windows startup | Native checkbox click immediately disabled/enabled Windows task (Disabled/Enabled), without Save; authoritative UI matched. `--autostart` launched hidden with active monitoring; tray click restored the window. User/policy refusal and activation refresh have automated coverage. Reboot was not performed. |
| Notifications/tray preferences | Existing automated coverage includes enablement, message suppression, menu visibility and explicit Exit availability; preview rendered in native Settings. No induced physical overtemperature event. |
| Language/navigation | Actual System/English/Russian changes, Dashboard/Settings/About/Test navigation, owned dialogs and localized automation names checked. EN and RU documentation use matching native captures. |
| About/update/repository | Actual manual update check returned “You are up to date.” Existing tests cover feed/launch failures, stable-version filtering, cancellation and manual-only networking. No automated download/installation added. |
| Packaging/security | Deterministic logo generation, exact PNG hashes/dimensions, sparse identity, signature/checksum/installer contracts and documentation link checks in Packaging tests. No new signed installer release created. |

## Verification commands

```powershell
./.dotnet/dotnet.exe test LumaTherm.sln -c Release -m:1 --no-restore -p:NuGetAudit=false --logger trx --results-directory artifacts/qa/final
./.dotnet/dotnet.exe publish src/LumaTherm.App -c Release -r win-x64 --self-contained true --no-restore -p:NuGetAudit=false -o artifacts/arctic-final
```

WPF interaction tests were run in the interactive Windows session. Local TRX files,
test logs, build outputs and the temporary settings backup stay under ignored
`artifacts/qa`; user settings/logs and signing material are not committed.

Final test execution: **628 passed, 0 failed, 0 skipped** — Core 126, Infrastructure 113, App 279, Smoke 19, Packaging 91. Release publish succeeded without warnings.

## Visual evidence

The [English](../../README.md) and [Russian](../../README.ru.md) READMEs each show
Dashboard, Settings, Lighting test, color picker and About from the actual app.
The generic screenshot paths now contain English captures. Native PrintWindow
captures preserve application pixels without desktop-window overlap. Raster
metadata records their origin; deterministic vector logo assets embed their origin
in the asset builder. Provenance scan: 22 rasters, none missing, including the minimum-size QA packet.

The [minimum-size Russian QA captures](../screenshots/qa/) are retained alongside the language-matched documentation images. Minimum-size Russian checks: main window 920×620, test window 700×580. Settings
and Test scroll to the final editors/application preferences while their action
footer remains visible. Dense 64/65/66°C labels are covered by a collision test at
240 DIP width. These are native DIP layouts, not browser viewport tests.

The independent review's initial F1/F2 findings (numeric binding errors bypassing
save; arbitrary-point overview/overlapping labels) and Delete focus finding were
addressed with regression tests. Rendered primary-button contrast was also fixed
and asserted using the actual TextBlock foreground. See [finish review](arctic-review.md).
