namespace LumaTherm.Packaging.Tests;

public sealed class InnoCompileGateTests
{
    private static string GatePath => Path.Combine(
        RepositoryLayout.Root, "scripts", "Test-LumaThermInnoCompile.ps1");

    [Fact]
    public void InnoCompileGateFailsWhenOfficialInstallerInputIsOmitted()
    {
        var result = PowerShellTestHost.Run(GatePath, Array.Empty<string>(), timeout: TimeSpan.FromSeconds(15));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("OfficialInstallerPath", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void InnoCompileGateRejectsUnpinnedExecutableBeforeExecutionAndCleansTemp()
    {
        var fakeRoot = Path.Combine(Path.GetTempPath(), "LumaTherm-fake-inno", Guid.NewGuid().ToString("N"));
        var fakeInstaller = Path.Combine(fakeRoot, "innosetup-6.7.3.exe");
        var sentinel = Path.Combine(fakeRoot, "executed.txt");
        Directory.CreateDirectory(fakeRoot);
        File.WriteAllText(fakeInstaller, $"this is not an executable; if execution is attempted the test must fail; {sentinel}");
        var before = ValidationRoots();

        try
        {
            var result = PowerShellTestHost.Run(
                GatePath,
                new[] { "-OfficialInstallerPath", fakeInstaller },
                timeout: TimeSpan.FromSeconds(15));

            Assert.NotEqual(0, result.ExitCode);
            var processOutput = result.StandardError + result.StandardOutput;
            Assert.True(processOutput.Contains("official Inno Setup 6.7.3 SHA-256 mismatch",
                StringComparison.OrdinalIgnoreCase), processOutput);
            Assert.Contains("https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe",
                processOutput, StringComparison.Ordinal);
            Assert.Contains("9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732",
                processOutput, StringComparison.Ordinal);
            Assert.False(File.Exists(sentinel));
            Assert.Equal(before, ValidationRoots());
        }
        finally
        {
            Directory.Delete(fakeRoot, recursive: true);
        }
    }

    private static string[] ValidationRoots() => Directory
        .EnumerateDirectories(Path.GetTempPath(), "LumaTherm-inno-validation-*")
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
