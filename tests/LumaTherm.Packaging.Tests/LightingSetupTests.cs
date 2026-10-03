using Microsoft.Win32;
using System.Runtime.Versioning;

namespace LumaTherm.Packaging.Tests;

[SupportedOSPlatform("windows")]
public sealed class LightingSetupTests : IDisposable
{
    private const string Family = "LumaTherm_jzd30fs6ag6cm";
    private readonly string _root = @"Software\LumaTherm.Tests\" + Guid.NewGuid().ToString("N");
    private PowerShellResult Run(string action = "Configure", bool testEnvironment = true, bool backupFailure = false, bool interrupt = false, bool device = false) => PowerShellTestHost.Run(
        Path.Combine(RepositoryLayout.Root, "scripts", "Set-LumaThermLighting.ps1"),
        new[] { "-Action", action, "-FamilyName", Family, "-RegistryRootForTest", _root }
            .Concat(backupFailure ? ["-BackupFailureForTest"] : Array.Empty<string>())
            .Concat(interrupt ? ["-InterruptAfterFirstWriteForTest"] : Array.Empty<string>())
            .Concat(device ? ["-InterruptTargetForTest", "Device"] : Array.Empty<string>()).ToArray(),
        testEnvironment ? new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1" } : null);
    private void Pass(string action = "Configure")
    {
        var result = Run(action);
        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
    }

    [Fact]
    public void SetupWritesRealUserPreferencesForCurrentAndFutureDevicesAndPreservesEffects()
    {
        Seed(_root + @"\Providers", "WindowsLighting", "OtherApp", Family);
        Seed(_root + @"\Devices\device-a\Providers", "OtherApp", "WindowsLighting", Family);
        using (var key = Registry.CurrentUser.CreateSubKey(_root + @"\Devices\device-a")) key.SetValue("Brightness", 73);
        Pass();
        Assert.Equal([Family, "WindowsLighting", "OtherApp"], Order(_root + @"\Providers"));
        Assert.Equal([Family, "OtherApp", "WindowsLighting"], Order(_root + @"\Devices\device-a\Providers"));
        using var device = Registry.CurrentUser.OpenSubKey(_root + @"\Devices\device-a");
        Assert.Equal(73, device!.GetValue("Brightness"));
        Assert.Equal(1, device.GetValue("AmbientLightingEnabled"));
        Assert.Equal(0, device.GetValue("ControlledByForegroundApp"));
    }

    [Fact]
    public void ReinstallKeepsOriginalBackupAndUninstallRestoresOrderAndToggleValues()
    {
        Seed(_root + @"\Providers", "WindowsLighting", "OtherApp");
        using (var key = Registry.CurrentUser.CreateSubKey(_root))
        { key.SetValue("AmbientLightingEnabled", 0); key.SetValue("ControlledByForegroundApp", 1); }
        Pass(); Pass(); Pass("Restore");
        Assert.Equal(["WindowsLighting", "OtherApp"], Order(_root + @"\Providers"));
        using var restored = Registry.CurrentUser.OpenSubKey(_root);
        Assert.Equal(0, restored!.GetValue("AmbientLightingEnabled"));
        Assert.Equal(1, restored.GetValue("ControlledByForegroundApp"));
        using var installation = Registry.CurrentUser.OpenSubKey(_root + @"\Installation");
        Assert.Null(installation!.GetValue("LightingBackup"));
    }

    [Fact]
    public void UninstallRespectsAnOrderChangedByTheUserAfterInstallation()
    {
        Seed(_root + @"\Providers", "WindowsLighting", "OtherApp");
        Pass(); Seed(_root + @"\Providers", "OtherApp", Family, "WindowsLighting"); Pass("Restore");
        Assert.Equal(["OtherApp", Family, "WindowsLighting"], Order(_root + @"\Providers"));
    }

    [Fact]
    public void RepairRebasesAnEditedOrderAndUninstallPreservesTheNewUserPreference()
    {
        Seed(_root + @"\Providers", "WindowsLighting", "OtherApp");
        Pass(); Seed(_root + @"\Providers", "OtherApp", Family, "WindowsLighting"); Pass();
        Assert.Equal([Family, "OtherApp", "WindowsLighting"], Order(_root + @"\Providers"));
        Pass("Restore");
        Assert.Equal(["OtherApp", "WindowsLighting"], Order(_root + @"\Providers"));
    }

    [Fact]
    public void UnknownProviderTypeFailsBeforeAnyPreferenceIsChanged()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_root + @"\Providers")) key.SetValue("1", 7);
        Assert.NotEqual(0, Run().ExitCode);
        using var root = Registry.CurrentUser.OpenSubKey(_root);
        Assert.Null(root!.GetValue("AmbientLightingEnabled"));
    }

    [Fact]
    public void FirstInstallAddsWindowsFallbackWhenNoProviderListExists()
    { Pass(); Assert.Equal([Family, "WindowsLighting"], Order(_root + @"\Providers")); }

    [Fact]
    public void BackupFailureRollsBackEveryChangedPreference()
    {
        Seed(_root + @"\Providers", "WindowsLighting", "OtherApp");
        Assert.NotEqual(0, Run(backupFailure: true).ExitCode);
        Assert.Equal(["WindowsLighting", "OtherApp"], Order(_root + @"\Providers"));
        using var root = Registry.CurrentUser.OpenSubKey(_root);
        Assert.Null(root!.GetValue("AmbientLightingEnabled"));
        Assert.Null(root.GetValue("ControlledByForegroundApp"));
    }

    [Fact]
    public void ModifiedBackupCannotWriteOutsideLightingPreferences()
    {
        using var installation = Registry.CurrentUser.CreateSubKey(_root + @"\Installation");
        installation.SetValue("LightingBackup",
            "{\"Family\":\"LumaTherm_jzd30fs6ag6cm\",\"Changes\":[{\"Key\":\"Software\\\\Elsewhere\",\"Name\":\"1\",\"Before\":null,\"After\":{\"Kind\":\"String\",\"Text\":\"OtherApp\"}}]}");
        Assert.NotEqual(0, Run().ExitCode);
        Assert.Empty(OrderOrEmpty(_root + @"\Providers"));
    }

    [Fact]
    public void UnknownBackupTypeFailsBeforeLightingPreferencesChange()
    {
        using var installation = Registry.CurrentUser.CreateSubKey(_root + @"\Installation");
        installation.SetValue("LightingBackup", 7);
        Assert.NotEqual(0, Run().ExitCode);
        using var root = Registry.CurrentUser.OpenSubKey(_root);
        Assert.Null(root!.GetValue("AmbientLightingEnabled"));
        Assert.Empty(OrderOrEmpty(_root + @"\Providers"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedConfigureRecoversItsActualBeforeStateOnTheNextRestore(bool device)
    {
        var path = _root + (device ? @"\Devices\device-a\Providers" : @"\Providers");
        Seed(path, "WindowsLighting", "OtherApp");
        Assert.Equal(42, Run(interrupt: true, device: device).ExitCode);
        Pass("Restore");
        Assert.Equal(["WindowsLighting", "OtherApp"], Order(path));
        using var root = Registry.CurrentUser.OpenSubKey(_root);
        Assert.Null(root!.GetValue("AmbientLightingEnabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedRestoreCompletesOnTheNextRestore(bool device)
    {
        var path = _root + (device ? @"\Devices\device-a\Providers" : @"\Providers");
        Seed(path, "WindowsLighting", "OtherApp");
        Pass(); Assert.Equal(42, Run("Restore", interrupt: true, device: device).ExitCode);
        Pass("Restore");
        Assert.Equal(["WindowsLighting", "OtherApp"], Order(path));
        using var installation = Registry.CurrentUser.OpenSubKey(_root + @"\Installation");
        Assert.Null(installation!.GetValue("LightingBackup"));
    }

    [Fact]
    public void InterruptedRecoveryRetainsItsBackupAndUserChangeOnConflict()
    {
        Seed(_root + @"\Providers", "WindowsLighting", "OtherApp");
        Assert.Equal(42, Run(interrupt: true).ExitCode);
        Seed(_root + @"\Providers", "NewUserChoice", "OtherApp");
        Assert.NotEqual(0, Run("Restore").ExitCode);
        Assert.Equal(["NewUserChoice", "OtherApp"], Order(_root + @"\Providers"));
        using var installation = Registry.CurrentUser.OpenSubKey(_root + @"\Installation");
        Assert.Contains("Pending", (string)installation!.GetValue("LightingBackup")!);
    }

    [Fact]
    public void ProductionInvocationRejectsTestOverridesBeforeAnyMutation()
    { Assert.NotEqual(0, Run(testEnvironment: false).ExitCode); Assert.Null(Registry.CurrentUser.OpenSubKey(_root)); }

    private static void Seed(string path, params string[] providers)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        for (var i = 0; i < providers.Length; i++) key.SetValue((i + 1).ToString(), providers[i]);
    }
    private static string[] Order(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key!.GetValueNames().Where(name => int.TryParse(name, out _)).OrderBy(int.Parse)
            .Select(name => (string)key.GetValue(name)!).ToArray();
    }
    private static string[] OrderOrEmpty(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is null ? [] : key.GetValueNames();
    }
    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, false);
    }
}
