# Task 8 Report — Thermal Core dashboard

## Result

Implemented the approved native WPF Thermal Core dashboard, reusable theme, supplied-source logo resources, deterministic package/app assets, custom live temperature controls, responsive shell, Task 7 view-model bindings, and compiled STA runtime UI coverage.

Implementation commit: `f9c294f07673ed9952bb643833a02549febcaebc` (`feat: build Thermal Core dashboard`).

Task 9 Settings UI, production composition/runtime startup, tray behavior, and physical RGB writes were not started.

## TDD evidence

The first compiled runtime acceptance test was written before the production UI. Its intended RED was witnessed with `CS0234`: `LumaTherm.App.Controls` and `LumaTherm.App.Views` did not exist.

Subsequent RED/GREEN cycles caught these runtime-visible defects:

- the first full UI execution completed 9/13 cases and failed 4/13 because WPF selected a two-way default for inline `Run.Text`, which could not write to read-only `MainViewModel.SensorSource`; explicit `Mode=OneWay` made the projection green;
- the device-state and profile-stop tests failed 2/2 because the shell exposed no unavailable device indicator and profile colors were not rendered as hex; the shell now switches its dot between muted and success states, and profile colors use a dedicated hex converter;
- the logo runtime-resource test failed because the outer source circle was represented by an almost-closed stream path; it now uses the exact `EllipseGeometry` center/radius from the supplied SVG and the on-screen drawing shares the three supplied blade geometry resources.

The initial STA test harness also exposed a test-lifecycle issue: the self-hosted xUnit process remained alive and locked `LumaTherm.App.Tests.exe`. Evidence isolated the boundary after discovery/test launch. The collection fixture now has finite 10-second readiness/action/join guards, explicitly calls `Application.Shutdown`, completes its queue, joins the STA, and exits cleanly.

No test reads or searches XAML/C# source text. Tests instantiate compiled resources, views, shell, and controls on a deterministic STA and render controls through `RenderTargetBitmap`.

## Fresh verification

- Asset builder: PASS — `dotnet run --project tools\LumaTherm.AssetBuilder -- "$PWD"`.
- Focused compiled UI tests: PASS — 16 passed, 0 failed, 0 skipped.
- Full solution tests: PASS — 182 total, 0 failed, 0 skipped:
  - `LumaTherm.App.Tests`: 53 passed;
  - `LumaTherm.Core.Tests`: 71 passed;
  - `LumaTherm.Infrastructure.Tests`: 58 passed.
- Debug solution build: PASS — 7 projects built, 0 warnings, 0 errors.
- `git diff --check`: PASS; no whitespace errors. Git emitted only the repository's LF-to-CRLF working-copy notices.

## Generated assets and determinism

Final dimensions and SHA-256 hashes:

| Asset | Dimensions | SHA-256 |
|---|---:|---|
| `packaging/Assets/StoreLogo.png` | 50×50 | `243830D4498910D6740B8567B6B9BB4692E09F0423CB8EBB8473F9204D10B940` |
| `packaging/Assets/Square44x44Logo.png` | 44×44 | `952FBFD4BE7E566E23C4AC02D999F6DFEA0FE3CF1F08F62BD83EC6C06B00C7E2` |
| `packaging/Assets/Square150x150Logo.png` | 150×150 | `AC3C076773D321B12B1292654EFA3762A64104D326F23D32471269D73F2C3F56` |
| `packaging/Assets/Wide310x150Logo.png` | 310×150 | `0DB9C40921D50CA1A08DCBD51C4593010A4830F893BBA0AFCBE50C1D2B47922C` |
| `src/LumaTherm.App/Assets/LumaTherm.ico` | 44×44 + 256×256 | `17ADA513048C7CD0F9AFC0137C8B6AC481376A7CCD7915D45D8B6553D351B5AD` |

Hashing all five files, regenerating with the same source/SDK, and hashing again produced byte-for-byte matches for every output.

ICO inspection confirmed reserved `0`, type `1`, count `2`; the 44 px entry encodes width/height `44`, and the 256 px entry encodes both as `0`. Both entries have planes `1`, bit depth `32`, and valid payload lengths/offsets. WPF decoded exactly two frames at 44×44 and 256×256.

## Asset visual inspection

All four PNGs and both decoded ICO payloads were inspected locally. Results:

- transparent corners on every square/wide asset and both icon frames (corner alpha values all `0`);
- centered non-transparent bounds: 44 px center `21.5,21.5`; 150 px center `74.5,74.5`; wide center `154.5,74.5`; 256 px center `127.5,127.5`;
- supplied three-blade geometry is centered and not cropped;
- no visible resampling halo;
- 44 px output remains crisp and readable;
- the approved blue → amber → red gradient is visible on the ring and blades.

## Responsive, interaction, and accessibility behavior

- `MainWindow` is a resizable 1180×720 borderless `WindowChrome` shell with 960×620 minimum size, 76 px rail, 68 px title bar, and functional minimize/maximize/restore/close controls.
- Navigation, window, and about buttons use Segoe Fluent Icons; no emoji, ASCII icon stand-ins, or replacement geometry is used.
- Every icon-only shell button has a non-empty Russian automation name.
- The primary dashboard toggle resolves to the real Task 7 `ToggleModeCommand` and exposes `Включить или выключить термосинхронизацию`.
- The dashboard uses two columns at 1180 px and changes to one column below 980 px; persistent mode controls remain laid out inside the view and compact content scrolls vertically rather than clipping.
- A real `MainViewModel` fixture update changes visible temperature, current color, status copy, ring input, and history binding on the UI dispatcher.
- `TemperatureRing` renders cold/warm/hot and below/above-limit values while preserving the raw accessible reading.
- `ThermalGradientBar` renders three continuous bound stops, temperature labels, and a current-temperature marker.
- `TemperatureSparkline` detaches stale collections, tracks current collection changes, redraws additions, handles empty/one-point/flat histories, and exposes the approved screen-reader label.

## Self-review and remaining concern

Self-review checked all Task 8 scoped files against the exact tokens, supplied HTML geometry/copy, Fluent icon ruling, runtime test ruling, responsive breakpoint, accessibility names, deterministic asset contract, and no-hardware-write constraint. Compile-time WPF mismatches, stale STA lifetime, incorrect inline binding mode, unavailable device state, profile hex projection, and circle fidelity were found and corrected before the implementation commit.

No Task 8 blocker remains. Per controller instruction, this report does **not** claim final screenshot/source visual QA. The blocking source-versus-implementation comparison is intentionally deferred until the combined Dashboard + Settings flow exists after Task 9.
