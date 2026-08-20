# Task 10 report — Tray, autostart, single instance, and power events

## Result

Implemented Task 10 on reviewed Task 9 base `fb5ec19`. Production/tests commit:

- `2860073a304e8d18bec1945a7082742d22afcb1c` — `feat: add tray autostart and lifecycle services`

No Task 11 composition root, logger implementation, package manifest, installer, GCC/RGB Fusion integration, raw HID access, vendor DLL dependency, or hardware write was added.

## Witnessed RED → GREEN evidence

1. **Autostart**
   - RED: `StartupServiceTests` failed compilation because `LumaTherm.Infrastructure.System`, `StartupTaskState`, and all platform seams/services did not exist.
   - GREEN: 9 tests passed for packaged state mappings, `EnabledByPolicy`, safe policy denial, cancellation before side effects, exact portable command, idempotent named-value deletion, packaged/portable selection, unrelated identity failure propagation, and default OFF/no constructor side effect.
2. **Single instance**
   - RED: `SingleInstanceCoordinatorTests` failed compilation because `SingleInstanceCoordinator` did not exist.
   - GREEN: 5 bounded tests passed for first/second ownership, `SHOW\n` activation, malformed/oversized rejection, serialized repeated activation, observed handler failure, cross-thread disposal/mutex release, and no callbacks after disposal.
3. **Power events**
   - RED: `PowerEventServiceTests` failed compilation because `IPowerEventSource`, `PowerEventKind`, and `PowerEventService` did not exist.
   - GREEN: ordered suspend/resume, non-overlap, failure observation/continuation, unsubscribe-first drain, idempotent dispose, and bounded cancellation of a stuck runtime all passed (4 tests).
4. **Tray and close policy**
   - RED: tray/close tests failed compilation because `TrayIconService`, platform/window/application seams, menu state, and `WindowClosePolicy` did not exist.
   - GREEN: 16 focused cases passed for the four exact menu entries, Russian labels, dynamic tooltip, surrogate-safe truncation, notifications/rearm/suppression/recovery warning, show/restore/activate behavior, serialized toggle operations, observed errors, strict exit order, and idempotent disposal.
   - Additional RED: `MainWindowLifecycleTests` failed compilation because `MainWindow.ClosePolicy` did not exist; GREEN proves real WPF close/minimize hides to tray and explicit Exit permits final close.
   - Additional behavioral RED: a fake tray callback after explicit exit executed one toggle (`expected 0, actual 1`); GREEN now marks shutdown intent synchronously before queueing Exit and rejects later toggle callbacks.

## Temporal and concurrency contracts

- Named-mutex ownership occurs on a dedicated background thread; only that same thread releases it, even when async disposal continues elsewhere.
- Pipe input is current-user-only where supported, byte-bounded, strict UTF-8, and accepts only the exact line `SHOW`.
- Activation handlers run serially; exceptions are routed to the injected error sink and do not terminate the server.
- Power callbacks synchronously enqueue into a single-consumer channel. Runtime suspend/resume calls cannot overlap and preserve event order.
- Power disposal unsubscribes first, drains normally, and applies a finite cancellation deadline if a runtime call is stuck.
- Tray toggle/exit callbacks synchronously enqueue tracked operations into one consumer. Exit intent blocks re-entry; runtime stop is attempted before icon disposal, which occurs before shutdown is requested even when stop fails.
- Production tray UI mutations/disposal marshal to the captured UI synchronization context; the embedded icon is cloned before its resource stream closes.

## No-side-effect evidence

- All autostart tests inject fake StartupTask, package-identity, and registry adapters. They never instantiate `WindowsStartupTaskPlatform` or `WindowsRegistryRunPlatform`; fake registry reports zero real accesses.
- Power tests use a fake event source, never `Microsoft.Win32.SystemEvents`.
- Tray tests use fake icon/window/application seams, never a real `NotifyIcon`.
- Single-instance tests use unique GUID-scoped mutex/pipe names and finite timeouts.
- No test or manual verification enabled startup, wrote HKCU Run, created a real tray icon, subscribed to real system power events, changed lights, or touched GCC/RGB Fusion.

## Final verification

Executed from the worktree at production commit `2860073` with `--no-restore -p:NuGetAudit=false`:

- Exact focused command from Task 10 brief: **34 passed** (`18` Infrastructure + `16` App), `0` failed, `0` skipped.
- Full solution: **252 passed** (`71` Core + `76` Infrastructure + `105` App), `0` failed, `0` skipped.
- Debug build: **0 warnings, 0 errors**.
- `git diff --check`: clean.
- Worktree before report creation: clean.

## Self-review

- Autostart remains default OFF (`AppSettings.Default.IsAutostartEnabled == false`); constructors perform no enable/write operation.
- Portable command is exactly `"<executable>" --autostart`; disable deletes only the `LumaTherm` value.
- Package selection catches only the explicit identity-unavailable exception; unrelated failures propagate.
- Single-instance same-process recursive mutex acquisition is prevented by per-coordinator ownership threads.
- Dispose paths are idempotent; event subscriptions are removed before platform disposal and post-dispose callbacks are ignored.
- Tooltip length enforcement never returns a dangling high surrogate.
- Static scans found no `async void`, GCC/RGB Fusion dependency, vendor controller dependency, or raw HID path in Task 10 code.

## Concerns / handoff

- Task 11 still needs to compose these production adapters, provide the real logger/error sink, wire the ViewModel toggle delegate, and coordinate application-wide shutdown/activation. This was intentionally not done in Task 10.
- Real StartupTask, registry, NotifyIcon, SystemEvents, and packaged/unpackaged behavior were intentionally not exercised in this task; those side-effecting acceptance checks remain gated to later tasks.

## Fix round 1/5 — lifecycle failure hardening

Review range base: `da8b5c8`. Production/tests fix:

- `51c478e` — `fix: harden lifecycle failure handling`

### Findings addressed

1. **Held-open oversized activation client**
   - Verified RED: a client wrote 17 bytes without a newline and remained connected; a later valid `SignalActivationAsync` timed out after two seconds with `OperationCanceledException` because the only pipe server kept reading the oversized connection.
   - GREEN: the reader rejects and closes immediately when byte 17 arrives. The regression keeps the oversized client open while a later exact `SHOW` connects and activates within a bounded deadline.
2. **Throwing window callback**
   - Verified RED: `FakeWindow.Show()` threw `InvalidOperationException("window failed")` directly through `RaiseLeftClick` and the tray event callback.
   - GREEN: Open/left-click/double-click use the tracked serialized operation queue; the failure reaches the non-throwing error path and never escapes the callback thread.
3. **Exit cleanup failures**
   - Verified RED: when hide threw, icon dispose was skipped; when the error sink threw, shutdown was never requested and disposal surfaced `logger failed`.
   - GREEN: runtime stop, failure reporting, icon hide, icon dispose, and WPF shutdown use independent nested `try/finally` boundaries. Hide and dispose are each attempted exactly once; a throwing error sink is contained; shutdown remains last and unconditional. Tests assert exact temporal order and all observed failures.
4. **Bounded single-instance tests**
   - Structural RED audit found every acquire/signal call used `CancellationToken.None`, and two coordinators were not guaranteed cleanup after an early assertion.
   - GREEN: every potentially blocking acquire/signal uses a linked finite CTS; raw pipe helpers are also bounded; coordinator/client lifetime uses `await using` or `finally`, so failed assertions cannot leave unbounded operations/resources.
5. **Bounded tray toggle gate**
   - Structural RED audit found the first toggle awaited an unbounded `TaskCompletionSource`, released only after an assertion which could fail before cleanup.
   - GREEN: the gate uses a linked two-second token and is released in an outer `finally` before awaited service disposal.
6. **Registry path assertion (minor)**
   - The test now derives the expectation independently with the literal `@"Software\Microsoft\Windows\CurrentVersion\Run"` rather than the production constant.

### Fix-round verification

Executed with `--no-restore -p:NuGetAudit=false`:

- Exact Task 10 focused filter plus new regression cases: **38 passed** (`19` Infrastructure + `19` App), `0` failed, `0` skipped.
- Full solution: **256 passed** (`71` Core + `77` Infrastructure + `108` App), `0` failed, `0` skipped.
- Debug build: **0 warnings, 0 errors**.
- `git diff --check`: clean.
- Static self-review: no `async void`; no real registry/StartupTask/tray/SystemEvents/hardware side effects; no GCC/RGB Fusion, vendor controller, raw HID, or lighting-write dependency introduced.
