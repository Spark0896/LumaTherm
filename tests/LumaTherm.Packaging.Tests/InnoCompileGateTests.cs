using System.Text.Json;

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

    [Fact]
    public void ContainmentRejectsSiblingThatSharesTheParentPrefix()
    {
        var parent = Path.Combine(Path.GetTempPath(), "LumaTherm-owned-root");
        var sibling = parent + "-sibling";

        var result = RunContainmentProbe(Path.Combine(sibling, "child.txt"), parent, emitJson: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("escaped its owned temporary root", result.StandardError + result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContainmentRejectsTheExactParentBecauseGateCallersRequireDescendants()
    {
        var parent = Path.Combine(Path.GetTempPath(), "LumaTherm-owned-root");

        var result = RunContainmentProbe(parent, parent, emitJson: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("escaped its owned temporary root", result.StandardError + result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaseInsensitiveContainedPathLeavesStdoutAsExactlyOneJsonDocument()
    {
        var parent = Path.Combine(Path.GetTempPath(), "LumaTherm-Case-Root");
        var child = Path.Combine(parent.ToUpperInvariant(), "nested", "child.txt");

        var result = RunContainmentProbe(child, parent.ToLowerInvariant(), emitJson: true);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        Assert.Equal("{\"contained\":true}", result.StandardOutput.TrimEnd('\r', '\n'));
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.True(json.RootElement.GetProperty("contained").GetBoolean());
    }

    private static PowerShellResult RunContainmentProbe(string child, string parent, bool emitJson)
    {
        var probeRoot = Path.Combine(Path.GetTempPath(), "LumaTherm-containment-probe", Guid.NewGuid().ToString("N"));
        var probePath = Path.Combine(probeRoot, "Invoke-ContainmentProbe.ps1");
        Directory.CreateDirectory(probeRoot);
        File.WriteAllText(probePath, """
            param(
                [Parameter(Mandatory = $true)][string] $GatePath,
                [Parameter(Mandatory = $true)][string] $Child,
                [Parameter(Mandatory = $true)][string] $Parent,
                [switch] $EmitJson
            )
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                $GatePath, [ref] $tokens, [ref] $errors)
            if ($errors.Count -ne 0) { throw ($errors.Message -join '; ') }
            $functionAst = $ast.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ceq 'Assert-ContainedPath'
            }, $true)
            if ($null -eq $functionAst) { throw 'Assert-ContainedPath was not found.' }
            . ([scriptblock]::Create($functionAst.Extent.Text))
            Assert-ContainedPath -Child $Child -Parent $Parent
            if ($EmitJson) {
                [pscustomobject]@{ contained = $true } | ConvertTo-Json -Compress
            }
            """);

        try
        {
            var arguments = new List<string>
            {
                "-GatePath", GatePath,
                "-Child", child,
                "-Parent", parent,
            };
            if (emitJson) arguments.Add("-EmitJson");
            return PowerShellTestHost.Run(probePath, arguments, timeout: TimeSpan.FromSeconds(15));
        }
        finally
        {
            Directory.Delete(probeRoot, recursive: true);
        }
    }

    private static string[] ValidationRoots() => Directory
        .EnumerateDirectories(Path.GetTempPath(), "LumaTherm-inno-validation-*")
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
