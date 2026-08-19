# LumaTherm Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Создать устанавливаемое Windows x64-приложение LumaTherm, которое плавно управляет GIGABYTE LampArray по температуре NVIDIA GPU, работает в трее и имеет настраиваемую шкалу цветов и автозапуск.

**Architecture:** Решение разделено на чистое ядро, Windows-инфраструктуру и WPF-приложение. Все аппаратные API скрыты за интерфейсами, поэтому расчёт цвета, восстановление после ошибок и ViewModel тестируются без реального оборудования; реальные NVML, MSI Afterburner и LampArray подключаются только в композиции приложения.

**Tech Stack:** C# 12, .NET SDK 8.0.423, .NET Desktop/WPF, Windows.Devices.Lights LampArray, NVIDIA NVML, MSI Afterburner MAHM Shared Memory v2, xUnit v3, Microsoft.NET.Test.Sdk 18.8.1, coverlet.collector 10.0.1, Microsoft.Windows.SDK.BuildTools 10.0.26100.8249, MSIX.

**Spec:** `docs/superpowers/specs/2026-08-19-lumatherm-design.md`

## Global Constraints

- Target: Windows 11 x64; `net8.0-windows10.0.22621.0`; self-contained `win-x64` publish.
- Confirmed lighting device: `GIGABYTE Device`, HID `VID_048D&PID_5702`, B650 EAGLE AX.
- Confirmed GPU: NVIDIA GeForce RTX 5070; NVML is primary, `Global\\MAHMSharedMemory` is fallback.
- Do not install or depend on OpenRGB, SignalRGB, vendor firmware tools, or closed GIGABYTE DLLs.
- Defaults: 35 °C `#50C8FF`, 65 °C `#FFC64A`, 85 °C `#FF565D`, smoothing 0.8 s, sensor polling 500 ms, lighting writes at most 10 Hz.
- First-launch thermal mode and autostart are off; close-to-tray and error notifications are on.
- Disabling the mode, sleeping, or exiting must stop writes and release LampArray control.
- Every behavioral change starts with a failing automated test; every task ends with the listed verification and one focused commit.
- Production files remain focused; no source file should combine hardware access, state orchestration, and UI concerns.

## Planned File Structure

```text
LumaTherm.sln
global.json
Directory.Build.props
Directory.Packages.props
src/
  LumaTherm.Core/
    Colors/RgbColor.cs
    Colors/ThermalProfile.cs
    Colors/ColorEngine.cs
    Sensors/ITemperatureSource.cs
    Sensors/ITemperatureProvider.cs
    Sensors/TemperatureReading.cs
    Sensors/TemperatureProvider.cs
    Lighting/ILightingController.cs
    Lighting/LightingDeviceInfo.cs
    Lighting/LightingCommandGate.cs
    Runtime/RuntimeSnapshot.cs
    Runtime/IThermalRuntime.cs
    Runtime/ThermalRuntime.cs
    Settings/AppSettings.cs
    Settings/ISettingsStore.cs
    Diagnostics/IAppLogger.cs
    System/IStartupService.cs
  LumaTherm.Infrastructure/
    Sensors/NvmlApi.cs
    Sensors/NvmlTemperatureSource.cs
    Sensors/MahmSnapshotParser.cs
    Sensors/AfterburnerTemperatureSource.cs
    Lighting/ILampArrayPlatform.cs
    Lighting/WindowsLampArrayPlatform.cs
    Lighting/LampArrayLightingController.cs
    Settings/JsonSettingsStore.cs
    Diagnostics/RollingFileLogger.cs
    System/PackagedStartupService.cs
    System/SingleInstanceCoordinator.cs
    System/PowerEventService.cs
  LumaTherm.App/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Composition/AppHost.cs
    Assets/LogoGeometry.xaml
    Controls/TemperatureRing.xaml
    Controls/ThermalGradientBar.xaml
    Controls/TemperatureSparkline.xaml
    Services/ColorPickerService.cs
    Services/TrayIconService.cs
    ViewModels/ObservableObject.cs
    ViewModels/RelayCommand.cs
    ViewModels/MainViewModel.cs
    ViewModels/SettingsViewModel.cs
    Views/DashboardView.xaml
    Views/SettingsView.xaml
tests/
  LumaTherm.Core.Tests/
  LumaTherm.Infrastructure.Tests/
  LumaTherm.App.Tests/
tools/
  LumaTherm.AssetBuilder/
  LumaTherm.Smoke/
packaging/
  AppxManifest.xml
  Assets/
scripts/
  build-release.ps1
  install.ps1
  uninstall.ps1
docs/
  hardware-validation.md
```

---

### Task 1: Solution Bootstrap and Perceptual Color Engine

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `LumaTherm.sln`
- Create: `src/LumaTherm.Core/LumaTherm.Core.csproj`
- Create: `src/LumaTherm.Core/Colors/RgbColor.cs`
- Create: `src/LumaTherm.Core/Colors/ThermalProfile.cs`
- Create: `src/LumaTherm.Core/Colors/ColorEngine.cs`
- Create: `tests/LumaTherm.Core.Tests/LumaTherm.Core.Tests.csproj`
- Create: `tests/LumaTherm.Core.Tests/Colors/ColorEngineTests.cs`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `RgbColor(byte R, byte G, byte B)` with hex round-trip, `ThermalProfile`, `ColorEngine.Map(double)`, and `ColorEngine.Step(double, TimeSpan)`.
- Consumes: only BCL types; no Windows or UI dependencies.

- [ ] **Step 1: Pin the SDK, create the projects, and write the failing color tests**

Create `global.json` with SDK `8.0.423`, set nullable and warnings-as-errors in `Directory.Build.props`, and centralize these package versions in `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="coverlet.collector" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.8249" />
  </ItemGroup>
</Project>
```

Append `.dotnet/`, `TestResults/`, and `*.trx` to `.gitignore`. Add `LumaTherm.Core` and `LumaTherm.Core.Tests` to the solution. The test project references the core project and the four test packages.

Create `ColorEngineTests.cs`:

```csharp
using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Tests.Colors;

public sealed class ColorEngineTests
{
    private static readonly ThermalProfile Profile = ThermalProfile.Default;

    [Theory]
    [InlineData(0, 0x50, 0xC8, 0xFF)]
    [InlineData(35, 0x50, 0xC8, 0xFF)]
    [InlineData(65, 0xFF, 0xC6, 0x4A)]
    [InlineData(85, 0xFF, 0x56, 0x5D)]
    [InlineData(110, 0xFF, 0x56, 0x5D)]
    public void Map_ClampsAndHitsControlPoints(double temperature, byte r, byte g, byte b)
    {
        var engine = new ColorEngine(Profile, 35);

        Assert.Equal(new RgbColor(r, g, b), engine.Map(temperature));
    }

    [Fact]
    public void Step_IsIndependentOfTickSize()
    {
        var oneStep = new ColorEngine(Profile, 35);
        var eightSteps = new ColorEngine(Profile, 35);

        var expected = oneStep.Step(85, TimeSpan.FromSeconds(0.8));
        RgbColor actual = default;
        for (var i = 0; i < 8; i++)
        {
            actual = eightSteps.Step(85, TimeSpan.FromSeconds(0.1));
        }

        Assert.InRange(Math.Abs(expected.R - actual.R), 0, 1);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, 1);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, 1);
    }

    [Fact]
    public void Validate_RejectsCrossedTemperatures()
    {
        var invalid = Profile with { ColdTemperature = 65, WarmTemperature = 65 };

        var error = Assert.Throws<ArgumentException>(() => invalid.Validate());

        Assert.Contains("ColdTemperature < WarmTemperature < HotTemperature", error.Message);
    }

    [Fact]
    public void Validate_RequiresOneDegreeBetweenControlPoints()
    {
        var invalid = Profile with { ColdTemperature = 64.5, WarmTemperature = 65 };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Theory]
    [InlineData("#50C8FF", 0x50, 0xC8, 0xFF)]
    [InlineData("50c8ff", 0x50, 0xC8, 0xFF)]
    public void Hex_RoundTrips(string text, byte r, byte g, byte b)
    {
        Assert.True(RgbColor.TryParseHex(text, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
        Assert.Equal("#50C8FF", color.ToHex());
    }

    [Fact]
    public void Map_UsesShortestHueArcAcrossZeroDegrees()
    {
        var profile = new ThermalProfile(0, new(255, 0, 43), 100, new(255, 43, 0), 120, new(255, 0, 0), 0.8);

        var middle = new ColorEngine(profile, 0).Map(50);

        Assert.Equal(new RgbColor(255, 0, 0), middle);
    }
}
```

- [ ] **Step 2: Run the tests and verify the red state**

Install the SDK locally, then run the focused project:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install-lumatherm.ps1"
& "$env:TEMP\dotnet-install-lumatherm.ps1" -Version 8.0.423 -InstallDir "$PWD\.dotnet"
& "$PWD\.dotnet\dotnet.exe" restore LumaTherm.sln
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --no-restore
```

Expected: FAIL at compile time because `RgbColor`, `ThermalProfile`, and `ColorEngine` do not exist.

- [ ] **Step 3: Implement the immutable color types, validation, HSV interpolation, and time-based smoothing**

Use these public contracts:

```csharp
using System.Globalization;

namespace LumaTherm.Core.Colors;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool TryParseHex(string? value, out RgbColor color)
    {
        color = default;
        if (value is null) return false;
        var digits = value.AsSpan();
        if (!digits.IsEmpty && digits[0] == '#') digits = digits[1..];
        if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}

public sealed record ThermalProfile(
    double ColdTemperature,
    RgbColor ColdColor,
    double WarmTemperature,
    RgbColor WarmColor,
    double HotTemperature,
    RgbColor HotColor,
    double SmoothingSeconds)
{
    public static ThermalProfile Default { get; } = new(
        35, new RgbColor(0x50, 0xC8, 0xFF),
        65, new RgbColor(0xFF, 0xC6, 0x4A),
        85, new RgbColor(0xFF, 0x56, 0x5D),
        0.8);

    public ThermalProfile Validate()
    {
        if (ColdTemperature is < 0 or > 120 || WarmTemperature is < 0 or > 120 || HotTemperature is < 0 or > 120)
            throw new ArgumentException("Temperatures must be between 0 and 120 °C.");
        if (WarmTemperature - ColdTemperature < 1 || HotTemperature - WarmTemperature < 1)
            throw new ArgumentException("Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points.");
        if (SmoothingSeconds is < 0.1 or > 5.0)
            throw new ArgumentException("SmoothingSeconds must be between 0.1 and 5.0.");
        return this;
    }
}
```

`RgbColor.TryParseHex` accepts exactly six hexadecimal digits with one optional leading `#`, uses invariant `NumberStyles.HexNumber`, and returns `false` rather than throwing for malformed input.

`ColorEngine` stores `_smoothedTemperature`; `Step` must use `alpha = 1 - Math.Exp(-elapsed.TotalSeconds / Profile.SmoothingSeconds)` and then call `Map`. `Map` clamps the input, selects cold→warm or warm→hot, converts endpoints to HSV, interpolates hue by `delta = ((h2 - h1 + 540) % 360) - 180`, and rounds RGB channels with `MidpointRounding.AwayFromZero`. Keep RGB↔HSV conversion private and deterministic.

- [ ] **Step 4: Run core tests and coverage**

Run:

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --collect:"XPlat Code Coverage"
```

Expected: all color tests PASS and a Cobertura coverage file appears under `TestResults`.

- [ ] **Step 5: Commit the color engine**

```powershell
git add .gitignore global.json Directory.Build.props Directory.Packages.props LumaTherm.sln src/LumaTherm.Core tests/LumaTherm.Core.Tests
git commit -m "feat: add thermal color engine"
```

---

### Task 2: Versioned Settings and Atomic Persistence

**Files:**
- Create: `src/LumaTherm.Core/Settings/AppSettings.cs`
- Create: `src/LumaTherm.Core/Settings/ISettingsStore.cs`
- Create: `src/LumaTherm.Infrastructure/LumaTherm.Infrastructure.csproj`
- Create: `src/LumaTherm.Infrastructure/Settings/JsonSettingsStore.cs`
- Create: `src/LumaTherm.Infrastructure/Settings/SettingsMigrator.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/LumaTherm.Infrastructure.Tests.csproj`
- Create: `tests/LumaTherm.Infrastructure.Tests/Settings/JsonSettingsStoreTests.cs`
- Modify: `LumaTherm.sln`

**Interfaces:**
- Consumes: `ThermalProfile` from Task 1.
- Produces: `AppSettings.Default`, `SettingsLoadResult`, `ISettingsStore.LoadAsync`, `ISettingsStore.SaveAsync`, and `JsonSettingsStore`.

- [ ] **Step 1: Write tests for defaults, round-trip persistence, and corrupt-file recovery**

Create the infrastructure project referencing core, create the test project referencing both, and write:

```csharp
using LumaTherm.Core.Settings;
using LumaTherm.Infrastructure.Settings;

namespace LumaTherm.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"LumaTherm-{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingFile_ReturnsSafeDefaults()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"), TimeProvider.System);

        var result = await store.LoadAsync(CancellationToken.None);
        var settings = result.Settings;

        Assert.False(settings.IsModeEnabled);
        Assert.False(settings.IsAutostartEnabled);
        Assert.True(settings.MinimizeToTray);
        Assert.Equal(35, settings.Profile.ColdTemperature);
        Assert.Null(result.RecoveryMessage);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllValues()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path, TimeProvider.System);
        var expected = AppSettings.Default with { IsModeEnabled = true, NotificationsEnabled = false };

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = (await store.LoadAsync(CancellationToken.None)).Settings;

        Assert.Equal(expected, actual);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task InvalidJson_IsQuarantinedAndDefaultsAreReturned()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{invalid");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var store = new JsonSettingsStore(path, clock);

        var result = await store.LoadAsync(CancellationToken.None);
        var settings = result.Settings;

        Assert.Equal(AppSettings.Default, settings);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.corrupt-20260819-120000.json")));
        Assert.Equal("Настройки были повреждены и сброшены", result.RecoveryMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
```

Add a 15-line `FakeTimeProvider` in the test file that overrides `GetUtcNow()` and returns the constructor value.

Add `SchemaZero_IsMigratedToCurrentProfile`: write a legacy flat JSON object with `schemaVersion: 0`, `coldTemperature`, `coldColor`, `warmTemperature`, `warmColor`, `hotTemperature`, `hotColor`, `smoothingSeconds`, and behavior flags; assert it loads as schema 1, preserves all values, returns no recovery warning, and is rewritten in the current nested-profile format on the next save. Add an unknown-schema test that quarantines safely instead of guessing.

- [ ] **Step 2: Run the settings tests and confirm failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Infrastructure.Tests\LumaTherm.Infrastructure.Tests.csproj --filter FullyQualifiedName~JsonSettingsStoreTests
```

Expected: FAIL because the settings contracts and store do not exist.

- [ ] **Step 3: Implement the settings contract and atomic JSON store**

Create these contracts:

```csharp
using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Settings;

public sealed record AppSettings(
    int SchemaVersion,
    ThermalProfile Profile,
    bool IsModeEnabled,
    bool IsAutostartEnabled,
    bool MinimizeToTray,
    bool NotificationsEnabled,
    string? PreferredLightingDeviceId)
{
    public static AppSettings Default { get; } = new(1, ThermalProfile.Default, false, false, true, true, null);

    public AppSettings Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException($"Unsupported settings schema {SchemaVersion}.");
        Profile.Validate();
        return this;
    }
}

public interface ISettingsStore
{
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public sealed record SettingsLoadResult(AppSettings Settings, string? RecoveryMessage = null);
```

`SettingsMigrator` first reads `schemaVersion` from `JsonDocument`. Version 1 deserializes directly; version 0 maps the documented flat color/temperature fields into a nested `ThermalProfile`, parses colors only through `RgbColor.TryParseHex`, applies missing behavior flags from `AppSettings.Default`, validates, and returns schema 1. Unknown versions throw `InvalidDataException`.

`JsonSettingsStore` must use `JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true }`, create the parent directory, write to `<path>.tmp` with `FileOptions.WriteThrough`, call `FlushAsync`, then use `File.Replace` when the destination exists and `File.Move` otherwise. Load through `SettingsMigrator`. Catch only `JsonException`, `InvalidDataException`, and `IOException` during load; quarantine an existing invalid file using the injected UTC timestamp before returning defaults plus the one-shot Russian `RecoveryMessage`. Missing files return defaults without a warning.

- [ ] **Step 4: Run focused and solution tests**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Infrastructure.Tests\LumaTherm.Infrastructure.Tests.csproj --filter FullyQualifiedName~JsonSettingsStoreTests
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln
```

Expected: all tests PASS; no temporary settings file remains.

- [ ] **Step 5: Commit settings persistence**

```powershell
git add LumaTherm.sln src/LumaTherm.Core/Settings src/LumaTherm.Infrastructure tests/LumaTherm.Infrastructure.Tests
git commit -m "feat: persist versioned settings atomically"
```

---

### Task 3: NVIDIA NVML Temperature Source

**Files:**
- Create: `src/LumaTherm.Core/Sensors/TemperatureReading.cs`
- Create: `src/LumaTherm.Core/Sensors/ITemperatureSource.cs`
- Create: `src/LumaTherm.Infrastructure/Sensors/INvmlApi.cs`
- Create: `src/LumaTherm.Infrastructure/Sensors/NvmlApi.cs`
- Create: `src/LumaTherm.Infrastructure/Sensors/NvmlTemperatureSource.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/Sensors/NvmlTemperatureSourceTests.cs`

**Interfaces:**
- Produces: `TemperatureReading`, `ITemperatureSource.TryReadAsync`, `NvmlTemperatureSource`.
- Consumes: installed NVIDIA `nvml.dll`; no process spawning and no dependency on `nvidia-smi.exe`.

- [ ] **Step 1: Write NVML source tests against a fake native API**

```csharp
using LumaTherm.Infrastructure.Sensors;

namespace LumaTherm.Infrastructure.Tests.Sensors;

public sealed class NvmlTemperatureSourceTests
{
    [Fact]
    public async Task TryRead_ReturnsGpuTemperatureAndName()
    {
        var api = new FakeNvmlApi(0, 68, "NVIDIA GeForce RTX 5070");
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        var reading = await source.TryReadAsync(CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(68, reading.Celsius);
        Assert.Equal("NVML", reading.SourceName);
        Assert.Equal("NVIDIA GeForce RTX 5070", reading.DeviceName);
    }

    [Fact]
    public async Task TryRead_WhenInitializationFails_ReturnsNullWithoutThrowing()
    {
        var api = new FakeNvmlApi(3, 0, string.Empty);
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
        Assert.Equal(1, api.InitializeCalls);
    }

    [Fact]
    public async Task TryRead_RetriesInitializationAfterTransientFailure()
    {
        var api = new RecoveringFakeNvmlApi(firstInitializeResult: 3, temperature: 67);
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
        Assert.NotNull(await source.TryReadAsync(CancellationToken.None));
        Assert.Equal(2, api.InitializeCalls);
    }
}
```

`FakeNvmlApi` implements the exact `INvmlApi` methods from Step 3 and returns a stable fake handle `(nint)42`.

- [ ] **Step 2: Run the tests and verify they fail**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Infrastructure.Tests\LumaTherm.Infrastructure.Tests.csproj --filter FullyQualifiedName~NvmlTemperatureSourceTests
```

Expected: FAIL because sensor contracts and NVML implementation do not exist.

- [ ] **Step 3: Implement the managed contracts and NVML P/Invoke adapter**

Create:

```csharp
namespace LumaTherm.Core.Sensors;

public sealed record TemperatureReading(double Celsius, string SourceName, string DeviceName, DateTimeOffset Timestamp);

public interface ITemperatureSource : IAsyncDisposable
{
    string Name { get; }
    ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken);
}
```

Use this native seam:

```csharp
namespace LumaTherm.Infrastructure.Sensors;

public interface INvmlApi
{
    int Initialize();
    int Shutdown();
    int GetDeviceHandle(uint index, out nint handle);
    int GetDeviceName(nint handle, byte[] buffer);
    int GetTemperature(nint handle, out uint temperature);
}
```

`NvmlApi` uses `[DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]` for `nvmlInit_v2`, `nvmlShutdown`, `nvmlDeviceGetHandleByIndex_v2`, `nvmlDeviceGetName`, and `nvmlDeviceGetTemperature` with sensor type `0`. `NvmlTemperatureSource` attempts initialization whenever it is not currently initialized, obtains GPU index 0, decodes a 96-byte null-terminated UTF-8 name, rejects temperatures over 120, and calls `Shutdown` exactly once from `DisposeAsync` after successful initialization. A failed initialization is not cached permanently, so later polls can recover. Native error codes, `DllNotFoundException`, `EntryPointNotFoundException`, and `BadImageFormatException` return `null`.

- [ ] **Step 4: Run sensor and full tests**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Infrastructure.Tests\LumaTherm.Infrastructure.Tests.csproj --filter FullyQualifiedName~NvmlTemperatureSourceTests
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln
```

Expected: all tests PASS.

- [ ] **Step 5: Commit NVML monitoring**

```powershell
git add src/LumaTherm.Core/Sensors src/LumaTherm.Infrastructure/Sensors tests/LumaTherm.Infrastructure.Tests/Sensors
git commit -m "feat: read NVIDIA temperature through NVML"
```

---

### Task 4: MSI Afterburner Shared-Memory Fallback and Source Failover

**Files:**
- Create: `src/LumaTherm.Core/Sensors/ITemperatureProvider.cs`
- Create: `src/LumaTherm.Core/Sensors/TemperatureProvider.cs`
- Create: `src/LumaTherm.Infrastructure/Sensors/IMahmMemoryReader.cs`
- Create: `src/LumaTherm.Infrastructure/Sensors/MahmMemoryReader.cs`
- Create: `src/LumaTherm.Infrastructure/Sensors/MahmSnapshotParser.cs`
- Create: `src/LumaTherm.Infrastructure/Sensors/AfterburnerTemperatureSource.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/Sensors/MahmSnapshotParserTests.cs`
- Create: `tests/LumaTherm.Core.Tests/Sensors/TemperatureProviderTests.cs`

**Interfaces:**
- Consumes: `ITemperatureSource` and `TemperatureReading` from Task 3.
- Produces: `MahmSnapshotParser.TryParseGpuTemperature`, `AfterburnerTemperatureSource`, `ITemperatureProvider`, and ordered `TemperatureProvider.TryReadAsync`.

- [ ] **Step 1: Write binary-format and failover tests**

Build a byte array that matches the installed official `MAHMSharedMemory.h`: 32-byte header, 1324-byte monitoring entry, and 1304-byte GPU entry. The header fields at offsets 0..31 are signature `0x4D48414D`, version `0x00020000`, header size, entry count, entry size, 32-bit polling time, GPU count, and GPU entry size. The monitoring entry uses `data` offset 1300, `dwGpu` offset 1316, and `dwSrcId` offset 1320.

```csharp
using System.Buffers.Binary;
using System.Text;
using LumaTherm.Infrastructure.Sensors;

namespace LumaTherm.Infrastructure.Tests.Sensors;

public sealed class MahmSnapshotParserTests
{
    [Fact]
    public void Parse_ReturnsGpuZeroTemperatureAndDeviceName()
    {
        var buffer = new byte[32 + 1324 + 1304];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), 0x4D48414D);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 0x00020000);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), 1324);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), 1304);
        WriteSingle(buffer, 32 + 1300, 68f);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32 + 1316), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32 + 1320), 0);
        Encoding.ASCII.GetBytes("NVIDIA GeForce RTX 5070\0").CopyTo(buffer, 32 + 1324 + 520);

        var parsed = MahmSnapshotParser.TryParseGpuTemperature(buffer, out var temperature, out var deviceName);

        Assert.True(parsed);
        Assert.Equal(68, temperature);
        Assert.Equal("NVIDIA GeForce RTX 5070", deviceName);
    }

    [Fact]
    public void Parse_RejectsDeallocatedSignature()
    {
        var buffer = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0x0000DEAD);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    private static void WriteSingle(byte[] buffer, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), BitConverter.SingleToInt32Bits(value));
}
```

Add `TemperatureProviderTests` with two fake sources: the first returns `null`, the second returns 67 °C. Assert the result source is `MSI Afterburner`; then make the first return 68 °C and assert the next poll immediately returns `NVML`.

- [ ] **Step 2: Run both focused test classes and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Infrastructure.Tests\LumaTherm.Infrastructure.Tests.csproj --filter FullyQualifiedName~MahmSnapshotParserTests
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --filter FullyQualifiedName~TemperatureProviderTests
```

Expected: FAIL because parser, fallback source, and provider do not exist.

- [ ] **Step 3: Implement bounded shared-memory reads and exact MAHM parsing**

Use these contracts:

```csharp
namespace LumaTherm.Infrastructure.Sensors;

public interface IMahmMemoryReader
{
    bool TryRead(out byte[] snapshot);
}

public static class MahmSnapshotParser
{
    public const uint ValidSignature = 0x4D48414D;
    public const uint MinimumVersion = 0x00020000;
    public const uint GpuTemperatureSourceId = 0;
    public static bool TryParseGpuTemperature(ReadOnlySpan<byte> snapshot, out double temperature, out string deviceName);
}
```

`MahmMemoryReader` opens `MemoryMappedFile.OpenExisting("Global\\MAHMSharedMemory", MemoryMappedFileRights.Read)`, reads 32 bytes, validates each size/count with checked arithmetic, rejects a total snapshot over 4 MiB, then copies exactly `headerSize + numEntries*entrySize + numGpuEntries*gpuEntrySize` bytes. It catches `FileNotFoundException`, `UnauthorizedAccessException`, and `IOException` and returns `false`.

`MahmSnapshotParser` must reject short buffers, invalid signature/version, entry size below 1324, GPU entry size below 1304, `float.MaxValue`, non-finite values, temperatures outside 0..120, non-zero GPU index, and any source ID other than `MONITORING_SOURCE_ID_GPU_TEMPERATURE` (`0`). Decode ASCII strings up to the first zero byte. Read the GPU description from offset 520 of GPU entry zero.

`AfterburnerTemperatureSource.TryReadAsync` returns a reading named `MSI Afterburner` with the injected `TimeProvider.GetUtcNow()`.

- [ ] **Step 4: Implement ordered failover and run all sensor tests**

```csharp
using LumaTherm.Core.Sensors;

namespace LumaTherm.Core.Sensors;

public interface ITemperatureProvider : IAsyncDisposable
{
    ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken);
}

public sealed class TemperatureProvider : ITemperatureProvider
{
    private readonly IReadOnlyList<ITemperatureSource> _sources;

    public TemperatureProvider(IReadOnlyList<ITemperatureSource> sources) =>
        _sources = sources.Count > 0 ? sources : throw new ArgumentException("At least one source is required.", nameof(sources));

    public async ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            var reading = await source.TryReadAsync(cancellationToken);
            if (reading is not null) return reading;
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources) await source.DisposeAsync();
    }
}
```

Run:

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --filter "FullyQualifiedName~Sensors"
```

Expected: parser, fallback, NVML, and failover tests PASS.

- [ ] **Step 5: Commit sensor fallback**

```powershell
git add src/LumaTherm.Core/Sensors src/LumaTherm.Infrastructure/Sensors tests/LumaTherm.Core.Tests/Sensors tests/LumaTherm.Infrastructure.Tests/Sensors
git commit -m "feat: add Afterburner temperature fallback"
```

---

### Task 5: LampArray Discovery, Control, and Write Throttling

**Files:**
- Create: `src/LumaTherm.Core/Lighting/LightingDeviceInfo.cs`
- Create: `src/LumaTherm.Core/Lighting/ILightingController.cs`
- Create: `src/LumaTherm.Core/Lighting/LightingCommandGate.cs`
- Create: `src/LumaTherm.Infrastructure/Lighting/ILampArrayPlatform.cs`
- Create: `src/LumaTherm.Infrastructure/Lighting/WindowsLampArrayPlatform.cs`
- Create: `src/LumaTherm.Infrastructure/Lighting/LampArrayLightingController.cs`
- Create: `tests/LumaTherm.Core.Tests/Lighting/LightingCommandGateTests.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/Lighting/LampArrayLightingControllerTests.cs`
- Modify: `src/LumaTherm.Infrastructure/LumaTherm.Infrastructure.csproj`

**Interfaces:**
- Consumes: `RgbColor` from Task 1 and Windows `LampArray` APIs.
- Produces: `ILightingController`, `LampArrayLightingController`, discovery events, and a deterministic 10 Hz gate.

- [ ] **Step 1: Write rate-limit and device-selection tests**

```csharp
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;

namespace LumaTherm.Core.Tests.Lighting;

public sealed class LightingCommandGateTests
{
    [Fact]
    public void ShouldSend_SuppressesDuplicatesAndSub100MillisecondUpdates()
    {
        var gate = new LightingCommandGate(TimeSpan.FromMilliseconds(100));
        var start = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Assert.True(gate.ShouldSend(new RgbColor(1, 2, 3), start));
        Assert.False(gate.ShouldSend(new RgbColor(1, 2, 3), start.AddSeconds(1)));
        Assert.False(gate.ShouldSend(new RgbColor(2, 3, 4), start.AddMilliseconds(99)));
        Assert.True(gate.ShouldSend(new RgbColor(2, 3, 4), start.AddMilliseconds(100)));
    }
}
```

In infrastructure tests, provide a fake platform containing `Other Device` and `GIGABYTE Device`. Assert that `ConnectAsync(null)` chooses GIGABYTE, `SetColorAsync` calls only that fake handle, a `Removed` event clears `IsConnected`, and `ReleaseAsync` calls `Disable` exactly once.

- [ ] **Step 2: Run focused lighting tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --filter "FullyQualifiedName~Lighting"
```

Expected: FAIL because the lighting contracts and adapters do not exist.

- [ ] **Step 3: Implement core lighting contracts and command gate**

```csharp
using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Lighting;

public sealed record LightingDeviceInfo(string Id, string Name, int LampCount, bool IsAvailable);

public interface ILightingController : IAsyncDisposable
{
    bool IsConnected { get; }
    LightingDeviceInfo? ConnectedDevice { get; }
    event EventHandler? DevicesChanged;
    Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken);
    Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken);
    Task SetColorAsync(RgbColor color, CancellationToken cancellationToken);
    Task ReleaseAsync(CancellationToken cancellationToken);
}

public sealed class LightingCommandGate
{
    private readonly TimeSpan _minimumInterval;
    private DateTimeOffset? _lastSentAt;
    private RgbColor? _lastColor;

    public LightingCommandGate(TimeSpan minimumInterval) => _minimumInterval = minimumInterval;

    public bool ShouldSend(RgbColor color, DateTimeOffset now)
    {
        if (_lastColor == color) return false;
        if (_lastSentAt is { } sent && now - sent < _minimumInterval) return false;
        _lastColor = color;
        _lastSentAt = now;
        return true;
    }

    public void Reset() { _lastColor = null; _lastSentAt = null; }
}
```

- [ ] **Step 4: Wrap Windows LampArray and implement selection/release**

Define `ILampArrayPlatform` with `event EventHandler? DevicesChanged`, `Task<IReadOnlyList<ILampArrayHandle>> FindAllAsync`, and `ILampArrayHandle` properties `Id`, `Name`, `LampCount`, `IsAvailable`, plus `Enable()`, `SetColor(RgbColor)`, and `Disable()`.

`WindowsLampArrayPlatform` creates a `DeviceWatcher` from `LampArray.GetDeviceSelector()`, raises `DevicesChanged` on added/removed/updated, uses `LampArray.FromIdAsync`, maps availability/count, calls `SetColor(Windows.UI.Color.FromArgb(255, r, g, b))`, and toggles `IsEnabled` in `Enable`/`Disable`.

`LampArrayLightingController.ConnectAsync` selects in this order: exact preferred ID, name equal to `GIGABYTE Device`, ID containing both `VID_048D` and `PID_5702`, first available device. It unsubscribes and disables the prior handle before switching. `SetColorAsync` throws `InvalidOperationException` when disconnected; release is idempotent.

Add to the infrastructure project:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
  <TargetPlatformMinVersion>10.0.22621.0</TargetPlatformMinVersion>
</PropertyGroup>
```

- [ ] **Step 5: Run the solution and commit lighting support**

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln
git add src/LumaTherm.Core/Lighting src/LumaTherm.Infrastructure/Lighting src/LumaTherm.Infrastructure/LumaTherm.Infrastructure.csproj tests
git commit -m "feat: control Windows LampArray devices"
```

Expected: all tests PASS and no test touches physical lighting.

---

### Task 6: Thermal Runtime, Recovery, and Power-Safe State Machine

**Files:**
- Create: `src/LumaTherm.Core/Runtime/RuntimeStatus.cs`
- Create: `src/LumaTherm.Core/Runtime/RuntimeSnapshot.cs`
- Create: `src/LumaTherm.Core/Runtime/IThermalRuntime.cs`
- Create: `src/LumaTherm.Core/Runtime/ThermalRuntime.cs`
- Create: `tests/LumaTherm.Core.Tests/Runtime/ThermalRuntimeTests.cs`

**Interfaces:**
- Consumes: `ITemperatureProvider`, `ColorEngine`, `ILightingController`, `AppSettings`, and `ISettingsStore`.
- Produces: `IThermalRuntime` plus `ThermalRuntime.StartAsync`, `SetModeEnabledAsync`, `UpdateSettingsAsync`, `SuspendAsync`, `ResumeAsync`, `StopAsync`, and `SnapshotChanged`.

- [ ] **Step 1: Write state-machine tests for normal operation, five-second hold, and release**

Use fake sources, fake lighting, and `FakeTimeProvider`. In addition to the examples below, add `OneSecondRamp_ProducesTenGradualWritesBetweenSamples`: after a cold sample, advance the fake clock in 100 ms increments toward a hot target and assert multiple distinct intermediate colors, no jump directly to red, temperature source reads no more than every 500 ms, and lighting receives no more than ten writes per second.

```csharp
using LumaTherm.Core.Runtime;

namespace LumaTherm.Core.Tests.Runtime;

public sealed class ThermalRuntimeTests
{
    [Fact]
    public async Task ValidReading_WhenEnabled_WritesCalculatedColor()
    {
        var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);

        var snapshot = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.Active, snapshot.Status);
        Assert.Single(fixture.Lighting.Colors);
        Assert.Equal(68, snapshot.Temperature!.Celsius);
    }

    [Fact]
    public async Task MissingReading_HoldsForFiveSecondsThenReleases()
    {
        var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68, null, null]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        var holding = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var released = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.HoldingLastColor, holding.Status);
        Assert.Equal(RuntimeStatus.SensorUnavailable, released.Status);
        Assert.Equal(1, fixture.Lighting.ReleaseCalls);
    }

    [Fact]
    public async Task DisableMode_ReleasesImmediatelyAndPersistsState()
    {
        var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);

        await fixture.Runtime.SetModeEnabledAsync(false, CancellationToken.None);

        Assert.Equal(1, fixture.Lighting.ReleaseCalls);
        Assert.False(fixture.SettingsStore.Saved!.IsModeEnabled);
    }
}
```

`RuntimeFixture` creates deterministic fake implementations and exposes `Runtime`, `Clock`, `Lighting`, and `SettingsStore`.

- [ ] **Step 2: Run runtime tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --filter FullyQualifiedName~ThermalRuntimeTests
```

Expected: FAIL because runtime types do not exist.

- [ ] **Step 3: Implement snapshots and one-cycle behavior**

```csharp
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;

namespace LumaTherm.Core.Runtime;

public enum RuntimeStatus { Disabled, Connecting, Active, HoldingLastColor, SensorUnavailable, LightingUnavailable, Suspended, Faulted }

public sealed record RuntimeSnapshot(
    RuntimeStatus Status,
    TemperatureReading? Temperature,
    RgbColor? Color,
    LightingDeviceInfo? LightingDevice,
    string? Message,
    DateTimeOffset Timestamp);

public interface IThermalRuntime : IAsyncDisposable
{
    event EventHandler<RuntimeSnapshot>? SnapshotChanged;
    RuntimeSnapshot CurrentSnapshot { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken);
    Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken);
    Task SuspendAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

`ThermalRuntime.ProcessOnceAsync` is the deterministic 100 ms render tick used by tests and must:

1. return `Disabled` without reading when mode is off;
2. poll the ordered temperature provider only when no sample exists or 500 ms elapsed since the last sensor poll; render ticks between polls reuse the last target temperature;
3. on a valid reading, reset the missing-data timer, store it as the target, calculate `ColorEngine.Step(target.Celsius, renderElapsed)`, connect lighting if needed, apply `LightingCommandGate`, and emit `Active`;
4. after a missing sensor poll and for under five seconds, freeze the already displayed color, emit `HoldingLastColor`, and perform no writes on intervening render ticks;
5. on missing data at or over five seconds, call `ReleaseAsync` once and emit `SensorUnavailable`;
6. catch lighting connection/write failures separately and emit `LightingUnavailable` without stopping sensor monitoring;
7. persist mode and setting changes before raising the new snapshot.

The background loop uses `PeriodicTimer(TimeSpan.FromMilliseconds(100))`, while the runtime independently enforces the 500 ms sensor interval and `LightingCommandGate` enforces at most 10 Hz writes. This separation is required for visibly smooth transitions even though GPU telemetry is sampled at 2 Hz. All public lifecycle methods are protected by one `SemaphoreSlim`; `SuspendAsync` stops the loop and releases lighting; `ResumeAsync` restarts only if mode is enabled; `StopAsync` cancels, awaits the loop, releases lighting, and disposes sources.

- [ ] **Step 4: Add reconnect and suspend/resume tests, then run all core tests**

Add tests asserting a failed fake controller is retried on the next valid reading, and that suspend releases while resume returns to `Active` with the preserved mode.

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Core.Tests\LumaTherm.Core.Tests.csproj --filter FullyQualifiedName~ThermalRuntimeTests
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln
```

Expected: all state-machine tests PASS without wall-clock delays.

- [ ] **Step 5: Commit the runtime state machine**

```powershell
git add src/LumaTherm.Core/Runtime tests/LumaTherm.Core.Tests/Runtime
git commit -m "feat: orchestrate thermal lighting runtime"
```

---

### Task 7: Testable Application ViewModels and Live History

**Files:**
- Create: `src/LumaTherm.Core/System/IStartupService.cs`
- Create: `src/LumaTherm.App/LumaTherm.App.csproj`
- Create: `src/LumaTherm.App/ViewModels/ObservableObject.cs`
- Create: `src/LumaTherm.App/ViewModels/RelayCommand.cs`
- Create: `src/LumaTherm.App/ViewModels/MainViewModel.cs`
- Create: `src/LumaTherm.App/ViewModels/SettingsViewModel.cs`
- Create: `tests/LumaTherm.App.Tests/LumaTherm.App.Tests.csproj`
- Create: `tests/LumaTherm.App.Tests/ViewModels/MainViewModelTests.cs`
- Create: `tests/LumaTherm.App.Tests/ViewModels/SettingsViewModelTests.cs`
- Modify: `LumaTherm.sln`

**Interfaces:**
- Consumes: `IThermalRuntime`, `RuntimeSnapshot`, `AppSettings`, `ISettingsStore`.
- Produces: WPF-bindable `MainViewModel` and `SettingsViewModel`, plus `IStartupService`.

- [ ] **Step 1: Write ViewModel tests for live data, bounded history, toggling, and validation**

```csharp
namespace LumaTherm.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void SnapshotUpdate_ProjectsTemperatureColorAndStatus()
    {
        var runtime = new FakeThermalRuntime();
        var vm = new MainViewModel(runtime);

        runtime.Publish(RuntimeFixture.ActiveSnapshot(68));

        Assert.Equal("68°C", vm.TemperatureText);
        Assert.Equal("Режим активен", vm.StatusText);
        Assert.Equal("#FFC64A", vm.CurrentColorHex);
        Assert.Single(vm.History);
    }

    [Fact]
    public void History_IsLimitedToOneHundredTwentySamples()
    {
        var runtime = new FakeThermalRuntime();
        var vm = new MainViewModel(runtime);

        for (var i = 0; i < 130; i++) runtime.Publish(RuntimeFixture.ActiveSnapshot(50 + i % 20));

        Assert.Equal(120, vm.History.Count);
    }
}
```

`SettingsViewModelTests` must assert: changing cold temperature to 65 while warm is 65 sets `ValidationMessage` and does not save; a valid 40/67/88 profile calls runtime update and store save; toggling autostart invokes `IStartupService.SetEnabledAsync(true)` and only updates settings after success.

- [ ] **Step 2: Run App tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels
```

Expected: FAIL because the WPF project and ViewModels do not exist.

- [ ] **Step 3: Implement MVVM primitives and MainViewModel**

The app project targets `net8.0-windows10.0.22621.0`, enables WPF and Windows Forms, references Core and Infrastructure, and sets `OutputType` to `WinExe`.

Use these public members on `MainViewModel`:

```csharp
public ObservableCollection<TemperaturePoint> History { get; } = [];
public double CurrentTemperature { get; private set; } = double.NaN;
public RgbColor DisplayColor { get; private set; } = ThermalProfile.Default.ColdColor;
public ThermalProfile Profile { get; private set; } = ThermalProfile.Default;
public string TemperatureText { get; private set; } = "—°C";
public string StatusText { get; private set; } = "Подключение…";
public string CurrentColorHex { get; private set; } = ThermalProfile.Default.ColdColor.ToHex();
public string GpuName { get; private set; } = "GPU не обнаружен";
public string SensorSource { get; private set; } = "—";
public string LightingDeviceName { get; private set; } = "Подсветка не обнаружена";
public bool IsModeEnabled { get; private set; }
public RelayCommand ToggleModeCommand { get; }
```

`TemperaturePoint` is a record of timestamp and Celsius. Marshal runtime events through an injected `SynchronizationContext`; when none is provided, execute inline for tests. Map every `RuntimeStatus` to a concrete Russian status string. Append history only when `TemperatureReading.Timestamp` changes (not on the 100 ms render snapshots), and keep only 120 sensor samples, which equals 60 seconds at 2 Hz. Project every snapshot into `CurrentTemperature` and `DisplayColor`; keep `Profile` synchronized after a successful settings update so the custom controls have no dependency on App-layer color types.

- [ ] **Step 4: Implement validated SettingsViewModel and run tests**

```csharp
namespace LumaTherm.Core.System;

public interface IStartupService
{
    Task<bool> GetEnabledAsync(CancellationToken cancellationToken);
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}
```

`SettingsViewModel` exposes cold/warm/hot temperatures and `RgbColor` values, smoothing, three behavior toggles, device labels, `ValidationMessage`, `SaveCommand`, and `ResetDefaultsCommand`. `SaveAsync` creates a candidate `ThermalProfile`, calls `Validate`, invokes startup only when its value changed, calls `ThermalRuntime.UpdateSettingsAsync`, then clears validation. Catch `ArgumentException` and show its exact user-safe Russian translation; do not mutate live settings on failure.

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~ViewModels
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln
```

Expected: all ViewModel and solution tests PASS.

- [ ] **Step 5: Commit the application state layer**

```powershell
git add LumaTherm.sln src/LumaTherm.Core/System src/LumaTherm.App tests/LumaTherm.App.Tests
git commit -m "feat: add dashboard and settings view models"
```

---

### Task 8: Thermal Core Visual System, Logo Assets, and Dashboard

**Files:**
- Create: `tools/LumaTherm.AssetBuilder/LumaTherm.AssetBuilder.csproj`
- Create: `tools/LumaTherm.AssetBuilder/Program.cs`
- Create: `src/LumaTherm.App/Assets/LogoGeometry.xaml`
- Generate: `src/LumaTherm.App/Assets/LumaTherm.ico`
- Generate: `packaging/Assets/StoreLogo.png`
- Generate: `packaging/Assets/Square44x44Logo.png`
- Generate: `packaging/Assets/Square150x150Logo.png`
- Generate: `packaging/Assets/Wide310x150Logo.png`
- Create: `src/LumaTherm.App/App.xaml`
- Create: `src/LumaTherm.App/App.xaml.cs`
- Create: `src/LumaTherm.App/MainWindow.xaml`
- Create: `src/LumaTherm.App/MainWindow.xaml.cs`
- Create: `src/LumaTherm.App/Controls/TemperatureRing.xaml`
- Create: `src/LumaTherm.App/Controls/TemperatureRing.xaml.cs`
- Create: `src/LumaTherm.App/Controls/ThermalGradientBar.xaml`
- Create: `src/LumaTherm.App/Controls/ThermalGradientBar.xaml.cs`
- Create: `src/LumaTherm.App/Controls/TemperatureSparkline.xaml`
- Create: `src/LumaTherm.App/Controls/TemperatureSparkline.xaml.cs`
- Create: `src/LumaTherm.App/Views/DashboardView.xaml`
- Create: `src/LumaTherm.App/Views/DashboardView.xaml.cs`
- Create: `tests/LumaTherm.App.Tests/Ui/DashboardXamlContractTests.cs`
- Modify: `src/LumaTherm.App/LumaTherm.App.csproj`

**Interfaces:**
- Consumes: `MainViewModel` and color/history properties from Task 7.
- Produces: compiled Thermal Core shell, dashboard controls, vector logo, `.ico`, and package PNG assets.

- [ ] **Step 1: Write a failing XAML contract test for the approved dashboard**

The test reads source XAML relative to the repository root so it runs without opening a window:

```csharp
using System.Xml.Linq;

namespace LumaTherm.App.Tests.Ui;

public sealed class DashboardXamlContractTests
{
    [Fact]
    public void Dashboard_ContainsApprovedBindingsAndAccessibleModeToggle()
    {
        var path = SourcePath("src", "LumaTherm.App", "Views", "DashboardView.xaml");
        var text = File.ReadAllText(path);
        var document = XDocument.Parse(text);

        Assert.NotNull(document.Root);
        Assert.Contains("TemperatureText", text);
        Assert.Contains("CurrentColorHex", text);
        Assert.Contains("History", text);
        Assert.Contains("ToggleModeCommand", text);
        Assert.Contains("AutomationProperties.Name=\"Включить или выключить термосинхронизацию\"", text);
    }

    [Fact]
    public void AppTheme_DefinesEveryThermalCoreToken()
    {
        var text = File.ReadAllText(SourcePath("src", "LumaTherm.App", "App.xaml"));
        string[] keys = ["WindowBackground", "RailBackground", "PanelBackground", "PrimaryText", "MutedText", "ColdColor", "WarmColor", "HotColor", "SuccessColor"];

        foreach (var key in keys) Assert.Contains($"x:Key=\"{key}\"", text);
    }

    private static string SourcePath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LumaTherm.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), Path.Combine(parts));
    }
}
```

- [ ] **Step 2: Run the UI contract test and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~DashboardXamlContractTests
```

Expected: FAIL because the XAML files and resources are absent.

- [ ] **Step 3: Implement the vector logo and deterministic asset builder**

`LogoGeometry.xaml` defines three rotated blade `PathGeometry` resources and an outer ring. The ring uses a linear gradient `#50C8FF → #FFC64A → #FF565D`; the hub is `#EEF7FA`.

`LumaTherm.AssetBuilder` is a `net8.0-windows` console project with `<UseWPF>true</UseWPF>`. `Program.Main` accepts one output root and renders the same `DrawingGroup` through `RenderTargetBitmap`. Write `StoreLogo.png` at 50×50, `Square44x44Logo.png` at 44×44, `Square150x150Logo.png` at 150×150, `Wide310x150Logo.png` at 310×150 with the mark centered in a 150×150 safe area, and the app PNG payloads at 44×44 and 256×256 for `LumaTherm.ico`. The ICO header must be `reserved=0`, `type=1`, `count=2`; each directory entry stores width/height (`0` for 256), planes `1`, bit depth `32`, payload length, and payload offset. Render a transparent background, 3-pixel scaled gradient ring, three filled fan blades rotated 120°, and a circular hub.

Run:

```powershell
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.AssetBuilder -- "$PWD"
```

Then inspect all generated PNGs with the local image viewer and verify transparent corners, centered blades, crisp 44px output, and the blue→amber→red gradient.

- [ ] **Step 4: Implement the dark Thermal Core shell and custom controls**

`App.xaml` must define these exact colors and brushes:

```xml
<Color x:Key="WindowBackground">#171B20</Color>
<Color x:Key="RailBackground">#111419</Color>
<Color x:Key="PanelBackground">#20252B</Color>
<Color x:Key="PanelSecondary">#1B2025</Color>
<Color x:Key="PrimaryText">#EEF4F8</Color>
<Color x:Key="MutedText">#7E8994</Color>
<Color x:Key="ColdColor">#50C8FF</Color>
<Color x:Key="WarmColor">#FFC64A</Color>
<Color x:Key="HotColor">#FF565D</Color>
<Color x:Key="SuccessColor">#55D69C</Color>
```

Create matching `SolidColorBrush` resources, typography styles based on `Segoe UI Variable`, 13px panel corners, 8px control corners, visible keyboard focus, and hover/pressed states. Never use pure black or pure white for large surfaces.

`MainWindow` is 1180×720 with minimum 960×620, custom dark title bar, 76px navigation rail, dashboard/settings navigation, minimize/maximize/close buttons, and `WindowChrome` resize borders. All icon-only buttons have Russian `AutomationProperties.Name` values.

`TemperatureRing` exposes dependency properties `Temperature`, `Minimum=35`, `Maximum=85`, and `DisplayColor`; draw a 270° arc and marker in `OnRender`. `ThermalGradientBar` exposes three colors and three temperatures, draws the continuous gradient and labelled markers. `TemperatureSparkline` exposes `IEnumerable<TemperaturePoint> ItemsSource`, redraws on collection changes, scales to the visible min/max with a 2 °C pad, and has a screen-reader label.

- [ ] **Step 5: Implement DashboardView with the approved information hierarchy**

The root layout must bind to `MainViewModel` and contain:

```xml
<Button Command="{Binding ToggleModeCommand}"
        AutomationProperties.Name="Включить или выключить термосинхронизацию" />
<controls:TemperatureRing Temperature="{Binding CurrentTemperature}"
                          DisplayColor="{Binding DisplayColor}" />
<controls:ThermalGradientBar ColdTemperature="{Binding Profile.ColdTemperature}"
                             WarmTemperature="{Binding Profile.WarmTemperature}"
                             HotTemperature="{Binding Profile.HotTemperature}" />
<controls:TemperatureSparkline ItemsSource="{Binding History}"
                               AutomationProperties.Name="График температуры GPU за последнюю минуту" />
```

Add the GPU/source/device labels and four behavior rows from the approved mockup. Use a two-column top area (230px ring + flexible profile) and a 65/35 lower split that collapses to a single column below 980px.

- [ ] **Step 6: Compile XAML, run contracts, and commit the dashboard**

```powershell
& "$PWD\.dotnet\dotnet.exe" build src\LumaTherm.App\LumaTherm.App.csproj -c Debug
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter FullyQualifiedName~DashboardXamlContractTests
git add tools/LumaTherm.AssetBuilder src/LumaTherm.App packaging/Assets tests/LumaTherm.App.Tests/Ui
git commit -m "feat: build Thermal Core dashboard"
```

Expected: WPF XAML compiles, UI contract tests PASS, and generated assets render correctly.

---

### Task 9: Settings Screen and Native Color Editing

**Files:**
- Create: `src/LumaTherm.App/Services/IColorPickerService.cs`
- Create: `src/LumaTherm.App/Services/ColorPickerService.cs`
- Create: `src/LumaTherm.App/Views/SettingsView.xaml`
- Create: `src/LumaTherm.App/Views/SettingsView.xaml.cs`
- Create: `tests/LumaTherm.App.Tests/Ui/SettingsXamlContractTests.cs`
- Modify: `src/LumaTherm.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/LumaTherm.App/MainWindow.xaml`

**Interfaces:**
- Consumes: `SettingsViewModel` from Task 7.
- Produces: editable three-stop profile, smoothing slider, app-behavior toggles, hardware status, and tray preview.

- [ ] **Step 1: Write failing settings XAML and color-command tests**

```csharp
namespace LumaTherm.App.Tests.Ui;

public sealed class SettingsXamlContractTests
{
    [Fact]
    public void SettingsView_ContainsAllEditableRequirements()
    {
        var text = File.ReadAllText(UiSourcePath.Get("Views", "SettingsView.xaml"));
        string[] bindings = ["ColdTemperature", "WarmTemperature", "HotTemperature", "SmoothingSeconds", "IsAutostartEnabled", "MinimizeToTray", "NotificationsEnabled", "PickColdColorCommand", "PickWarmColorCommand", "PickHotColorCommand"];

        foreach (var binding in bindings) Assert.Contains(binding, text);
        Assert.Contains("Minimum=\"0.1\"", text);
        Assert.Contains("Maximum=\"5\"", text);
        Assert.Contains("ValidationMessage", text);
    }
}
```

Add a ViewModel test where fake color picker returns `RgbColor(1,2,3)` and assert `PickColdColorCommand` updates only `ColdColor` and then invokes the validated save path.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter "FullyQualifiedName~Settings"
```

Expected: FAIL because settings XAML and picker commands are absent.

- [ ] **Step 3: Implement the color-picker service and ViewModel commands**

```csharp
using LumaTherm.Core.Colors;

namespace LumaTherm.App.Services;

public interface IColorPickerService
{
    RgbColor? Pick(RgbColor current);
}

public sealed class ColorPickerService : IColorPickerService
{
    public RgbColor? Pick(RgbColor current)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? new RgbColor(dialog.Color.R, dialog.Color.G, dialog.Color.B)
            : null;
    }
}
```

Inject the picker into `SettingsViewModel`. Each picker command reads the matching current color, ignores cancel, changes only that color, raises property notifications for the color and hex string, and calls the same validated save method as sliders/toggles.

- [ ] **Step 4: Implement SettingsView with validation and keyboard accessibility**

Use the approved two-column layout. The left column contains three color cards, three 0..120 temperature sliders paired with numeric text boxes, and the 0.1..5.0 smoothing slider. The right column contains three app switches, two hardware rows, and a static tray-menu preview.

Every slider has a visible label and `AutomationProperties.Name`; every numeric text box uses `UpdateSourceTrigger=LostFocus`; every color card is a real `Button` bound to its picker command and shows both swatch and hex text. Show `ValidationMessage` in `#FF858B` above the editor; keep the last valid live profile until validation succeeds. Add Reset and Save buttons, with Save as the only primary blue button.

- [ ] **Step 5: Run UI and ViewModel tests, then commit**

```powershell
& "$PWD\.dotnet\dotnet.exe" build src\LumaTherm.App\LumaTherm.App.csproj -c Debug
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.App.Tests\LumaTherm.App.Tests.csproj --filter "FullyQualifiedName~Settings"
git add src/LumaTherm.App/Services src/LumaTherm.App/Views/SettingsView.xaml* src/LumaTherm.App/ViewModels/SettingsViewModel.cs src/LumaTherm.App/MainWindow.xaml tests/LumaTherm.App.Tests
git commit -m "feat: add thermal profile settings UI"
```

Expected: build and all settings tests PASS.

---

### Task 10: Tray, Autostart, Single Instance, and Power Events

**Files:**
- Create: `src/LumaTherm.Infrastructure/System/IStartupTaskPlatform.cs`
- Create: `src/LumaTherm.Infrastructure/System/PackagedStartupService.cs`
- Create: `src/LumaTherm.Infrastructure/System/RegistryStartupService.cs`
- Create: `src/LumaTherm.Infrastructure/System/StartupService.cs`
- Create: `src/LumaTherm.Infrastructure/System/SingleInstanceCoordinator.cs`
- Create: `src/LumaTherm.Infrastructure/System/IPowerEventSource.cs`
- Create: `src/LumaTherm.Infrastructure/System/WindowsPowerEventSource.cs`
- Create: `src/LumaTherm.Infrastructure/System/PowerEventService.cs`
- Create: `src/LumaTherm.App/Services/TrayIconService.cs`
- Create: `src/LumaTherm.App/Services/WindowClosePolicy.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/System/StartupServiceTests.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/System/SingleInstanceCoordinatorTests.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/System/PowerEventServiceTests.cs`
- Create: `tests/LumaTherm.App.Tests/Services/TrayIconServiceTests.cs`
- Create: `tests/LumaTherm.App.Tests/Services/WindowClosePolicyTests.cs`

**Interfaces:**
- Consumes: `IStartupService`, `IThermalRuntime`, `MainViewModel`, and the generated app icon.
- Produces: one running instance, tray lifecycle, default-off startup, and suspend/resume forwarding.

- [ ] **Step 1: Write lifecycle tests**

`StartupServiceTests` use a fake packaged platform. Assert `SetEnabledAsync(true)` requests enable, maps `EnabledByPolicy` to true, and throws a user-safe `InvalidOperationException` for `DisabledByUser`. Assert unpackaged mode delegates to a fake registry adapter.

```csharp
namespace LumaTherm.Infrastructure.Tests.System;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondInstance_SignalsFirstInstance()
    {
        var name = $"LumaTherm-Test-{Guid.NewGuid():N}";
        await using var first = new SingleInstanceCoordinator(name);
        await using var second = new SingleInstanceCoordinator(name);
        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += (_, _) => signaled.SetResult();

        Assert.True(await first.TryAcquireAsync(CancellationToken.None));
        Assert.False(await second.TryAcquireAsync(CancellationToken.None));
        await second.SignalActivationAsync(CancellationToken.None);

        await signaled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
```

`TrayIconServiceTests` test a separate `BuildMenuState(RuntimeSnapshot)` pure method: temperature label `68°C`, mode label `Выключить режим`, device status, and four menu entries. Add transition tests proving that `Active -> SensorUnavailable`, `Active -> LightingUnavailable`, and `Active -> Faulted` each create one Russian notification when enabled, repeated identical snapshots create none, return to `Active` rearms the notification, and `NotificationsEnabled=false` suppresses it.

`PowerEventServiceTests` raise suspend/resume through a fake `IPowerEventSource` and assert the matching `IThermalRuntime` method is awaited once and subscriptions are removed on dispose. `WindowClosePolicyTests` assert ordinary close/minimize returns `HideAndCancel` when close-to-tray is enabled, explicit Exit always returns `AllowClose`, and normal close returns `AllowClose` when close-to-tray is disabled. The tray Exit test uses a fake runtime and asserts `StopAsync` completes before application shutdown is requested.

- [ ] **Step 2: Run lifecycle tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --filter "FullyQualifiedName~StartupServiceTests|FullyQualifiedName~SingleInstanceCoordinatorTests|FullyQualifiedName~TrayIconServiceTests"
```

Expected: FAIL because lifecycle services do not exist.

- [ ] **Step 3: Implement packaged and portable autostart with default off**

`PackagedStartupService` calls `StartupTask.GetAsync("LumaThermStartup")`; `GetEnabledAsync` returns true only for `StartupTaskState.Enabled`; `SetEnabledAsync(false)` calls `Disable`; enabling calls `RequestEnableAsync` and accepts only `Enabled` or `EnabledByPolicy`.

`RegistryStartupService` uses `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`, value `LumaTherm`, quoted executable path plus `--autostart`. Delete only that named value when disabling.

`StartupService` detects package identity by attempting `Windows.ApplicationModel.Package.Current.Id.Name`; it delegates to packaged service when present and registry service only for the portable publish. No constructor or installation path enables startup automatically.

- [ ] **Step 4: Implement mutex/named-pipe activation and power events**

`SingleInstanceCoordinator` owns mutex `Local\\LumaTherm.SingleInstance` and named pipe `LumaTherm.Activation`. The owner runs one cancellable `NamedPipeServerStream` loop reading the exact UTF-8 line `SHOW`; a non-owner writes `SHOW\n` then exits. Dispose cancels the server and releases the mutex only when owned.

`IPowerEventSource` exposes one event with `Suspend`/`Resume` values. `WindowsPowerEventSource` is the only class that subscribes to `Microsoft.Win32.SystemEvents.PowerModeChanged`. `PowerEventService` maps those values to `IThermalRuntime.SuspendAsync` and `ResumeAsync`, serializes callbacks with a semaphore, logs exceptions, and always unsubscribes/disposes the source.

- [ ] **Step 5: Implement tray behavior and close-to-tray**

`TrayIconService` wraps `System.Windows.Forms.NotifyIcon`, loads the embedded `LumaTherm.ico`, and owns menu items for temperature (disabled), open, toggle mode, and exit. A single left click and double click both call an idempotent `ShowWindow` action. Toggle awaits the ViewModel command; Exit sets an explicit shutdown flag, awaits runtime stop, hides/disposes the icon, then shuts down WPF.

`WindowClosePolicy` centralizes the tested decision. `MainWindow.Closing` cancels and hides only when the policy returns `HideAndCancel`; Window minimize uses the same policy. Update the tooltip to `LumaTherm · 68°C · Активно`, truncated to NotifyIcon's platform limit. `TrayIconService.ShowNotification(title, message)` uses `ShowBalloonTip` for the one-shot settings-recovery warning and for rearmed transitions to `SensorUnavailable`, `LightingUnavailable`, or `Faulted`; never emit a balloon when notifications are disabled.

- [ ] **Step 6: Run tests and commit lifecycle integration**

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --filter "FullyQualifiedName~System|FullyQualifiedName~TrayIcon"
git add src/LumaTherm.Core/System src/LumaTherm.Infrastructure/System src/LumaTherm.App/Services/TrayIconService.cs tests
git commit -m "feat: add tray autostart and lifecycle services"
```

Expected: all focused tests PASS; the tests never modify the real Run key or startup task.

---

### Task 11: Diagnostics and Application Composition

**Files:**
- Create: `src/LumaTherm.Core/Diagnostics/IAppLogger.cs`
- Create: `src/LumaTherm.Core/Diagnostics/LogEntry.cs`
- Create: `src/LumaTherm.Infrastructure/Diagnostics/RollingFileLogger.cs`
- Create: `src/LumaTherm.App/Composition/AppHost.cs`
- Create: `src/LumaTherm.App/Composition/AppServices.cs`
- Modify: `src/LumaTherm.App/App.xaml`
- Modify: `src/LumaTherm.App/App.xaml.cs`
- Create: `tests/LumaTherm.Infrastructure.Tests/Diagnostics/RollingFileLoggerTests.cs`
- Create: `tests/LumaTherm.App.Tests/Composition/AppHostTests.cs`

**Interfaces:**
- Consumes: all previously implemented sensor, light, settings, runtime, tray, startup, and power services.
- Produces: the real composition root, bounded rolling diagnostics, clean startup/shutdown, and crash reporting.

- [ ] **Step 1: Write logger and composition tests**

Use a temporary folder and a deliberately small 256-byte limit in `RollingFileLoggerTests`. Assert that every line is timestamped JSONL, contains level/event/message fields, omits exception stack traces when no exception exists, rotates before unbounded growth, retains exactly the configured current file plus four archives, and can be disposed twice.

`AppHostTests` inject fake factories through `AppServices`. Assert that construction keeps the temperature source order `NVML -> MSI Afterburner`, loads settings before creating ViewModels, does not start the thermal runtime until `StartAsync`, and disposes services in reverse dependency order even when one dispose throws.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln --filter "FullyQualifiedName~RollingFileLoggerTests|FullyQualifiedName~AppHostTests"
```

Expected: FAIL because diagnostics and the composition root do not exist.

- [ ] **Step 3: Implement bounded JSONL logging**

```csharp
namespace LumaTherm.Core.Diagnostics;

public enum AppLogLevel { Debug, Information, Warning, Error }

public interface IAppLogger
{
    void Write(AppLogLevel level, string eventName, string message,
        Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null);
}
```

`RollingFileLogger` writes UTF-8 JSONL under `%LOCALAPPDATA%\LumaTherm\logs\lumatherm.log`. Serialize writes with a lock; before each write rotate `lumatherm.4.log` away, shift `3 -> 4` through `0 -> 1`, and start a new current file when the next UTF-8 record would exceed 1 MiB. Keep four archives plus the current file. Logging must never crash the controller: catch file/JSON failures and report only to `System.Diagnostics.Debug`.

Log these stable event names at minimum: `app.start`, `app.stop`, `sensor.selected`, `sensor.failed`, `lamp.connected`, `lamp.disconnected`, `runtime.enabled`, `runtime.disabled`, `settings.saved`, `startup.changed`, and `app.unhandled`.

- [ ] **Step 4: Implement the application host and exception boundaries**

`AppServices` is an injectable factory record used by tests; production defaults create, in this order: logger, `JsonSettingsStore`, `NvmlTemperatureSource`, `AfterburnerTemperatureSource`, `TemperatureProvider`, `LampArrayDiscovery`, `LightingControlGate`, `LampArrayLightingController`, `ThermalRuntime`, startup/single-instance/power services, ViewModels, main window, and tray icon.

`AppHost.StartAsync` performs this exact sequence:

1. acquire `SingleInstanceCoordinator`; if not owner, signal `SHOW` and request immediate clean exit;
2. load/migrate/validate settings and retain any one-shot `RecoveryMessage`;
3. create and show the main window unless `--autostart` is present;
4. initialize tray and power subscriptions, then show the retained recovery warning once when notifications are enabled;
5. start device discovery;
6. start `ThermalRuntime` only when the persisted mode is enabled (first run remains disabled).

`AppHost.StopAsync` prevents re-entry, stops runtime, releases LampArray ownership, disposes discovery/power/tray, saves valid settings, disposes the single-instance coordinator, and finally logs/disposes the logger. Each step is attempted even if an earlier one fails.

`App.xaml.cs` uses explicit `ShutdownMode.OnExplicitShutdown`. Register `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`; log `app.unhandled`, show one Russian error dialog for a foreground dispatcher failure or a tray notification for a background failure when available/enabled, and always attempt `AppHost.StopAsync` during exit. Marshal `SHOW` activation to the dispatcher, restore the window, and call `Activate()`.

- [ ] **Step 5: Run the complete automated suite and commit composition**

```powershell
& "$PWD\.dotnet\dotnet.exe" build LumaTherm.sln -c Debug
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln -c Debug --no-build
git add src tests
git commit -m "feat: compose LumaTherm runtime and diagnostics"
```

Expected: solution builds with zero warnings and every unit/integration test PASS.

---

### Task 12: MSIX Packaging, Signing, Install, and Uninstall

**Files:**
- Create: `packaging/LumaTherm.Packaging.csproj`
- Create: `packaging/AppxManifest.xml`
- Verify: `packaging/Assets/StoreLogo.png`
- Verify: `packaging/Assets/Square44x44Logo.png`
- Verify: `packaging/Assets/Square150x150Logo.png`
- Verify: `packaging/Assets/Wide310x150Logo.png`
- Create: `packaging/public/.gitkeep`
- Create: `scripts/build-release.ps1`
- Create: `scripts/install.ps1`
- Create: `scripts/uninstall.ps1`
- Create: `tests/LumaTherm.Packaging.Tests/LumaTherm.Packaging.Tests.csproj`
- Create: `tests/LumaTherm.Packaging.Tests/ManifestTests.cs`
- Modify: `.gitignore`
- Modify: `LumaTherm.sln`

**Interfaces:**
- Consumes: the self-contained `win-x64` WPF publish and generated brand assets.
- Produces: a signed MSIX with background Dynamic Lighting identity, default-off startup task, and reversible per-user scripts.

- [ ] **Step 1: Write manifest contract tests**

Parse `packaging/AppxManifest.xml` with `XDocument`. Assert all of the following exact contracts:

- package architecture is `x64` and application executable is `LumaTherm.exe`;
- `rescap:Capability Name="runFullTrust"` exists;
- a `uap3:Extension Category="windows.appExtension"` contains `uap3:AppExtension Name="com.microsoft.windows.lighting"`;
- a `desktop:Extension Category="windows.startupTask"` contains `desktop:StartupTask TaskId="LumaThermStartup" Enabled="false"`;
- visual assets referenced by the manifest exist and are PNG files;
- no startup element is enabled by default.

- [ ] **Step 2: Run manifest tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj
```

Expected: FAIL because the package project and manifest do not exist.

- [ ] **Step 3: Create the package manifest and deterministic assets**

Use these namespaces and declarations in `AppxManifest.xml`:

```xml
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap3 desktop rescap">
  <Identity Name="LumaTherm" Publisher="CN=LumaTherm Local" Version="1.0.0.0" ProcessorArchitecture="x64" />
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
  <Applications>
    <Application Id="LumaTherm" Executable="LumaTherm.exe" EntryPoint="Windows.FullTrustApplication">
      <Extensions>
        <uap3:Extension Category="windows.appExtension">
          <uap3:AppExtension Name="com.microsoft.windows.lighting" Id="LumaThermLighting" PublicFolder="public" DisplayName="LumaTherm" />
        </uap3:Extension>
        <desktop:Extension Category="windows.startupTask" Executable="LumaTherm.exe" EntryPoint="Windows.FullTrustApplication">
          <desktop:StartupTask TaskId="LumaThermStartup" Enabled="false" DisplayName="LumaTherm" />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>
</Package>
```

Add the full required Properties/Dependencies/uap:VisualElements around this excerpt. Reuse `tools/LumaTherm.AssetBuilder` to generate the four PNG sizes without resampling blur. Add `packaging/public/.gitkeep` because the app-extension declaration requires that public folder.

`LumaTherm.Packaging.csproj` targets `net8.0`, is non-packable, and pins `Microsoft.Windows.SDK.BuildTools` to `10.0.26100.8249` with `PrivateAssets="all"` and `GeneratePathProperty="true"` so the release script can locate `MakeAppx.exe` and `SignTool.exe` without a machine-wide Windows SDK.

- [ ] **Step 4: Implement a repeatable signed release build**

`scripts/build-release.ps1` uses `$ErrorActionPreference = 'Stop'` and only writes beneath `artifacts/`, `dist/`, and the ignored `packaging/local-signing/` folder. It must:

1. restore and test the solution in Release;
2. publish `LumaTherm.App` for `win-x64` with `--self-contained true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, and symbols disabled;
3. rebuild `artifacts/package-layout/` from the publish output, manifest, assets, and `public/`;
4. query `PkgMicrosoft_Windows_SDK_BuildTools` with `dotnet msbuild -getProperty`, then choose the included x64 `MakeAppx.exe` and `SignTool.exe` by resolved absolute path;
5. when no certificate is supplied, create `CN=LumaTherm Local` in `Cert:\CurrentUser\My`, export an ignored PFX/CER with a per-run secure random password, and never print that password;
6. run MakeAppx and SignTool with SHA-256, then verify the signature;
7. produce `dist/LumaTherm-1.0.0-win-x64.msix`, `dist/LumaTherm-1.0.0-portable-win-x64.zip`, `dist/LumaTherm.cer`, and `dist/SHA256SUMS.txt`, then copy `install.ps1` and `uninstall.ps1` into `dist/` so their `$PSScriptRoot` is the release folder.

Accept optional `-CertificatePath`, `-CertificatePassword`, and `-Publisher` parameters for a future trusted production certificate. Fail early if the manifest Publisher differs from the signing certificate subject.

- [ ] **Step 5: Implement reversible install and uninstall scripts**

`scripts/install.ps1` resolves only files beside the script (the release builder copies it into `dist/`), verifies the MSIX signature and checksum, asks before importing the local certificate into `Cert:\CurrentUser\TrustedPeople`, then calls `Add-AppxPackage`. It never enables autostart. Print the installed package name and the Start-menu launch instruction.

`scripts/uninstall.ps1` resolves the exact `LumaTherm` package, asks for confirmation unless `-Force`, removes only that package, deletes only the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LumaTherm` portable fallback, and leaves `%LOCALAPPDATA%\LumaTherm` unless `-RemoveUserData` is passed. `-RemoveCertificate` removes only the matching `CN=LumaTherm Local` certificate thumbprint found in `dist/LumaTherm.cer`.

Ignore `.dotnet/`, `artifacts/`, `dist/`, `packaging/local-signing/`, `*.pfx`, and generated certificate files.

- [ ] **Step 6: Run packaging tests, build both artifacts, and commit**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Packaging.Tests\LumaTherm.Packaging.Tests.csproj -c Release
& "$PWD\scripts\build-release.ps1"
git add packaging scripts tests/LumaTherm.Packaging.Tests .gitignore LumaTherm.sln
git commit -m "build: add signed MSIX and portable release"
```

Expected: manifest tests PASS, SignTool verification succeeds, both release artifacts and their SHA-256 entries exist. Generated keys, binaries, and artifacts remain untracked.

---

### Task 13: Hardware Smoke Tool, End-to-End Acceptance, and Handoff

**Files:**
- Create: `tools/LumaTherm.Smoke/LumaTherm.Smoke.csproj`
- Create: `tools/LumaTherm.Smoke/Program.cs`
- Create: `tools/LumaTherm.Smoke/SmokeCommand.cs`
- Create: `tests/LumaTherm.Smoke.Tests/LumaTherm.Smoke.Tests.csproj`
- Create: `tests/LumaTherm.Smoke.Tests/SmokeCommandTests.cs`
- Create: `docs/hardware-validation.md`
- Create: `README.md`
- Modify: `LumaTherm.sln`

**Interfaces:**
- Consumes: the same production sensor and LampArray adapters used by the app.
- Produces: safe diagnostic commands, recorded validation on this PC, and complete installation/usage/recovery documentation.

- [ ] **Step 1: Write smoke-command safety tests**

With fake temperature/light adapters, assert:

- `sensor` prints provider, GPU name, temperature, and exits 0 without touching lights; `sensor --skip-nvml` proves the Afterburner fallback path without invoking NVML;
- `lights` prints each LampArray name/id, lamp count, and availability without taking control;
- `cycle` without `--confirm-light-write` exits 2 and performs zero writes;
- confirmed `cycle` writes `#50C8FF`, `#FFC64A`, `#FF565D` in order, then releases in `finally` even when the second write fails;
- `simulate --from 35 --to 85 --seconds 1` uses the production interpolation/smoothing path and always releases ownership.

- [ ] **Step 2: Run smoke tests and verify failure**

```powershell
& "$PWD\.dotnet\dotnet.exe" test tests\LumaTherm.Smoke.Tests\LumaTherm.Smoke.Tests.csproj
```

Expected: FAIL because the smoke command does not exist.

- [ ] **Step 3: Implement the read-only probes and explicit write gate**

Build commands with a small manual argument parser so no additional CLI dependency is required. `sensor` tries NVML then Afterburner and prints actionable Russian errors; `--skip-nvml` constructs the provider with Afterburner only for deterministic fallback validation. `lights` performs discovery only. `cycle` and `simulate` require both `--confirm-light-write` and an interactive `YES` prompt unless `--non-interactive` is also supplied by the automated acceptance script. Every mutating command registers Ctrl+C cancellation and calls `ReleaseAsync` in `finally`.

Add `--json` to every command so acceptance results can be archived. Never change fan speed, GPU clocks, voltage, BIOS settings, or GIGABYTE Control Center files/processes.

- [ ] **Step 4: Run tests, commit the tool, then build the signed release**

```powershell
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln -c Release
git add tools/LumaTherm.Smoke tests/LumaTherm.Smoke.Tests LumaTherm.sln
git commit -m "test: add safe hardware smoke diagnostics"
& "$PWD\scripts\build-release.ps1"
```

Expected: every automated test PASS and the signed MSIX/portable ZIP are rebuilt from that exact commit.

- [ ] **Step 5: Perform read-only hardware discovery on this PC**

Close neither MSI Afterburner nor GIGABYTE Control Center. Run:

```powershell
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- sensor --json
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- sensor --skip-nvml --json
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- lights --json
```

Record in `docs/hardware-validation.md`: Windows version, NVIDIA GPU/provider, both normal and forced-fallback readings, current temperature, `GIGABYTE Device` LampArray id, availability, lamp count, and timestamp. Expected here: RTX 5070 is read through NVML, the forced fallback reads MAHM, and HID `VID_048D&PID_5702` is discovered through Dynamic Lighting.

- [ ] **Step 6: Ask for one explicit hardware-write confirmation and exercise RGB safely**

Before the first real light write, state that LumaTherm will temporarily take Windows Dynamic Lighting control and will release it afterward; obtain the user's explicit confirmation. Then run:

```powershell
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- cycle --confirm-light-write
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- simulate --from 35 --to 85 --seconds 10 --confirm-light-write
```

Visually confirm cold blue, warm yellow, hot red, a smooth transition, and restoration of GIGABYTE Control Center ownership after each command. If LampArray is unavailable, use the recovery checklist first: enable Windows Dynamic Lighting, move LumaTherm above conflicting background controllers, close only the RGB Fusion page if needed, and rerun discovery. Do not implement unsupported Gigabyte HID writes as a shortcut.

- [ ] **Step 7: Install and exercise the packaged application end to end**

Install using `dist/install.ps1`, launch from Start, and verify in this order:

1. first launch shows mode OFF and autostart OFF;
2. dashboard shows live temperature/device status and updates the 60-s chart;
3. enabling mode changes the physical fan lighting smoothly with GPU temperature/simulation;
4. custom thresholds/colors persist after restart and invalid `cold >= warm >= hot` ordering is rejected;
5. disabling mode immediately releases lighting and RGB Fusion can control it again;
6. close/minimize hides to tray; tray Open/Toggle/Exit work; a second launch activates the first instance;
7. enabling autostart registers the packaged startup task and the initial state was off; coordinate one real sign-out/sign-in check, confirm LumaTherm starts hidden in the tray, then disable autostart and confirm the task is disabled;
8. simulated sensor loss holds color for 5 s, then releases and shows an error; reconnection recovers automatically;
9. suspend/resume or the safe resume hook reconnects sensor and lights without a second process;
10. keep the app running in tray for at least five minutes while monitoring `%LOCALAPPDATA%\LumaTherm\logs`; verify no crash, runaway CPU/memory, or file growth beyond five 1-MiB files.

Record PASS/FAIL plus observed temperature/color for each item. Stop and diagnose any failure with `superpowers:systematic-debugging`; never mark acceptance complete with a skipped hardware-critical item.

- [ ] **Step 8: Complete documentation and final verification**

`README.md` covers: what LumaTherm does, supported Windows/Dynamic Lighting requirement, installation, first run, tray, profile editing, autostart, MSI Afterburner fallback, GIGABYTE Control Center coexistence, logs, uninstall, and limitations. `docs/hardware-validation.md` contains the timestamped evidence and exact release SHA-256.

Run the final clean verification from tracked source:

```powershell
git status --short
& "$PWD\.dotnet\dotnet.exe" clean LumaTherm.sln -c Release
& "$PWD\.dotnet\dotnet.exe" test LumaTherm.sln -c Release
& "$PWD\scripts\build-release.ps1"
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- sensor --json
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- sensor --skip-nvml --json
& "$PWD\.dotnet\dotnet.exe" run --project tools\LumaTherm.Smoke -c Release -- lights --json
Get-FileHash -Algorithm SHA256 dist\LumaTherm-1.0.0-win-x64.msix, dist\LumaTherm-1.0.0-portable-win-x64.zip
git status --short
```

Expected: clean tracked tree, every test PASS, signed artifacts rebuild successfully, both real devices are discovered, recorded hashes match `dist/SHA256SUMS.txt`, and only ignored release outputs exist outside git.

- [ ] **Step 9: Request code review, resolve findings, and finish the branch**

Invoke `superpowers:requesting-code-review` against the implementation range, address all correctness/safety findings, rerun Step 8, then invoke `superpowers:verification-before-completion`. Only after current evidence passes, invoke `superpowers:finishing-a-development-branch` and present the verified installer, portable ZIP, documentation, and any remaining hardware limitation plainly.

---

## Requirement Coverage Checklist

- GPU temperature direct + MSI Afterburner fallback: Tasks 3-4 and 13.
- Smooth blue/yellow/red curve with editable colors and limits: Tasks 1, 6, and 9.
- Enable/disable and release back to RGB Fusion: Tasks 5-7 and 13.
- Beautiful approved Thermal Core UI, brand, and logo: Tasks 7-9.
- Tray/background operation and single instance: Task 10.
- Autostart default OFF: Tasks 2, 9, 10, and 12.
- Sensor/device recovery, suspend/resume, diagnostics: Tasks 6, 10, and 11.
- Fully installable and verified Windows app: Tasks 12-13.
