# Task 11 report — Diagnostics and application composition

## Result

Implemented the production composition root, crash-session safe mode, bounded JSONL diagnostics, WPF exception/lifecycle boundaries, single-instance-first startup, and exhaustive reverse cleanup. Production lighting remains direct Windows `LampArray`; no GCC/RGB Fusion process, vendor DLL, or raw HID dependency was introduced. Production temperature sources are constructed in the reviewed order `NVML -> MSI Afterburner`.

Implementation commit: `cd3d001` (`feat: compose LumaTherm runtime and diagnostics`).

## Witnessed RED -> GREEN evidence

- Logger/sentinel RED: focused build failed because `LumaTherm.Core.Diagnostics`, `RollingFileLogger`, and `FileSessionSentinel` did not exist.
- Logger GREEN: schema, UTF-8 JSONL, concurrency, byte rotation, four-archive retention, file failure, double-dispose, control sanitization, and sentinel tests passed.
- Oversized-record RED: the initial truncation loop hung on one-character exception fields; the bounded regression test reproduced it under a 20-second hang deadline.
- Oversized-record GREEN: progressive reduction reaches an empty field and safely emits a `truncated: true` record.
- Oversized event/data RED: a 20,111-byte line escaped the 256-byte limit.
- Oversized event/data GREEN: data is dropped first, then exception/message/event fields are progressively bounded; the record stays at or below 256 bytes and remains valid JSON.
- AppHost RED: composition contracts/root did not exist.
- AppHost GREEN: ownership gating, source/order construction, autostart, crash safe mode, warning, reverse cleanup, startup failure, activation dispatch, and idempotent stop passed.
- Concurrent-start RED: a second caller returned `false` while the first ownership call was pending.
- Concurrent-start GREEN: all callers share the same tracked start task; ownership and runtime start occur once.
- Stop-during-start RED: stop completed before ownership/start and could leave later-created resources alive.
- Stop-during-start GREEN: stop awaits the tracked startup operation and then performs one complete cleanup.
- Exception-boundary RED: boundary/source contracts did not exist.
- Exception-boundary GREEN: attach/detach exactly once, dispatcher handled state, task observation, one foreground/background notification, and one shutdown request passed.
- WPF full-suite RED: the existing resource-only STA fixture processed production `OnStartup` when its dispatcher was pumped, started real composition, and shut down the fixture application.
- WPF full-suite GREEN: the application has an internal resource-host constructor used by the fixture; the public constructor still starts production composition. The previously failing lifecycle test and the entire UI suite pass.
- Ownership/logger cleanup RED: acquisition failure leaked coordinator/logger; injected logger-dispose failure removed the sentinel too early.
- Ownership/logger cleanup GREEN: pre-ownership failures clean both resources without touching the sentinel, and sentinel removal now occurs only after all other cleanup including logger disposal succeeds.

## Lifecycle ordering

Startup is serialized and follows:

1. logger creation;
2. single-instance creation/acquisition;
3. non-owner `SHOW` signal and exit, with no sentinel/settings/device/tray creation;
4. sentinel detect/create;
5. settings store creation and load/validation;
6. runtime construction with `NVML -> MSI Afterburner`, `WindowsLampArrayPlatform`, and `LampArrayLightingController`;
7. crash recovery persistence through `IThermalRuntime.UpdateSettingsAsync` before UI publication;
8. startup service, ViewModels/window, optional window show, tray, power;
9. read-only LampArray discovery;
10. thermal runtime start.

Stop is one shared task. It waits for an in-flight start, then attempts runtime stop/release, discovery, power, tray, UI/ViewModels, single-instance, final stop logging/logger disposal, and finally removes the sentinel only when every preceding cleanup succeeded. Injected failures are aggregated after every step has been attempted.

## Sentinel matrix

| Scenario | Marker result | Mode result | Warning |
|---|---|---|---|
| First run | created, removed after graceful stop | default OFF | none |
| Clean enabled restart | created, removed after graceful stop | persisted ON restored | none |
| Prior marker/crash | replaced before runtime ownership; retained until graceful stop | persisted OFF through runtime/settings authority | once |
| Second instance | never created/deleted | no settings/runtime created | none; sends `SHOW` |
| Startup failure | retained | no automatic takeover | startup failure logged |
| Partial shutdown/logger failure | retained | next launch forced safe | aggregate returned |
| Repeated stop | no duplicate cleanup/delete | unchanged | none |

## Diagnostics evidence

- JSONL records contain offset timestamp, level, stable event name, message, optional exception/data, and optional truncation marker.
- Control characters are replaced; sensitive data keys containing password/secret/token/certificate are omitted; unknown CLI argument values are not logged.
- Writes are lock-serialized and concurrent test output parses as 200 complete independent JSON lines.
- Rotation uses UTF-8 byte length before append, retains current plus exactly `.1` through `.4`, and test records never exceed the 256-byte configured ceiling.
- File failures and writes after disposal do not escape; disposal is idempotent.
- Stable events exercised or wired: `app.start`, `app.stop`, `app.unhandled`, `sensor.selected`, `sensor.failed`, `lamp.connected`, `lamp.disconnected`, `runtime.enabled`, `runtime.disabled`, `settings.saved`, and `startup.changed`.

## Fresh verification

```text
dotnet test LumaTherm.sln --filter "FullyQualifiedName~RollingFileLoggerTests|FullyQualifiedName~AppHostTests|FullyQualifiedName~SessionSentinel" --no-restore -p:NuGetAudit=false
PASS: Infrastructure 9, App 15; 24 focused tests total, 0 failed.

dotnet test LumaTherm.sln --no-restore -p:NuGetAudit=false
PASS: Core 71 + Infrastructure 86 + App 126 = 283 tests, 0 failed.

dotnet build LumaTherm.sln -c Debug --no-restore -p:NuGetAudit=false
PASS: 0 warnings, 0 errors.

git diff --cached --check
PASS before commit; no whitespace errors.
```

## Self-review

- Verified no production hardware/tray/autostart/install side effects are invoked by tests; all host lifecycle tests use bounded fakes.
- Verified no `async void` was added. WPF startup is a tracked task; exit uses a bounded dispatcher pump so UI cleanup can complete without dispatcher deadlock.
- Verified settings persistence still has one authority: `ThermalRuntime`; logging is a decorator, not a second owner.
- Verified LampArray release/runtime stop is attempted before every downstream cleanup and before sentinel removal.
- Verified non-owner startup cannot create or mutate the session marker.
- Verified `AppServices` contains immutable factory delegates and exposes no mutable global service registry.

## Concerns / deferred acceptance

- Automated verification intentionally did not launch the production executable, write physical RGB, install a package, change real autostart, or exercise the real tray. Those are hardware/package acceptance gates for later tasks.
- Package identity/background Dynamic Lighting behavior remains dependent on the Task 12 MSIX manifest/signing work.
