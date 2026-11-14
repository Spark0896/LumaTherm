# Architecture

LumaTherm is a Windows desktop app with four boundaries:

- `LumaTherm.Core` contains profiles, color interpolation, runtime state, settings contracts, and update abstractions. It does not call Windows APIs.
- `LumaTherm.Infrastructure` implements NVML, MSI Afterburner shared-memory reading, Windows LampArray, JSON settings, logging, autostart, session/power services, and the GitHub release feed.
- `LumaTherm.App` composes the production services and hosts the WPF interface, tray, localization, and dialogs.
- `LumaTherm.Packaging`, PowerShell scripts, and Inno Setup build the signed sparse identity, setup, portable payload, and checksum manifests.

## Runtime data flow

`NvmlTemperatureSource` is the preferred source. `AfterburnerTemperatureSource` is tried as a read-only fallback. `TemperatureProvider` supplies readings to `ThermalRuntime`, which maps the selected thermal profile through `ColorEngine` and sends only deduplicated / rate-gated changes through `LampArrayLightingController`. The controller uses `Windows.Devices.Lights.LampArray`; it does not use vendor SDKs or direct HID writes.

When the mode is disabled, the app exits, or sensor loss requires release, the runtime attempts to release Lighting ownership. A failed safety release while the temperature reading is unavailable is retried on the lighting retry interval. The runtime keeps the handle until release succeeds or Windows reports that the handle is closed. A lighting-test session is separately scoped and is closed safely before ordinary operation resumes.

## Settings and recovery

Settings reside in `%LOCALAPPDATA%\LumaTherm\settings.json`; logs are in `%LOCALAPPDATA%\LumaTherm\logs`. `AppSettings` currently uses `SchemaVersion: 2`, with `Profile.Points` and `Profile.SmoothingSeconds` as the canonical profile data; derived Cold/Warm/Hot aliases are neither serialized nor applied on load. The JSON store saves atomically through a temporary file and validates on load. Invalid settings are quarantined as `settings.corrupt-<UTC timestamp>.json`, then recovery defaults are used. Temporary access or I/O failures stop startup with an error while preserving the existing settings file.

`SettingsMigrator` accepts schema 0 and 1 as legacy formats and produces schema 2. Do not change a persisted field or add a schema version without migration tests. The default profile has three saturated default points: 35°C blue (#006BFF), 65°C green (#3CFF00), and 85°C red (#FF0000). Users may define additional points (minimum two); temperatures must be 0–120 °C, adjacent points at least 1 °C apart, and smoothing must be 0.1–5.0 seconds. There is no separate point-count setting; the range and spacing permit at most 121 points. Recovery defaults keep mode and autostart disabled. When no settings file exists, first-run settings enable both; upgrades preserve saved choices. Saving preferences does not change the live mode; enabling or disabling it remains a separate runtime operation.

## Localization and updates

Resource keys are paired in `src/LumaTherm.App/Resources/Strings.en-US.xaml` and `Strings.ru-RU.xaml`. Add the same key to both dictionaries, retain formatting placeholders, and cover the pair in localization tests. The application has a system-language default with English fallback and manual English or Russian selection. `GitHubReleaseFeed` requests the project's GitHub `releases/latest` endpoint over HTTPS with a five-second timeout and a bounded response only when the user manually initiates a check, with no startup or background polling. It accepts only non-draft, non-prerelease semantic-version releases and opens the release URL; it is not an in-app downloader or updater.
