# Task 13 implementation report

## Scope and outcome

Implemented the final bilingual and visual-consistency pass from base `966083ee281142934dedd1e5eb81cd0741119537`. The change stays within Task 13: application localization, existing-window live reprojection, stable user-facing errors, narrow visual/accessibility consistency, and their tests. Packaging behavior and Task 14+ artifacts were not changed.

## TDD evidence

### RED

After adding the behavior/runtime tests and before production edits:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false
```

Exit code: `1`. Compilation failed only at the deliberately required localization injection points: `MainViewModel` lacked the named `localization` argument, `SettingsViewModel` lacked the six-argument overload, and `LightingTestViewModel` lacked the fourth localization argument. No tests executed, establishing the intended RED boundary before production changes.

During GREEN, the first broad focused run compiled and reported 39 passed / 75 failed / 114 total. The failures shared one diagnostic cause: fallback localization attempted `ResourceDictionary.Source` loading in parallel non-STA unit hosts. The supported `Application.LoadComponent` resource path plus synchronized compiled-resource loading fixed that test-host/runtime compatibility issue. A later full App run exposed nine expected stale assertions/concurrent resource-load failures (254 passed / 9 failed / 263 total), followed by one stale resource-key assertion (262 passed / 1 failed / 263 total); both were corrected before final verification.

### GREEN and refactor verification

Focused Task 13 command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "FullyQualifiedName~Task13LocalizationRuntimeTests|FullyQualifiedName~LocalizationServiceTests|FullyQualifiedName~MainViewModelTests|FullyQualifiedName~LightingTestViewModelTests|FullyQualifiedName~TrayIconServiceTests"
```

Exit code: `0`. Passed 78, failed 0, skipped 0.

Full App command:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false
```

Exit code: `0`. Passed 263, failed 0, skipped 0.

Full solution command:

```powershell
& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit code: `0`. Passed 558 total: Core 123, Infrastructure 111, App 263, Smoke 19, Packaging 42; failed 0, skipped 0.

Release build:

```powershell
& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false
```

Exit code: `0`. Warnings 0, errors 0.

Expected environmental noise was limited to Russian-localized .NET/VSTest progress output and parallel interleaving of project results. Restore reported all projects up to date. NuGet audit was explicitly disabled as required.

## Runtime localization and error behavior

- Compiled WPF runtime tests apply RU, switch the existing shell/Dashboard/Settings/About instances to EN, then back to RU without recreating them. Navigation names/item status, window accessibility labels, validation messages, headings, and view text update in place.
- Main, Settings, Lighting Test, About, tray notifications, recovery/foreground/background errors, and computed Dashboard behavior values now obtain UI strings through `ILocalizationService.Get`; XAML surfaces use `DynamicResource`.
- Localized computed properties subscribe to `LanguageChanged`, reproject cached semantic state, raise the relevant property changes, and unsubscribe on disposal.
- Raw exceptions are no longer projected to users by the touched lighting/settings/composition paths. Stable localized messages are shown while original exceptions continue through the existing propagation/logging paths.
- English compiled-surface traversal covers visible text and automation names and found no Cyrillic. The final source audit also found zero Cyrillic literals in App C#/XAML outside `Strings.ru-RU.xaml`.
- RU/EN dictionary parity remains behaviorally covered by the existing localization resource suite.

## Visual decisions and runtime evidence

- Preserved the dark panel hierarchy, cyan focus/selection treatment, saturated thermal gradient, and `960 x 620` minimum.
- Retained `SectionHeadingStyle.Foreground={StaticResource PrimaryTextBrush}`; runtime contrast assertions verify heading and status text at or above 4.5:1 against the dark panel.
- Added wrapping/auto sizing to dynamic headings and labels and a compiled-layout assertion at the minimum client area. WPF uses device-independent units, so the same logical minimum assertion covers the common 125% scaling layout; trimming is retained only where explicitly intentional.
- Preserved the intended `Segoe MDL2 Assets` chrome glyphs: minimize U+E921, maximize U+E922, close U+E8BB. The runtime test validates code points/font and, when installed, verifies each character exists in the font glyph map.
- Replaced the localized device-name string comparison used for the header indicator with the semantic `HasLightingDevice` state, preserving correct idle/connected color across languages.
- Thermal controls use dynamic localized accessibility resources; marker names remain informative and language-neutral temperature values.

## Deferred findings

- Task 6: resolved. Added a direct behavior regression that removes one key from the active Russian dictionary and verifies English fallback returns `Settings` without changing the active Russian language or weakening shipping dictionary parity.
- Task 10: resolved. Successful preview refresh after a temporary invalid point reorder now clears the stale localized validation key/message. The regression reproduces invalid ordering, restores a valid point, and verifies the error clears; raw English exception text is not exposed.

## Self-review

- Reviewed lifecycle ownership: all new language/settings subscriptions are detached by the existing dispose/close paths; no runtime, tray, lighting-session, startup, or background ownership sequence was changed.
- Reviewed threading: UI projections continue through captured synchronization contexts, and compiled localization dictionary loads are serialized to avoid WPF package-stream races in parallel hosts.
- Reviewed diagnostics: stable UI text does not replace existing exception propagation or logging.
- `git diff --check` is clean.
- No packaging manifests, installer metadata, publish scripts, CI release flow, or Task 14+ scope was modified.
