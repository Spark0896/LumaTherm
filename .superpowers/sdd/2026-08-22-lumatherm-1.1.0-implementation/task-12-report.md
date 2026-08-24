# Task 12 report: About page and safe GitHub update check

## Status

Implemented Task 12 only. No repository, tag, release, installer execution, download execution, or Task 13+ publication work was performed.

## TDD evidence

### RED: SemVer, feed, and link launcher

Tests were added before their production types.

- `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Core.Tests -c Release -p:NuGetAudit=false --filter SemanticVersionTests`
  - Exit: `1`
  - Expected failure: `LumaTherm.Core.Updates` did not exist.
- `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Infrastructure.Tests -c Release -p:NuGetAudit=false --filter GitHubReleaseFeedTests`
  - Exit: `1`
  - Expected failure: `LumaTherm.Infrastructure.Updates` / `GitHubReleaseFeed` did not exist.
- `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter LinkLauncherTests`
  - Exit: `1`
  - Expected failure: `LinkLauncher` did not exist.

After the minimal implementation, the focused cycle passed:

- SemanticVersionTests: exit `0`, passed `17`, failed `0`, skipped `0`.
- GitHubReleaseFeedTests: exit `0`, passed `9`, failed `0`, skipped `0`.
- LinkLauncherTests: exit `0`, passed `16`, failed `0`, skipped `0`.

### RED: About ViewModel and compiled UI

- `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "AboutViewModelTests|SettingsUiRuntimeTests"`
  - Exit: `1`
  - Expected failure: `AboutViewModel` did not exist.

After implementing the ViewModel, page, navigation, resources, and composition lifetime, the filter passed `19/19`.

### Regression RED -> GREEN

The first full solution run found an existing shell contract regression because pre-Task-13 rail accessibility names had been changed from Russian to the active language.

- Isolated reproduction command:
  - `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "FullyQualifiedName~ThermalCoreRuntimeTests.Dashboard_LoadsCompiledXamlAndBindsPrimaryInteraction"`
  - RED: exit `1`, passed `0`, failed `1`; Home/Settings/About names were English while the existing test requires Cyrillic shell names.
  - Root cause: Task 12 had changed pre-existing shell accessibility strings that remain Task 13 scope.
  - Minimal fix: preserve the three existing Russian rail names; all newly added About controls/status/page text remain runtime-localized.
  - GREEN: exit `0`, passed `1`, failed `0`, skipped `0`.

## Final verification evidence

All commands ran in `C:\Users\User\Documents\project\monitoring_color\.worktrees\lumatherm-v1` with `-p:NuGetAudit=false`.

1. `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Core.Tests -c Release -p:NuGetAudit=false --filter SemanticVersionTests`
   - Exit `0`; passed `17`, failed `0`, skipped `0`; no warnings/noise.
2. `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Infrastructure.Tests -c Release -p:NuGetAudit=false --filter GitHubReleaseFeedTests`
   - Exit `0`; passed `9`, failed `0`, skipped `0`; no warnings/noise.
3. `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "AboutViewModelTests|SettingsUiRuntimeTests"`
   - Exit `0`; passed `19`, failed `0`, skipped `0`; no warnings/noise.
4. `& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false`
   - Exit `0`; passed `533`, failed `0`, skipped `0` across Core `123`, Infrastructure `104`, App `245`, Smoke `19`, Packaging `42`.
   - Noise: normal localized restore/build/VSTest progress only; no warnings or failures.
5. `& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false`
   - Exit `0`; warnings `0`, errors `0`.

`git diff --check` exited `0` before final verification.

## Security and lifecycle decisions

- SemVer accepts only three non-negative, non-overflowing stable numeric components, optionally prefixed with one lowercase `v`; it rejects missing/extra components, leading zeroes, negative values, prerelease/build metadata, whitespace, and overflow.
- The feed requests exactly `https://api.github.com/repos/Spark0896/LumaTherm/releases/latest` with `User-Agent: LumaTherm/1.1.0`, `HttpCompletionOption.ResponseHeadersRead`, a 5-second `HttpClient.Timeout`, and caller cancellation.
- GitHub payloads are bounded to `65,536` bytes both by `Content-Length` and streamed byte count. Non-success responses, malformed JSON, invalid stable tags, drafts, prereleases, missing assets, and non-HTTPS page/asset URLs fail without mutation or execution.
- All feed tests use a fake `HttpMessageHandler`; no live network test was added or run.
- `LinkLauncher` requires an absolute HTTPS URI, host exactly `github.com`, the default HTTPS port, no credentials, and a case-exact path at or below `/Spark0896/LumaTherm/`. Repository root is normalized to a trailing slash. Backslashes, percent-encoded paths, traversal, protocol-relative inputs, alternate hosts, subdomains, and lookalike repository names are rejected before the injected/real process start boundary.
- The application only shell-opens a revalidated GitHub repository/release-download URL. It never downloads, executes, installs, or publishes an artifact.
- About version is parsed from `AssemblyInformationalVersionAttribute` on the application assembly, with assembly version metadata only as fallback; the UI does not own a mutable version constant.
- About update state is exclusive (`Idle`, `Checking`, `Current`, `Available`, `Failed`). The async command and state predicate prevent overlap; release navigation is enabled only for a newer stable release.
- Feed/network failures are converted into the localized retryable `Failed` state.
- `AboutViewModel.Dispose` unsubscribes localization, cancels the lifetime token, and blocks post-disposal property updates. `WpfUiSession` owns one feed, disposes the ViewModel first to cancel in-flight work, then disposes the feed/owned `HttpClient`; both disposal paths are idempotent.
- RU/EN resources have key parity for every new visible/action/status/accessibility string, and the About page/status refresh in the existing window after language changes.

## Self-review

- Scope checked against the Task 12 brief: only update domain/feed, allowlisted navigation, About UI/ViewModel, shell composition/navigation, RU/EN resources, tests, and this report changed.
- Mutation review: wrong SemVer comparison/parse boundaries, wrong endpoint/header/timeout, missing stable/HTTPS/size/cancellation validation, loosened link host/path checks, wrong update state/enablement, overlapping checks, post-disposal updates, mutable UI version, missing localized About content, and broken info navigation each have a focused test that would fail.
- Resource ownership is single and explicit; no live network, installer execution, or publication side effect exists.
- Remaining concern: the GitHub endpoint may legitimately fail until Task 16 publishes the repository/release; this is intentionally represented as localized `Failed` and is not treated as an application fault.

## Fix Round 1/5 — Important review findings

Three Important findings were addressed with separate verified RED → GREEN cycles. The ledgered HttpClient-timeout ownership Minor remained out of scope.

### Feed stability fields

Test-first cases cover both fields omitted, each field omitted independently, `null` for either field, and a non-boolean value for either field. Existing `true` draft/prerelease rejection remains covered.

- RED command: `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Infrastructure.Tests -c Release -p:NuGetAudit=false --filter GitHubReleaseFeedTests`
  - Exit `1`; passed `13`, failed `3`, skipped `0`, total `16`.
  - Expected failures: payloads missing both stability fields, missing `draft`, and missing `prerelease` were incorrectly accepted. Null/wrong-type cases already failed safely.
- GREEN: same command exited `0`; passed `16`, failed `0`, skipped `0`.
- Implementation decision: `draft` and `prerelease` deserialize as nullable booleans and acceptance requires each property to be present and exactly JSON `false`. Missing/null values fail the stable-release gate; wrong JSON types remain caught as malformed release data.

### Original-input traversal rejection

Test-first cases include literal `releases/../issues`, literal single-dot segments, upper/lower percent-encoded dot segments, double-encoded dot segments, literal mixed backslash separators, and single/double-encoded backslash traversal. Existing valid repository root, release, and asset links remain covered.

- RED command: `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter LinkLauncherTests`
  - Exit `1`; passed `20`, failed `4`, skipped `0`, total `24`.
  - Expected failures: literal `.`/`..` and single-encoded dot segments normalized to an allowed in-repository path before validation.
- GREEN: same command exited `0`; passed `24`, failed `0`, skipped `0`.
- Implementation decision: the raw original path is isolated before query/fragment data, checked for slash/backslash dot segments, decoded repeatedly, and checked again before normalized URI allowlisting. Rejected input never reaches the process-start delegate.

### Runtime navigation accessibility localization

A new STA test exercises the same `MainWindow` through RU → EN → RU changes, verifies exact accessible names for Home/Settings/About, and verifies the active `ItemStatus` moves between all three buttons and refreshes from `Выбрано` to `Selected` without recreating the window.

- RED command: `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter SettingsUiRuntimeTestsAbout`
  - Exit `1`; passed `1`, failed `1`, skipped `0`, total `2`.
  - Expected failure: after switching to English, Home still exposed hard-coded `Главная`.
- Initial broader GREEN check exposed one stale test-only hard-coded `Выбрано` expectation while the active dictionary was English: exit `1`, passed `19`, failed `1`, total `20`.
- Final GREEN command: `& .\.dotnet\dotnet.exe test .\tests\LumaTherm.App.Tests -c Release -p:NuGetAudit=false --filter "AboutViewModelTests|SettingsUiRuntimeTests"`
  - Exit `0`; passed `20`, failed `0`, skipped `0`.
- Implementation decision: all three navigation names use RU/EN dynamic resources. The selected button owns a live `Accessibility.Selected` resource reference; inactive buttons clear the attached property. Older shell tests retain their non-empty accessibility purpose without assuming a specific active language.

### Final Fix Round 1 verification

All commands used Release configuration and `-p:NuGetAudit=false`.

1. Core focused (`SemanticVersionTests`): exit `0`; passed `17`, failed `0`, skipped `0`.
2. Infrastructure focused (`GitHubReleaseFeedTests`): exit `0`; passed `16`, failed `0`, skipped `0`.
3. App focused (`AboutViewModelTests|SettingsUiRuntimeTests`): exit `0`; passed `20`, failed `0`, skipped `0`.
4. Link security focused (`LinkLauncherTests`): exit `0`; passed `24`, failed `0`, skipped `0`.
5. Full solution: `& .\.dotnet\dotnet.exe test .\LumaTherm.sln -c Release -p:NuGetAudit=false`
   - Exit `0`; passed `549`, failed `0`, skipped `0`: Core `123`, Infrastructure `111`, App `254`, Smoke `19`, Packaging `42`.
6. Release build: `& .\.dotnet\dotnet.exe build .\LumaTherm.sln -c Release -p:NuGetAudit=false`
   - Exit `0`; warnings `0`, errors `0`.

Output noise was limited to normal localized restore/build/VSTest progress. No live network, installer execution, repository publication, or Task 13+ behavior was introduced.
