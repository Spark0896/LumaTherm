# Architecture

LumaTherm is a Windows desktop app with four boundaries:

- `LumaTherm.Core` contains profiles, color interpolation, runtime state, settings contracts, and update abstractions. It does not call Windows APIs.
- `LumaTherm.Infrastructure` implements NVML, MSI Afterburner shared-memory reading, Windows LampArray, JSON settings, logging, autostart, session/power services, and the GitHub release feed.
- `LumaTherm.App` composes the production services and hosts the WPF interface, tray, localization, and dialogs.
- `LumaTherm.Packaging`, PowerShell scripts, and Inno Setup build the signed sparse identity, setup, portable payload, and checksum manifests.

## Runtime data flow

`NvmlTemperatureSource` is the preferred source. `AfterburnerTemperatureSource` is tried as a read-only fallback. `TemperatureProvider` supplies readings to `ThermalRuntime`, which maps the selected thermal profile through `ColorEngine` and sends only deduplicated / rate-gated changes through `LampArrayLightingController`. The controller uses `Windows.Devices.Lights.LampArray`; it does not use vendor SDKs or direct HID writes.

When the mode is disabled, the app exits, or sensor loss requires release, the runtime attempts to release Lighting ownership. Failed release is surfaced as runtime status/logging rather than silently treated as success. A lighting-test session is separately scoped and is closed safely before ordinary operation resumes.

## Settings and recovery

Settings reside in `%LOCALAPPDATA%\LumaTherm\settings.json`; logs are in `%LOCALAPPDATA%\LumaTherm\logs`. `AppSettings` currently uses `schemaVersion: 2`. The JSON store saves atomically through a temporary file and validates on load. Invalid or unreadable settings are quarantined as `settings.corrupt-<UTC timestamp>.json`, then defaults are used.

`SettingsMigrator` accepts schema 0 and 1 as legacy formats and produces schema 2. Do not change a persisted field or add a schema version without migration tests. Defaults have mode and autostart disabled; profile points must remain ordered and are validated by Core.

## Localization and updates

Resource keys are paired in `src/LumaTherm.App/Resources/Strings.en-US.xaml` and `Strings.ru-RU.xaml`. Add the same key to both dictionaries, retain formatting placeholders, and cover the pair in localization tests. `GitHubReleaseFeed` requests the project's GitHub `releases/latest` endpoint over HTTPS with a five-second timeout and a bounded response. It accepts only non-draft, non-prerelease semantic-version releases and opens the release URL; it is not an in-app downloader or updater.
