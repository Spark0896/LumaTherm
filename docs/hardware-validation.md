# Hardware validation

## Read-only discovery

Timestamp: `2026-08-20T19:40:47.5837960+03:00`.

| Check | Status | Evidence |
| --- | --- | --- |
| Windows version | PASS | `Microsoft Windows NT 10.0.26200.0`, collected read-only with `[Environment]::OSVersion.VersionString`. |
| NVML GPU temperature | PASS | NVIDIA GeForce RTX 5070, 71°C, exit 0. |
| Forced MSI Afterburner fallback | FAIL (actionable) | MAHM provided no temperature; verify that MSI Afterburner is running and shared memory is enabled; exit 1. |
| Dynamic Lighting LampArray | PASS | `GIGABYTE Device`, `\\?\HID#VID_048D&PID_5702&MI_00#a&30f63cd2&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`, available, 1 lamp, exit 0. |
| Release SHA-256 | Pending controller build | The signing build is controller-gated and has not been run. |

Exact read-only evidence:

```json
{"status":"pass","provider":"NVML","gpu":"NVIDIA GeForce RTX 5070","temperatureC":71}
```

Exit code: `0` (`sensor --json`).

```json
{"status":"error","message":"MSI Afterburner \u043D\u0435 \u043F\u0440\u0435\u0434\u043E\u0441\u0442\u0430\u0432\u0438\u043B \u0442\u0435\u043C\u043F\u0435\u0440\u0430\u0442\u0443\u0440\u0443 GPU. \u0423\u0431\u0435\u0434\u0438\u0442\u0435\u0441\u044C, \u0447\u0442\u043E Afterburner \u0437\u0430\u043F\u0443\u0449\u0435\u043D \u0438 shared memory \u0432\u043A\u043B\u044E\u0447\u0435\u043D\u0430."}
```

Exit code: `1` (`sensor --skip-nvml --json`).

```json
{"status":"pass","devices":[{"id":"\\\\?\\HID#VID_048D&PID_5702&MI_00#a&30f63cd2&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}","name":"GIGABYTE Device","lampCount":1,"available":true}]}
```

Exit code: `0` (`lights --json`).

The smoke tool performs no lighting control for `sensor` or `lights`. `lights` discovers LampArray devices only; it does not connect, enable, disable, or set a color.

## Gated hardware acceptance

The following checks are deliberately pending explicit controller authorization and must not be interpreted as passed: physical cold/warm/hot color cycle, simulated smooth transition, GIGABYTE Control Center ownership restoration, package installation and first run, tray behavior, autostart/sign-out, sensor-loss recovery, suspend/resume, five-minute soak, uninstall, and final visual comparison.

Before any physical light write, state that LumaTherm will temporarily take Windows Dynamic Lighting control and will release it afterward, then obtain explicit user confirmation. If no LampArray is found, enable Windows Dynamic Lighting, prioritize LumaTherm above conflicting background controllers, and close only the RGB Fusion page if necessary before retrying discovery. Do not add unsupported Gigabyte HID writes.
