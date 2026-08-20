using System.Diagnostics;

namespace LumaTherm.Packaging.Tests;

public sealed class PowerShellTestHostTests
{
    [Fact]
    public void TimeoutKillsTheProcessTreeWithoutBlockingOnRedirectedStreams()
    {
        var directory = Path.Combine(RepositoryLayout.Root, "artifacts", "powershell-host-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "hang.ps1");
        File.WriteAllText(script, "$child = Start-Process powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30' -PassThru; Write-Output $child.Id; Start-Sleep -Seconds 30");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            Assert.Throws<TimeoutException>(() => PowerShellTestHost.Run(script, Array.Empty<string>(), timeout: TimeSpan.FromMilliseconds(250)));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Timeout took {stopwatch.Elapsed}.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
