# SDD ledger — plan: docs/superpowers/plans/2026-08-22-lumatherm-1.1.0-implementation.md

Workspace: `.superpowers/sdd/2026-08-22-lumatherm-1.1.0-implementation/`
Branch: `feature/lumatherm-v1`
Plan base: `15b4c0a5a8473ff17104f3e582cbf252c08d605b`
Spec: `docs/superpowers/specs/2026-08-22-lumatherm-1.1.0-design.md`

## Pre-flight task consistency

| Task | Internal consistency | Result |
| --- | --- | --- |
| 1 | Version tests match central props and package identity edits. | Clean. |
| 2 | Multi-point tests match the new domain types and generalized UI consumers. | Clean. |
| 3 | Schema-2 tests match explicit v0/v1 migration and new preference fields. | Clean. |
| 4 | Regression tests match a dedicated preference-save API. | Clean. |
| 5 | Runtime session tests match scoped override lifecycle. | Clean; release-call assertions must compare deltas, not assume a pristine fake. |
| 6 | Resource tests match dynamic RU/EN dictionary switching. | Clean. |
| 7 | Editor tests match stable-ID point operations and unlimited valid points. | Clean. |
| 8 | STA tests match the reusable WPF editor control. | Clean. |
| 9 | Settings tests match editor/localization/tray integration. | Clean; lifecycle ownership must be explicit. |
| 10 | Test-window tests match scoped runtime session and draft/apply behavior. | Clean; WPF Closing cannot be awaited directly. |
| 11 | Tray tests match typed commands, visibility options, and live temperature. | Clean. |
| 12 | SemVer/feed/About tests match a manual, link-only update flow. | Clean. |
| 13 | Literal/contrast tests match full localization adoption and style repair. | Clean. |
| 14 | Packaging tests match Inno plus sparse identity and stable external signing input. | Clean; existing full-package manifest and new sparse manifest need one authoritative shipping path. |
| 15 | Documentation tests match the public bilingual repository contract. | Clean. |
| 16 | Verification steps match signed artifacts, hardware acceptance, screenshots, and publication. | Clean; certificate path remains an operator-provided secret and never enters artifacts. |

## Pre-flight shared-file and interface map

| Tasks | Shared surface | Finding |
| --- | --- | --- |
| 1 → 14 | `Directory.Build.props`, package version and manifests | Task 14 consumes exact 1.1.0 metadata from Task 1. |
| 1 → 12 | assembly version | About reads the central version established by Task 1. |
| 2 → 3 | `ThermalProfile` JSON shape | Task 3 migrates old fields into Task 2 points. |
| 2 → 4 | `ThermalProfile.ContentEquals` | Runtime preference update uses value equality from Task 2. |
| 2 → 7 | `ThermalPoint` and `ColorEngine.Map` | Editor creates and previews points using Core behavior. |
| 2 → 8 | multi-stop gradient | WPF strip renders the Core point sequence. |
| 2 → 9 | removal of fixed fields | Settings replaces old Cold/Warm/Hot controls after Core migration. |
| 3 → 6 | `AppLanguage` | Localization applies the persisted enum. |
| 3 → 9 | schema-2 fields | Settings edits Language and TrayMenuOptions. |
| 3 → 11 | `TrayMenuOptions` | Real tray visibility is driven by saved preferences. |
| 3 → 14 | settings compatibility | Installer update must preserve schema migration input and local settings. |
| 4 → 5 | `IThermalRuntime` | Test-session API extends the preference-safe runtime contract. |
| 4 → 9 | `UpdatePreferencesAsync` | Settings save must not alter live mode. |
| 4 → 10 | preference callback | Test Apply saves a profile without altering live mode. |
| 4 → 11 | runtime fakes and settings | Tray observes the same authoritative runtime settings. |
| 5 → 10 | `ILightingTestSession` | Test window owns and disposes the runtime override. |
| 6 → 9 | localization service/resources | Settings and language selector consume shared dictionaries. |
| 6 → 11 | localization service/resources | Tray rebuilds localized labels on change. |
| 6 → 12 | localization service/resources | About/update states use the same catalog. |
| 6 → 13 | resources and existing literals | Task 6 establishes inventory; Task 13 completes adoption across old UI. |
| 7 → 8 | editor ViewModels | WPF control delegates mutations to the tested editor. |
| 7 → 9 | `ProfileEditor` | Settings uses the same draft model. |
| 7 → 10 | editor draft | Test window uses a separate draft instance. |
| 8 → 9 | `ThermalProfileEditor` | Settings embeds the reusable control. |
| 8 → 10 | `ThermalProfileEditor` | Test window embeds the same control. |
| 9 → 10 | `SettingsViewModel` and save callback | Test Apply routes through preference-safe save. |
| 9 → 13 | `SettingsView.xaml` and ViewModel | Task 13 localizes the redesigned settings rather than resurrecting old controls. |
| 9 → 15 | user-visible settings behavior | README documents actual editor/tray/test behavior. |
| 10 → 13 | test window resources | Task 13 verifies full bilingual presentation. |
| 10 → 16 | physical test path | Hardware acceptance uses the shipped test window. |
| 11 → 13 | tray labels/resources | Task 13 completes localization of real and preview tray text. |
| 11 → 16 | inactive-window behavior | Acceptance verifies background ownership with the final tray service. |
| 12 → 13 | About resources/navigation | Task 13 checks language switching without window recreation. |
| 12 → 15 | GitHub/version/license content | Documentation matches exact About/update behavior. |
| 12 → 16 | GitHub latest release endpoint | It can only return 1.1.0 after publication; pre-publication tests use fakes. |
| 13 → 16 | UI layout | Screenshots are captured only after visual/localization completion. |
| 14 → 15 | install/portable behavior | Documentation describes verified sparse identity and certificate flow. |
| 14 → 16 | release artifacts | Acceptance and publication use Task 14 outputs. |
| 15 → 16 | docs, changelog, screenshots | Task 16 adds evidence and final release state without rewriting contracts. |

## Pre-flight rulings

Ruling: Task 5 release assertions compare the change in release count around the test session — existing fixture setup may already release once — cost if wrong: a brittle test or a hidden extra release.

Ruling: `SettingsViewModel` implements `IDisposable` in Task 9 and ProductionAppServices owns disposal — required to unsubscribe runtime/localization events — cost if wrong: retained window/ViewModel subscriptions.

Ruling: Task 10 cancels the first WPF Closing event, awaits session cleanup through one async close command, then performs an explicitly permitted close — WPF Closing itself is synchronous — cost if wrong: window close can race test-session disposal.

Ruling: Task 14 moves/converts the existing `packaging/AppxManifest.xml` into `packaging/sparse/AppxManifest.xml` and updates all build/tests; there will be one authoritative shipping identity manifest, not two — cost if wrong: downstream paths need adjustment, but duplicate divergent identities are avoided.

Ruling: Task 6 creates the complete RU/EN key inventory, while Task 13 replaces remaining legacy literals and may add only keys discovered by that migration with parity tests in the same commit — cost if wrong: one extra resource-only addition in Task 13.

Ruling: GitHub publication remains last and uses the user's prior explicit authorization; no remote mutation occurs before Task 16 acceptance — cost if wrong: publication must be stopped and reverted before release creation.

## Task progress

Task 1: complete (commits 15b4c0a..ca4805e, review clean)

Task 2: Ruling: the plan's literal `(128,255,0)` at 70°C conflicts with the binding saturated shortest-arc HSV design; the expected midpoint between green@60 and red@80 is `(255,255,0)` — cost if wrong: one corrected test expectation, while changing production interpolation would break hardware saturation requirements.

Task 2: fix round 1/5 (2 addressed, 1 open — zero-range label offset can become NaN; commits 5d16761..373fda7)

Task 2: fix round 2/5 (1 addressed, 0 open — guarded/clamped zero-range offsets; commits 373fda7..6246200)
Task 2: complete (commits ca4805e..6246200, review clean)

Task 3: fix round 1/5 (1 addressed, 0 open — invalid legacy profiles translate to recoverable InvalidDataException; commits b56f005..6b042a8)
Task 3: complete (commits 6246200..6b042a8, review clean)

Task 4: complete (commits 6b042a8..225a96e, review clean)

Task 5: fix round 1/5 (3 addressed, 0 open — sensor-loss re-arm, suspended cleanup, lifecycle coverage; commits 9ade007..71b376d)
Task 5: complete (commits 225a96e..71b376d, review clean)

Task 6: minor (deferred): defensive English fallback lookup is implemented but lacks a direct regression because parity-complete shipping dictionaries always resolve in the active dictionary; final whole-branch review must triage.
Task 6: fix round 1/5 (0 addressed, 1 open — report still omitted explicit warnings/noise status; commits ff49a9b..ff49a9b)
Task 6: fix round 2/5 (1 addressed, 0 open — report records 0 warnings/errors/skips/noise; commits ff49a9b..ff49a9b)
Task 6: complete (commits 71b376d..ff49a9b, review clean with 1 deferred minor)

Task 7: fix round 1/5 (3 addressed, 0 open — cross-neighbor reorder, edge coverage, exact evidence; commits 0fbd3dd..971bf70)
Task 7: complete (commits ff49a9b..971bf70, review clean)

Task 8: fix round 1/5 (4 addressed, 0 open — drag capture/lifecycle, subscription lifetime/reset, stale gradient, routed input coverage; commits ffe34fa..c3d7c26)
Task 8: complete (commits 971bf70..c3d7c26, re-review approved)

Task 9: initial review rejected (2 important, 1 minor — authoritative autostart, tray preview flags, color-picker failure handling; commit 1036a97)
Task 9: fix round 1 committed (083fd3c; 45 focused, 448 full, Release clean)
Task 9: re-review rejected (2 important lifecycle races remain — Dispose/Save and Reset/pending autostart read)
Task 9: fix round 2 paused by user at RED stage; uncommitted test-only diff in SettingsViewModelTests.cs (83 insertions), implementation not started. Resume from HEAD 083fd3c without discarding tests.
Task 9: fix round 2 completed (3da3f06; RED 0/2, lifecycle GREEN 2/2, Settings 47/47, full 450/450, Release clean)
Task 9: fix round 2 re-review approved (Dispose/Save and Reset/pending-read races addressed, no new Critical/Important findings)
Task 9: complete (commits c3d7c26..3da3f06, review clean)

Ruling (user amendment, Tasks 14/16): installer upgrade uses one stable Inno Setup AppId and in-place replacement of an older LumaTherm installation, including safe app shutdown; this is simpler and preserves user settings. Cost if wrong: an incompatible legacy install may require explicit uninstall fallback.
Task 16 release gate: stop/uninstall the currently installed old LumaTherm, install the newly built installer, launch it, enable thermal mode, minimize fully to tray/make window inactive, and verify background temperature-to-RGB control remains active on real LEDs rather than reverting to another controller.
Task 14/16 evidence must record detected old version/uninstall result, new installed version/path/shortcuts, upgrade behavior, minimized-background duration, runtime/log continuity, and physical RGB observation/cleanup.

Task 10: minor (deferred): successful preview refresh after temporary draft reorder can leave a stale validation error/raw English exception visible; final whole-branch review must triage.
Task 10: minor (deferred): cleanup/open exceptions are swallowed without durable logging after the modal closes; final whole-branch review must triage.
Task 10: fix round 1/5 (3 addressed, 0 open — pending Open/Close, shutdown cleanup ownership, authoritative Apply; commits ac44ee5..1b43e08)
Task 10: fix round 1 re-review approved (fresh narrow 8/8; no new Critical/Important findings)
Task 10: complete (commits 3da3f06..1b43e08, review clean with 2 deferred minors)

Task 11: minor (deferred): real NotifyIconTrayPlatform disposal marks disposed before potentially throwing `Visible = false`, preventing retry and possibly leaking resources; final whole-branch review must triage.
Task 11: fix round 1/5 (2 addressed, 0 open — real minimize/deactivate ownership regression, transactional tray construction rollback; commits f53ef3d..35532e7)
Task 11: fix round 1 re-review approved (fresh narrow 4/4; no new Critical/Important findings)
Task 11: complete (commits 1b43e08..35532e7, review clean with 1 deferred minor)

Task 12: minor (deferred): GitHubReleaseFeed mutates timeout on a caller-owned HttpClient, which can affect other consumers or throw after first use; production client is owned, final whole-branch review must triage.
Task 12: fix round 1/5 (3 addressed, 0 open — explicit release flags, pre-normalization traversal rejection, runtime-localized navigation accessibility; commits 241d4dd..966083e)
Task 12: fix round 1 re-review approved (feed 16/16, link 24/24, accessibility 2/2; no new Critical/Important findings)
Task 12: complete (commits 35532e7..966083e, review clean with 1 deferred minor)

Task 13: implementation complete at 4df99dc (focused 78/78, App 263/263, full 558/558, Release 0 warnings/errors; worktree clean).
Task 13: deferred Task 6 English fallback regression and Task 10 stale preview-validation/raw-error finding reported resolved in implementation.
Task 13: paused by user before independent review completed. Review package exists at task-13-review-package.md; resume by re-dispatching a fresh Task 13 reviewer from base 966083e to head 4df99dc. Do not reimplement or rerun completed implementation first.
Task 13: fix round 1/5 (2 addressed, 0 open — resolved About secondary brush/contrast and preserved unavailable device semantics across language changes; commits 4df99dc..88329b9)
Task 13: fix round 1 re-review approved (isolated regressions 2/2; no new Critical/Important findings)
Task 13: complete (commits 966083e..88329b9, review clean; Task 6 and Task 10 deferred localization findings resolved)

Task 14: minor (deferred): installer has no optional post-install launch entry; final whole-branch review must triage against accepted design.
Task 14: minor (deferred): manifest asset tests no longer prove every manifest-referenced asset/PublicFolder exists; final whole-branch review must triage.
Task 14: minor (deferred): PFX password reaches SignTool as plaintext process argument, risking process-list/history exposure despite output redaction; final whole-branch review must triage.

Task 14: fix round 2/5 (2 addressed, 1 open — publisher mismatch and real AssetBuilder coverage restored; actual Inno compile gate still silently skips without a verified official ISCC input; commits c044d37..8bee54a)

Task 14: fix round 3/5 (1 addressed, 1 open — mandatory verified official Inno compile gate restored; new containment/stdout ambiguity raised; commits 8bee54a..3fab04a)

Task 14: fix round 4/5 (1 addressed, 0 open — path-boundary construction made explicit and exact unsafe mutation regression-tested; commits 3fab04a..868cb68)

Task 14: complete (commits 88329b9..868cb68, review clean with 3 deferred minors)

Task 15: fix round 1/5 (5 addressed, 0 open — compatibility/manual update/schema/security reporting/RU link contracts; commits b322c71..f255bee)

Task 15: complete (commits 868cb68..f255bee, review clean)

Ruling (Task 16 publication order): complete local automated, installer, portable, screenshot, and hardware acceptance and a pre-publication whole-branch review before any public push/tag/release mutation — user required deployment only after all changes and checks; cost if wrong: publication is delayed by one review checkpoint, but no public rollback is needed for review findings.
Task 16: recovery slice complete (commit d89bb9f; Fusion `<msix>` activation manifest corrected, sparse `runFullTrust`/`win32App`/`mediumIL` contract preserved, 598/598 full suite, task review clean).

Task 16: installed-state checkpoint before retry — existing package `LumaTherm_1.0.1.0_x64__jzd30fs6ag6cm` remains Status Ok, no LumaTherm process is running, desktop shortcut exists, Start Menu shortcut is absent, settings SHA-256 remains `4DDC31323D58625CBC875F6110558C7D15E806CFE0A096E7A9BA088D46574B3F`.

Task 16b: Ruling: use exact defaults (35, #006BFF), (65, #D000FF), (85, #FF1800) with smoothing 0.8; migrate only exact legacy default points (35, #008CFF), (65, #FFD800), (85, #FF1800), preserving custom smoothing and every custom profile including yellow — cost if wrong: existing exact-default users receive this palette, custom profiles are untouched.

Task 16b: complete (commits f7b2355..eab24e1, review clean)

Task 16b: fix round 1/5 (1 addressed, 0 open — machine `0x80073CFB` same-version/different-content AppX block resolved by removing only one exact Name/Publisher/incoming-Version package by exact PackageFullName immediately before replacement registration; settings/files remain untouched and identity-loss risk on replacement failure is reported without rollback claim)

Task 16b: complete after fix round 1 (commits 61ede68..c72a9de, review clean)
