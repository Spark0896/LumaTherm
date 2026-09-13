using System.Text.Json;
using LumaTherm.Core.Colors;
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

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);
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

        await store.SaveAsync(expected, TestContext.Current.CancellationToken);
        var actual = (await store.LoadAsync(TestContext.Current.CancellationToken)).Settings;

        Assert.Equal(expected, actual);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task SavingExistingFile_ReplacesStoredValuesWithoutLeavingTemporaryFile()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path, TimeProvider.System);
        var replacement = AppSettings.Default with
        {
            IsModeEnabled = true,
            IsAutostartEnabled = true,
            MinimizeToTray = false,
            NotificationsEnabled = false,
            PreferredLightingDeviceId = "replacement-device"
        };

        await store.SaveAsync(AppSettings.Default, TestContext.Current.CancellationToken);
        await store.SaveAsync(replacement, TestContext.Current.CancellationToken);
        var actual = (await store.LoadAsync(TestContext.Current.CancellationToken)).Settings;

        Assert.Equal(replacement, actual);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task InvalidJson_IsQuarantinedAndDefaultsAreReturned()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{invalid", TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var store = new JsonSettingsStore(path, clock);

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);
        var settings = result.Settings;

        Assert.Equal(AppSettings.Default, settings);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.corrupt-20260819-120000.json")));
        Assert.Equal("Настройки были повреждены и сброшены", result.RecoveryMessage);
    }

    [Fact]
    public async Task CorruptFilesWithSameTimestamp_AreQuarantinedWithoutOverwritingDiagnostics()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var store = new JsonSettingsStore(path, clock);

        await File.WriteAllTextAsync(path, "{first", TestContext.Current.CancellationToken);
        var first = await store.LoadAsync(TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, "{second", TestContext.Current.CancellationToken);
        var second = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.Default, first.Settings);
        Assert.Equal(AppSettings.Default, second.Settings);
        Assert.Equal("Настройки были повреждены и сброшены", first.RecoveryMessage);
        Assert.Equal("Настройки были повреждены и сброшены", second.RecoveryMessage);
        Assert.Equal("{first", await File.ReadAllTextAsync(Path.Combine(_directory, "settings.corrupt-20260819-120000.json"), TestContext.Current.CancellationToken));
        Assert.Equal("{second", await File.ReadAllTextAsync(Path.Combine(_directory, "settings.corrupt-20260819-120000-1.json"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SchemaZero_IsMigratedToCurrentProfile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"schemaVersion":0,"coldTemperature":30,"coldColor":"#102030","warmTemperature":62,"warmColor":"#405060","hotTemperature":91,"hotColor":"#708090","smoothingSeconds":1.2,"isModeEnabled":true,"isAutostartEnabled":true,"minimizeToTray":false,"notificationsEnabled":false,"preferredLightingDeviceId":"device-1"}
            """, TestContext.Current.CancellationToken);
        var store = new JsonSettingsStore(path, TimeProvider.System);

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);
        var expected = new AppSettings(2, new ThermalProfile(30, new RgbColor(0x10, 0x20, 0x30), 62, new RgbColor(0x40, 0x50, 0x60), 91, new RgbColor(0x70, 0x80, 0x90), 1.2), true, true, false, false, "device-1", AppLanguage.System, TrayMenuOptions.Default);

        Assert.Equal(expected, result.Settings);
        Assert.Null(result.RecoveryMessage);

        await store.SaveAsync(result.Settings, TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(2, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("Profile", out _));
    }

    [Fact]
    public async Task SchemaZero_WithMissingBehaviorFlags_UsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"schemaVersion":0,"coldTemperature":30,"coldColor":"#102030","warmTemperature":62,"warmColor":"#405060","hotTemperature":91,"hotColor":"#708090","smoothingSeconds":1.2}
            """, TestContext.Current.CancellationToken);
        var store = new JsonSettingsStore(path, TimeProvider.System);

        var settings = (await store.LoadAsync(TestContext.Current.CancellationToken)).Settings;

        Assert.False(settings.IsModeEnabled);
        Assert.False(settings.IsAutostartEnabled);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.NotificationsEnabled);
        Assert.Null(settings.PreferredLightingDeviceId);
    }

    [Theory]
    [InlineData("""{"schemaVersion":0,"coldTemperature":70,"coldColor":"#008CFF","warmTemperature":60,"warmColor":"#FFD800","hotTemperature":85,"hotColor":"#FF1800","smoothingSeconds":0.8}""")]
    [InlineData("""{"schemaVersion":1,"profile":{"coldTemperature":70,"coldColor":"#008CFF","warmTemperature":60,"warmColor":"#FFD800","hotTemperature":85,"hotColor":"#FF1800","smoothingSeconds":0.8}}""")]
    public async Task LegacySchemaWithInvalidProfile_IsQuarantinedAndDefaultsAreReturned(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var store = new JsonSettingsStore(path, clock);

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.corrupt-20260819-120000.json")));
        Assert.Equal("Настройки были повреждены и сброшены", result.RecoveryMessage);
    }

    [Fact]
    public async Task SchemaOne_IsMigratedToSchemaTwoWithDefaultPreferences()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"schemaVersion":1,"profile":{"coldTemperature":35,"coldColor":"#008CFF","warmTemperature":65,"warmColor":"#FFD800","hotTemperature":85,"hotColor":"#FF1800","smoothingSeconds":0.8},"isModeEnabled":true,"isAutostartEnabled":true,"minimizeToTray":false,"notificationsEnabled":false,"preferredLightingDeviceId":"device-1"}
            """, TestContext.Current.CancellationToken);
        var store = new JsonSettingsStore(path, TimeProvider.System);

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Settings.SchemaVersion);
        Assert.Equal(3, result.Settings.Profile.Points.Count);
        Assert.Equal(ThermalProfile.Default, result.Settings.Profile);
        Assert.True(result.Settings.IsModeEnabled);
        Assert.True(result.Settings.IsAutostartEnabled);
        Assert.False(result.Settings.MinimizeToTray);
        Assert.False(result.Settings.NotificationsEnabled);
        Assert.Equal("device-1", result.Settings.PreferredLightingDeviceId);
        Assert.Equal(AppLanguage.System, result.Settings.Language);
        Assert.Equal(TrayMenuOptions.Default, result.Settings.TrayMenu);
        Assert.Null(result.RecoveryMessage);
    }

    [Fact]
    public async Task SchemaTwo_ExactLegacyDefaultPointsReceiveTheVividPaletteAndPreserveSmoothing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"schemaVersion":2,"profile":{"points":[{"temperature":35,"color":{"r":0,"g":140,"b":255}},{"temperature":65,"color":{"r":255,"g":216,"b":0}},{"temperature":85,"color":{"r":255,"g":24,"b":0}}],"smoothingSeconds":1.3},"isModeEnabled":true,"isAutostartEnabled":false,"minimizeToTray":true,"notificationsEnabled":true,"preferredLightingDeviceId":null,"language":0,"trayMenu":{"showTemperature":true,"showOpen":true,"showModeToggle":true}}
            """, TestContext.Current.CancellationToken);
        var store = new JsonSettingsStore(path, TimeProvider.System);

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [new ThermalPoint(35, new RgbColor(0x00, 0x6B, 0xFF)), new(65, new RgbColor(0xD0, 0x00, 0xFF)), new(85, new RgbColor(0xFF, 0x00, 0x00))],
            result.Settings.Profile.Points);
        Assert.Equal(1.3, result.Settings.Profile.SmoothingSeconds);
        Assert.True(result.Settings.IsModeEnabled);
        Assert.Null(result.RecoveryMessage);
    }

    [Fact]
    public async Task SchemaTwo_CustomProfileContainingLegacyYellowIsPreservedExactly()
    {
        var path = Path.Combine(_directory, "settings.json");
        var expected = AppSettings.Default with
        {
            Profile = ThermalProfile.Create(
            [
                new(35, new RgbColor(0x00, 0x6B, 0xFF)),
                new(65, new RgbColor(0xFF, 0xD8, 0x00)),
                new(85, new RgbColor(0xFF, 0x18, 0x00)),
                new(100, new RgbColor(0xFF, 0x00, 0x00)),
            ], 0.9),
        };
        var store = new JsonSettingsStore(path, TimeProvider.System);
        await store.SaveAsync(expected, TestContext.Current.CancellationToken);

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Settings);
        Assert.Equal(new RgbColor(0xFF, 0xD8, 0x00), result.Settings.Profile.Points[1].Color);
    }

    [Fact]
    public async Task SchemaTwo_SaveThenLoad_RoundTripsArbitraryProfilePointsAndLanguage()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path, TimeProvider.System);
        var expected = new AppSettings(
            2,
            ThermalProfile.Create(
            [
                new ThermalPoint(20, new RgbColor(0x00, 0x00, 0xFF)),
                new ThermalPoint(36, new RgbColor(0x00, 0xFF, 0xFF)),
                new ThermalPoint(59, new RgbColor(0x00, 0xFF, 0x00)),
                new ThermalPoint(77, new RgbColor(0xFF, 0xFF, 0x00)),
                new ThermalPoint(95, new RgbColor(0xFF, 0x00, 0x00))
            ],
            1.3),
            true,
            true,
            false,
            false,
            "device-1",
            AppLanguage.Russian,
            new TrayMenuOptions(false, true, false));

        await store.SaveAsync(expected, TestContext.Current.CancellationToken);
        var actual = (await store.LoadAsync(TestContext.Current.CancellationToken)).Settings;

        Assert.Equal(expected, actual);
        Assert.Equal(5, actual.Profile.Points.Count);
        Assert.Equal(AppLanguage.Russian, actual.Language);
    }

    [Fact]
    public async Task UnknownSchema_IsQuarantinedAndDefaultsAreReturned()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":42}""", TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var store = new JsonSettingsStore(path, clock);

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.corrupt-20260819-120000.json")));
        Assert.Equal("Настройки были повреждены и сброшены", result.RecoveryMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

public sealed class FakeTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }
}
