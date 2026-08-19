# Task 5 Report: LampArray Discovery, Control, and Write Throttling

## Status

Implemented Task 5 on `feature/lumatherm-v1` from base `feb6979`. Core remains BCL-only at `net8.0`; Infrastructure and its test host target exactly `net8.0-windows10.0.22621.0` with `TargetPlatformMinVersion` `10.0.22621.0`.

## Witnessed TDD Evidence

### RED

Tests were created before production lighting code. The first prescribed command was blocked during restore by sandbox denial of `C:\Users\User\AppData\Roaming\NuGet\NuGet.Config`; this was not accepted as TDD evidence. The valid RED was then witnessed using already-restored assets:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~Lighting"
```

Exit code: `1`. Expected feature-missing failures included:

- `CS0234`: `LumaTherm.Core.Lighting` does not exist.
- `CS0234`: `LumaTherm.Infrastructure.Lighting` does not exist.
- `CS0246`: `ILampArrayPlatform` and `ILampArrayHandle` could not be found.

After assigning Infrastructure the required Windows TFM, the portable Infrastructure test host correctly rejected its project reference. The test host was therefore updated to the same Windows TFM; Core and Core.Tests stayed portable.

### GREEN

After the minimal contracts, controller, platform adapter, and gate were implemented:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore --filter "FullyQualifiedName~Lighting"
```

Exit code: `0`. Results: Core Lighting `2/2`; Infrastructure Lighting `11/11`; total `13/13`. The real Windows adapter compiled as part of the focused run. Tests used only the complete in-memory `ILampArrayPlatform`/`ILampArrayHandle`; no test instantiated `WindowsLampArrayPlatform` or accessed physical lighting.

## Files and Implementation

- `src/LumaTherm.Core/Lighting/LightingDeviceInfo.cs`: stable discovery DTO.
- `src/LumaTherm.Core/Lighting/ILightingController.cs`: discovery, connection, color, release, event, and async-disposal contract.
- `src/LumaTherm.Core/Lighting/LightingCommandGate.cs`: deterministic duplicate suppression, literal 100 ms boundary behavior, and reset.
- `src/LumaTherm.Infrastructure/Lighting/ILampArrayPlatform.cs`: complete platform and handle seams, including platform lifetime ownership.
- `src/LumaTherm.Infrastructure/Lighting/LampArrayLightingController.cs`: lists every available device, selects exact saved ID then exact `GIGABYTE Device` name then VID/PID HID then first available, enables and routes color to the selected handle, disconnects on removal, disables before switching, and releases/disposes idempotently.
- `src/LumaTherm.Infrastructure/Lighting/WindowsLampArrayPlatform.cs`: owns a `DeviceWatcher`, forwards added/removed/updated discovery events, tracks removal availability safely across watcher callbacks, creates handles through `LampArray.FromIdAsync`, maps stable ID/name/count/availability, writes `Windows.UI.Color`, and stops/unsubscribes the watcher idempotently.
- `src/LumaTherm.Infrastructure/LumaTherm.Infrastructure.csproj`: exact Windows TFM and minimum platform version.
- `tests/LumaTherm.Infrastructure.Tests/LumaTherm.Infrastructure.Tests.csproj`: matching Windows test-host TFM required for the project reference.
- `tests/LumaTherm.Core.Tests/Lighting/LightingCommandGateTests.cs`: literal colors and timestamps for duplicate, 99 ms, 100 ms, and reset outcomes.
- `tests/LumaTherm.Infrastructure.Tests/Lighting/LampArrayLightingControllerTests.cs`: complete in-memory behavior tests for discovery, saved ID, name/HID/fallback priority, enable/color routing, no-device failure, removal/disconnect notification, disable-before-enable switching order, disconnected color failure, idempotent release, and idempotent disposal.

## Final Verification

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --no-restore
```

Exit code: `0`. Core `36/36`; Infrastructure `52/52`; total `88/88`, with no failures or skips.

```powershell
& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln --no-restore
```

Exit code: `0`; warnings `0`; errors `0`. This separately compiled the real Windows LampArray platform at the exact target TFM.

## Self-review

- Checked each binding ruling against a behavior assertion and independently derived literal outcome.
- Mutation review: changing duplicate/timing boundaries, any selection tier, selected color target/value, removal clearing, switch order, release count, or disposal count breaks at least one test.
- Controller policy and Windows API responsibilities remain separated: controller knows selection/lifecycle policy; adapter alone references WinRT.
- Connection state is protected from watcher-thread callbacks, connection/write/release/disposal operations are serialized, and watcher subscription/stop are idempotent.
- `git diff --check` reports no whitespace errors (only the repository's expected LF-to-CRLF checkout notices for modified project files).

## Concerns

- Physical LampArray behavior was intentionally not exercised. Runtime validation against `VID_048D&PID_5702` remains a manual hardware step.
- `DevicesChanged` is forwarded on the DeviceWatcher callback thread; a future UI subscriber must marshal to its dispatcher, as is standard for infrastructure events.
