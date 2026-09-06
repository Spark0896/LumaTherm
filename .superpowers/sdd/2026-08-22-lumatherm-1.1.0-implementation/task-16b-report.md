# Task 16b report — bounded LumaTherm 1.1.0 release blockers

## Status

Implementation complete from base `f7b2355134aab72747e78cef7ef987c68cda5e6d`. No install, registration, application launch, RGB hardware write, Windows policy/registry change, certificate operation, tag, push, publication, or upload was performed.

## Binding ruling

Task 16b: Ruling: use exact defaults `(35, #006BFF)`, `(65, #D000FF)`, `(85, #FF1800)` with smoothing `0.8`; migrate only exact legacy default points `(35, #008CFF)`, `(65, #FFD800)`, `(85, #FF1800)`, preserving custom smoothing and every custom profile including yellow — cost if wrong: existing exact-default users receive this palette, custom profiles are untouched.

The same ruling is appended to `progress.md` so it survives compaction.

## Diagnosis and implementation

### Installer acceptance and background registration

Initial machine evidence established that the then-installed `Register-LumaTherm.ps1` could succeed and emit `pathsVerified`, `checksumsVerified`, `signedAnchorVerified`, `applicationSignatureVerified`, `signatureMatchedUntrusted`, `administratorPreflightPassed`, `certificateImportConfirmed`, `certificateImported`, `signaturesReverified`, and `postRegistrationVerified`; Windows then reported `LumaTherm_1.1.0.0_x64__jzd30fs6ag6cm` as `Ok`. That evidence identified the first defect as installer orchestration/acceptance. A later differently signed payload at the same four-part version exposed the platform replacement constraint documented in fix round 1 below.

Inno Setup invokes `CurStepChanged(ssPostInstall)` through exception handling, so raising from that callback alone was not a sufficient process exit-code contract. `packaging/LumaTherm.iss` now records success only after the registration helper starts and exits zero, and `GetCustomSetupExitCode` returns 1 unless that success was reached. The pre-install unregister callback was removed, preserving the prior identity until replacement registration occurs. The working registration helper was not changed; its exact identity/version/publisher/external-location/application/extension verification remains authoritative. No code changes Windows Dynamic Lighting policy.

### Saturated palette and exact-default migration

`ThermalProfile.Default` and the hardware-confirmed smoke cycle now use exactly blue `#006BFF` at 35 °C, magenta `#D000FF` at 65 °C, and red `#FF1800` at 85 °C, with smoothing still `0.8`. Full-saturation HSV interpolation and unlimited custom points are unchanged.

Settings migration replaces points only when the validated profile's full point sequence exactly equals the legacy three-point default. It preserves the user's smoothing and all other preferences. Any custom point set—including a profile containing the old yellow—is preserved exactly.

Secondary, muted, and idle-control colors were raised to tested dark-surface contrast targets (`#C5D0D8`, `#AAB6C0`, `#9AA8B3`). New test-window strings have matching RU/EN keys.

### Mouse-editable lighting test

The main 0..120 °C slider now uses an explicit WPF `Track`, 42 px hit area, 24×28 px thumb, TwoWay value binding, and `IsMoveToPointEnabled=True`, supporting drag and click-to-position. Existing profile-point dragging remains intact.

Selecting a profile point exposes a mouse/keyboard reachable temperature field and color button. The color button uses the existing `IColorPickerService` pattern. Valid draft edits are serialized through the scoped `ILightingTestSession` and update live preview output without saving preferences. Apply waits for queued live updates and saves the draft; Cancel/disposal restores the committed runtime profile and prior mode-enabled state. Existing minimize/deactivation behavior was not altered; runtime code changed only for the focused scoped-draft regression.

## Files changed

- Installer: `packaging/LumaTherm.iss`; static acceptance regression in `tests/LumaTherm.Packaging.Tests/InstallerScriptTests.cs`.
- Palette/runtime: `src/LumaTherm.Core/Colors/ThermalProfile.cs`, `src/LumaTherm.Core/Runtime/ILightingTestSession.cs`, `src/LumaTherm.Core/Runtime/ThermalRuntime.cs`, and Core regressions.
- Migration: `src/LumaTherm.Infrastructure/Settings/SettingsMigrator.cs` and `tests/LumaTherm.Infrastructure.Tests/Settings/JsonSettingsStoreTests.cs`.
- App/UI: `App.xaml`, `ProductionAppServices.cs`, RU/EN resource dictionaries, `LightingTestViewModel.cs`, `LightingTestWindow.xaml`, and App localization/UI/ViewModel regressions.
- Diagnostics: `tools/LumaTherm.Smoke/SmokeCommand.cs` and smoke regressions.
- Durable records: `progress.md` and this report.

Fix round 1 additionally changes `scripts/Register-LumaTherm.ps1` and the two executable portable registration fixtures in `Task14SecurityRegressionTests.cs` and `InstallerScriptTests.cs`.

## Focused TDD evidence

All commands ran from the Task 16b worktree with the repository-pinned .NET 8.0.423 SDK.

### Installer acceptance

RED:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj -c Release --filter "FullyQualifiedName~InnoDefinitionReturnsFailureUnlessPostInstallRegistrationCompletes" --no-restore
```

Result: 0 passed, 1 failed. The new assertion found the pre-registration `PrepareToInstall` identity removal and no final exit-code acceptance contract.

GREEN: the same command passed 1/1 after removing pre-unregister and adding `RegistrationSucceeded` plus `GetCustomSetupExitCode`.

### Palette

RED:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ColorEngineTests" --no-restore
```

Result: 48 passed, 4 failed; expected the new saturated point/interpolation values but production returned the legacy blue/yellow palette.

GREEN:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ColorEngineTests|FullyQualifiedName~LightingTest_DraftProfileRendersImmediatelyAndDisposalRestoresCommittedProfile" --no-restore
```

Result: 53 passed, 0 failed.

### Exact legacy-default migration

RED:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.Infrastructure.Tests\LumaTherm.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~SchemaTwo_ExactLegacyDefaultPointsReceiveTheVividPaletteAndPreserveSmoothing|FullyQualifiedName~SchemaTwo_CustomProfileContainingLegacyYellowIsPreservedExactly|FullyQualifiedName~SchemaOne_IsMigratedToSchemaTwoWithDefaultPreferences" --no-restore
```

Result: 2 passed, 1 failed; the exact schema-2 legacy default remained unmigrated.

GREEN: the same command passed 3/3. The custom yellow profile stayed byte-for-value equivalent at the model boundary, while the exact legacy triplet changed and retained smoothing `1.3`.

### Scoped live draft and restore

RED: the focused Core test above initially failed compilation with `CS1061` at `ThermalRuntimeTests.cs(757,23)`: `ILightingTestSession` had no `SetProfileAsync`.

GREEN: the combined Core command above passed 53/53, proving immediate draft rendering, unchanged committed settings, disposal restore, and preserved enabled mode.

### Selected point editing

RED:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj -c Release --filter "FullyQualifiedName~SelectedPointTemperatureEditWritesDraftProfileThroughScopedSession|FullyQualifiedName~PickSelectedPointColorUsesExistingPickerAndWritesDraftProfileThroughScopedSession" --no-restore
```

Result: compile failure because `SelectedPointTemperature`, `SelectedPoint`, `PickSelectedColorCommand`, the color-picker constructor dependency, and the session profile-write contract did not exist.

GREEN: the same command passed 2/2 after the minimal ViewModel/session implementation.

### Slider, selected-point controls, contrast, and RU/EN parity

RED:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj -c Release --filter "FullyQualifiedName~Window_UsesProfileEditorPreviewAndExactTemperatureRange|FullyQualifiedName~Window_SelectedPointEditorExposesMouseReachableTemperatureAndColorControls|FullyQualifiedName~DarkThemeSecondaryMutedAndControlColorsMeetReadableContrastTargets|FullyQualifiedName~EnglishResources_ContainFoundationKeysForEveryPlannedSurface" --no-restore
```

Result: 0 passed, 4 failed: secondary contrast was 7.37:1 below the 9:1 target, three selected-point localization keys were absent, selected-point controls were absent, and the slider hit target was under 40 px.

GREEN:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj -c Release --filter "FullyQualifiedName~Window_UsesProfileEditorPreviewAndExactTemperatureRange|FullyQualifiedName~Window_SelectedPointEditorExposesMouseReachableTemperatureAndColorControls|FullyQualifiedName~DarkThemeSecondaryMutedAndControlColorsMeetReadableContrastTargets|FullyQualifiedName~EnglishResources_ContainFoundationKeysForEveryPlannedSurface|FullyQualifiedName~RussianAndEnglishResources_HaveTheSameCompleteKeySet" --no-restore
```

Result: 5 passed, 0 failed.

### Smoke and existing visual expectations

After changing the Core default, the full smoke project exposed one stale palette expectation (18 passed, 1 failed). Focused cycle RED:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.Smoke.Tests\LumaTherm.Smoke.Tests.csproj -c Release --filter "FullyQualifiedName~ConfirmedCycle_WritesTheThreeSafetyColorsInOrderAndReleases|FullyQualifiedName~ConfirmedCycle_ReleasesOwnershipWhenTheSecondWriteFails" --no-restore
```

Result: 0 passed, 2 failed; the smoke command still wrote legacy `#008CFF`/`#FFD800`. After updating the diagnostic constants, the full smoke project passed 19/19.

Focused App sweep:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj -c Release --filter "FullyQualifiedName~MainViewModelTests|FullyQualifiedName~ThermalCoreRuntimeTests" --no-restore
```

RED result: 52 passed, 3 failed because dashboard/theme expectations still encoded the old palette/contrast colors. GREEN result after expectation-only updates: 55 passed, 0 failed.

An initial full-suite diagnostic then passed 605 tests and failed one final stale Task 13 icon-color expectation. That expectation was updated to the new tested idle-control color; the fresh final suite below passed all 606.

## Final verification

Release build:

```powershell
.\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release --no-restore
```

Result: exit 0; 0 warnings, 0 errors.

Full suite against that build:

```powershell
.\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release --no-build --no-restore
```

Result: exit 0; Core 124/124, Infrastructure 113/113, App 269/269, Smoke 19/19, Packaging 81/81; total 606/606.

Final whitespace verification:

```powershell
git diff --check
```

Result: recorded after the final report edit and before commit; no whitespace errors.

## Self-review

- Scope is limited to the three approved blocker areas plus direct tests/records.
- Existing path, checksum, signed-anchor, signature, certificate-consent, post-registration, and Windows Dynamic Lighting policy guards remain intact; fix round 1 changes only the precisely gated same-version replacement branch.
- Installer failure is enforced at process exit, while the prior identity is not proactively removed.
- Migration equality covers the complete ordered legacy point set only and retains custom smoothing/settings.
- The point editor uses the established picker abstraction and valid drafts write only through the owned test session.
- Session disposal restores the committed profile under the runtime's existing lifecycle/process locks and leaves prior live mode enabled.
- Existing drag editing, minimize/deactivation, tray lifetime, HSV interpolation, and unlimited-point behavior were preserved.
- No unrelated dirty changes were present at start; all changed files are intentional.

## Remaining machine acceptance (controller-authorized phase only)

1. Rebuild the signed 1.1.0 payload and installer with the pinned release toolchain; verify package/installer signatures and hashes.
2. Force a controlled registration failure and prove the installer returns nonzero and does not report success. For a same-version replacement after exact removal, verify the diagnostic explicitly reports that the prior identity is no longer installed and cannot be rolled back safely; verify settings and installed files remain untouched.
3. Run a successful installer and capture helper success events plus package status `Ok`; verify identity `LumaTherm`, version `1.1.0.0`, publisher `CN=LumaTherm Local`, external location equal to the installed app directory, application ID `LumaTherm`, and extension `com.microsoft.windows.lighting`.
4. Verify settings preservation and exact-only legacy palette migration on a copied real schema-2 settings file; separately verify a custom profile containing yellow is unchanged.
5. Verify desktop and Start Menu shortcuts, then authorized launch/tray/minimize/deactivation behavior: the runtime remains active, the tray process remains alive, and background LampArray provider ownership works under the existing `ControlledByForegroundApp=1` policy without changing that registry value.
6. With explicit hardware-write authorization, exercise slider thumb drag, click-to-position, profile-point mouse drag, selected-point temperature/color editing, real-time lighting preview, Apply persistence, Cancel restore, and prior live-mode-state preservation on the actual LampArray device.

No machine mutation was executed by this implementation agent; machine evidence in this report was supplied by the controller.

## Machine-acceptance fix round 1/5

### Machine finding and root cause

The signed 1.1.0 installer copied the new payload and correctly exited 1. Direct elevated registration then failed in `Add-AppxPackage` with HRESULT `0x80073CFB`: `LumaTherm_1.1.0.0_x64__jzd30fs6ag6cm` was already installed at the same version with different content, and Windows required either a higher version or removal of the existing package. Events before failure were `pathsVerified`, `checksumsVerified`, `signedAnchorVerified`, `applicationSignatureVerified`, `signatureMatchedUntrusted`, `administratorPreflightPassed`, `certificateImportConfirmed`, `certificateImported`, `signaturesReverified`, and `certificateRolledBack`. The exact old identity remained `Status Ok`; settings SHA-256 remained `CDE73B85EA1B56E1A80BE6B700387D4EEB7DAC556CA098C053D72599E9E61E1E`.

The incoming signed `AppxManifest.xml` identity is now read and validated as exact Name `LumaTherm`, Publisher `CN=LumaTherm Local`, and a valid four-part package Version. Immediately before the single `Add-AppxPackage` call, the helper queries installed packages and removes by exact `PackageFullName` only when exactly one exact Name/Publisher match exists and its version equals that validated incoming version. Fresh installs, version upgrades, other publishers, lookalikes, other versions, and ambiguous multiple exact matches do not enter the removal path. The event `sameVersionPackageRemoved` records the exact path.

If registration or post-verification fails after that removal, the helper remains nonzero and emits `sameVersionReplacementFailed` with the explicit statement that the previous identity is no longer installed and cannot be rolled back safely. Cleanup may remove a failed newly added replacement, but it does not describe that as restoration of the old equal-version package. User settings/data and Windows Dynamic Lighting policy are not touched. The Inno installer success gate remains unchanged and still requires helper exit zero.

### Focused RED/GREEN

RED, captured before production changes:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj -c Release --filter "FullyQualifiedName~ExactSingleSameVersionIdentityIsRemovedImmediatelyBeforeRegistration|FullyQualifiedName~FreshUpgradeUnknownAndAmbiguousInventoriesRemoveNothing|FullyQualifiedName~FailedSameVersionReplacementReportsUnrecoverableIdentityLossWithoutRollbackClaim" --no-restore
```

Result: exit 1; 0 passed, 7 failed. The executable PowerShell harness rejected the absent `PreRegistrationPackagesJsonForTest` parameter (`NamedParameterNotFound`), proving the guarded inventory/removal behavior did not exist.

GREEN, identical command after the minimal helper implementation: exit 0; 7 passed, 0 failed. It proves exact removal command targeting and ordering, no removal for fresh/upgrade/unknown/ambiguous inventories, and honest nonzero identity-loss reporting without a rollback claim.

Complete packaging project:

```powershell
.\.dotnet\dotnet.exe test tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj -c Release --no-restore
```

Result: exit 0; 88/88 passed.

### Fix-round final verification

```powershell
.\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release --no-restore
.\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release --no-build --no-restore
git diff --check
```

Result: Release build exit 0 with 0 warnings and 0 errors. Fresh full-suite rerun exit 0: Core 124/124, Infrastructure 113/113, App 269/269, Smoke 19/19, Packaging 88/88; total 613/613. `git diff --check` reported no whitespace errors.

The first concurrent full-suite attempt passed 612 tests and hit one pre-existing timing race in `BoundedProcessTestHostTests.TimeoutKillsChildTreeAndReleasesItsFileLockWithinTheBound` because its child lock was briefly still held. The exact test immediately passed 1/1 in isolation, and the full fresh rerun passed 613/613; no unrelated process-host code was changed.

### Fix-round self-review and remaining acceptance

- The removal condition is fail-closed: one exact Name/Publisher match, exact incoming manifest Version, and nonempty exact PackageFullName are all required.
- Only `Remove-AppxPackage -Package <exact full name>` is used; no wildcard, `-AllUsers`, broad pre-unregister, settings deletion, or policy mutation was added.
- The removal is immediately adjacent to the single registration call and occurs only after all path/hash/signature/trust validation.
- Normal fresh and higher-version upgrade behavior remains the existing direct `Add-AppxPackage` path.
- Failure after same-version removal is deliberately nonzero and names irreversible identity loss; it does not claim the prior package was restored.
- Machine acceptance must rebuild/re-sign the payload, rerun the installer, confirm `sameVersionPackageRemoved` precedes `postRegistrationVerified`, recheck exact package identity/status/external location/extension, and confirm the settings hash remains unchanged.
