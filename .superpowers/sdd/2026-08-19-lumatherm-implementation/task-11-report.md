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

---

## Fix round 1 — lifecycle and crash-safety review

Implementation commit: `98e0a25b16f82756dc0142e035e188888922827c` (`fix: harden app lifecycle and crash safety`).

### Verified findings and witnessed RED -> GREEN

- **C1 dispatcher affinity:** RED first failed to compile without an awaitable dispatcher contract; the real STA regression then exposed tray warning and tray disposal on the test thread (`Actual 6`, dispatcher `Expected 9`). GREEN uses `WpfAppDispatcher`/`Application.Dispatcher.InvokeAsync` for UI, ViewModel, tray, power and discovery construction, window/warning display, tray disposal and UI disposal. The test delays ownership/settings asynchronously and verifies eight factory/show/warning/disposal observations on the real STA dispatcher.
- **C2 atomic sentinel:** RED failed against the missing injected file-system seam. GREEN establishes a durable target marker with `FileMode.CreateNew` before any metadata update. Existing crash evidence is never moved or deleted during refresh; deterministic failure leaves the marker present. The redesign creates no temporary/update artifact, so cancellation/failure cannot strand one.
- **C3 ownership overlap:** RED showed coordinator disposal preceding sentinel completion. GREEN holds single-instance ownership through marker deletion and logger finalization, then releases it last. The barrier regression starts the next marker at ownership release and proves the old session cannot subsequently delete it.
- **I1 exception boundary:** RED failed against the missing dispatcher dependency; route/shutdown exceptions and off-dispatcher routing were then exercised. A canceled-dispatch regression also reproduced a stuck reentrancy gate (`routed 0`, expected `1`). GREEN contains route/shutdown failures, marshals foreground/background notification work, observes faulted/canceled dispatch tasks, releases gates, and requests foreground shutdown once from `finally` semantics without a notification storm.
- **I2 queued SHOW:** RED executed the queued activation after stop (`ActivationCalls 1`). GREEN checks lifecycle both before enqueue and inside the callback, clears owned references, and contains synchronous dispatcher failures; queued callbacks cannot touch disposed UI.
- **I3 startup cancellation:** RED timed out a blocked ownership stop at 250 ms and a blocked discovery start at the bounded deadline. GREEN uses one host-owned linked startup CTS: concurrent starters share one task, Stop cancels blocked acquisition/discovery, awaits startup termination, then performs uncancelled safety cleanup. The four focused concurrency/cancellation tests pass without release fallbacks or hangs.
- **I4 recursive logging safety:** RED serialized nested `accessToken` value `must-not-leak`. GREEN recursively accepts bounded scalar/dictionary/enumerable structures, strips secret keys at every depth, sanitizes keys/strings/control characters, replaces unsupported DTOs with a type marker, and terminates cycles/depth/item overflow with explicit markers.
- **M1 final stop diagnostics:** RED order assertions showed success logging before sentinel finalization. GREEN logs success only after successful sentinel completion and logs incomplete before logger disposal when sentinel finalization fails. Logger/single-instance disposal failures restore the crash marker while ownership is still held and remain reported.
- **M2 artifact hygiene:** the sentinel no longer uses temporary files at all; injected metadata failure and cancellation-safe paths retain only the owned target marker.

### Lifecycle ordering after review

Startup ownership is acquired before sentinel/settings/hardware/tray work. After sentinel and settings, WPF-bound factories/display run on the application dispatcher; discovery and runtime start remain cancellation-aware. Stop cancels startup, awaits its termination, and then attempts every safety cleanup with `CancellationToken.None` in this order: runtime stop/release, discovery, power, dispatcher-bound tray disposal, dispatcher-bound UI/ViewModel disposal, sentinel finalization when all prior steps succeeded, final stop log/logger disposal, and single-instance release. If a post-sentinel logger/coordinator failure occurs, the marker is re-established before ownership release.

### Sentinel outcomes after review

| Path | Ownership while marker changes | Final marker |
|---|---|---|
| graceful stop | retained through deletion and final diagnostics | absent |
| runtime/discovery/power/tray/UI failure | retained; deletion skipped | present |
| sentinel metadata/finalization failure | retained; incomplete logged | present |
| logger or coordinator disposal failure after deletion | retained during marker restoration | present |
| next owner overlap | acquisition only after old finalization | new owner's marker remains |
| non-owner | never reaches sentinel factory | unchanged |

### Logger evidence

- Nested dictionary/list regression removes token/password values and sanitizes embedded NUL/SOH controls.
- Unsupported DTO property values are never reflected or serialized.
- Cycles produce `[cycle]`, excessive depth produces `[max-depth]`, and collections are capped at 64 items; output remains finite valid JSONL.
- Existing UTF-8 byte rotation, oversized-record truncation, concurrency, no-throw failure and idempotent-dispose coverage remains green.

### Fresh verification after all fixes

```text
dotnet test LumaTherm.sln --filter "FullyQualifiedName~RollingFileLoggerTests|FullyQualifiedName~AppHostTests|FullyQualifiedName~SessionSentinel" --no-restore -p:NuGetAudit=false
PASS: Infrastructure 13 + App 23 = 36 focused tests, 0 failed.

dotnet test LumaTherm.sln --no-restore -p:NuGetAudit=false
PASS: Core 71 + Infrastructure 90 + App 138 = 299 tests, 0 failed.

dotnet build LumaTherm.sln -c Debug --no-restore -p:NuGetAudit=false
PASS: 0 warnings, 0 errors.

git diff --check / git diff --cached --check
PASS: no whitespace errors.
```

### Fix-round self-review and concerns

- Construction/cleanup ordering was re-read against the Task 11 brief and all review findings; owned references are cleared and every cleanup attempt remains failure-aggregating.
- No `async void`, real install/autostart/tray action, physical hardware write, GCC/RGB Fusion dependency, vendor DLL integration, or raw-HID control was added. Production lighting remains direct Windows LampArray; temperature remains NVML primary with optional MSI Afterburner shared-memory fallback.
- Tests use fakes/temp files plus bounded real STA dispatcher threads only; all waits have explicit deadlines or cancellation.
- Remaining acceptance is intentionally hardware/package/manual UI validation in later gates; there is no unresolved lifecycle or startup-cancellation contract in this fix round.

---

## Fix round 2 — bounded non-generic dictionary normalization

Implementation commit: `2fbe2acd05ffb569112c07d721ada77f2bab7952` (`fix: bound dictionary log normalization`).

### Witnessed RED -> GREEN

- **RED:** `NonGenericDictionary_StopsEnumerationAtTheConfiguredItemBound` supplied a deterministic effectively-unbounded `IDictionary` whose enumerator throws after 65 `MoveNext` calls. The old implementation first copied the source into an unbounded intermediate list, reached the 66th call, the logger safely swallowed that failure, and no JSONL record was written (`File.Exists(path)` was false).
- **GREEN:** non-generic `IDictionary` values are normalized directly. Enumeration stops on the 65th successful entry boundary, emits `[truncated]: true`, filters nested `accessToken`, recursively normalizes accepted values, and writes valid bounded JSONL. No source-sized intermediate collection is allocated.

### Fresh verification

```text
RollingFileLoggerTests: 9 passed, 0 failed.

Task 11 focused filter:
Infrastructure 14 + App 23 = 37 passed, 0 failed.

Full solution:
Core 71 + Infrastructure 91 + App 138 = 300 passed, 0 failed.

Debug build:
0 warnings, 0 errors.

git diff --check / git diff --cached --check:
PASS, no whitespace errors.
```

### Self-review / concerns

- The same 64-item contract, secret-key filtering, string/key sanitization, recursion depth/cycle handling, and truncation marker semantics now apply to generic and non-generic dictionaries.
- The regression is bounded and cannot hang the suite: it throws immediately if production requests a 66th element.
- Scope is limited to logger normalization and its test. No application lifecycle, hardware, tray, autostart, LampArray, NVML/Afterburner, GCC/vendor DLL, or raw-HID behavior changed; no real side effects were run.
- No unresolved concern remains for this finding.
