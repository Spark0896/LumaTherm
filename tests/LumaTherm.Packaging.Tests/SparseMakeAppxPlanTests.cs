using System.Text.Json;

namespace LumaTherm.Packaging.Tests;

public sealed class SparseMakeAppxPlanTests
{
    [Fact]
    public void ReleasePlanDisablesFileValidationForIdentityOnlySparsePackage()
    {
        var script = Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1");
        var result = PowerShellTestHost.Run(script, new[] { "-Mode", "Plan" },
            timeout: TimeSpan.FromSeconds(30), invokeViaCommand: true);
        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var plan = JsonDocument.Parse(result.StandardOutput);
        var command = Assert.Single(plan.RootElement.GetProperty("plannedCommands").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "make-sparse-package");
        Assert.Contains("/nv", command.GetProperty("arguments").EnumerateArray()
            .Select(item => item.GetString()));
    }
}
