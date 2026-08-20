using System.Diagnostics;

namespace LumaTherm.Packaging.Tests;

public sealed class BoundedProcessTestHostTests
{
    [Fact]
    public void TimeoutKillsChildTreeAndReleasesItsFileLockWithinTheBound()
    {
        var directory = Path.Combine(RepositoryLayout.Root, "artifacts", "bounded-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, "child.lock");
        var childScript = Path.Combine(directory, "child.ps1");
        var rootScript = Path.Combine(directory, "root.ps1");
        File.WriteAllText(childScript, "$stream = [IO.File]::Open($args[0], 'OpenOrCreate', 'ReadWrite', 'None'); try { Start-Sleep -Seconds 30 } finally { $stream.Dispose() }");
        File.WriteAllText(rootScript, "$child = Start-Process powershell.exe -ArgumentList @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$args[0],$args[1]) -PassThru; while (!(Test-Path -LiteralPath $args[1])) { Start-Sleep -Milliseconds 10 }; Start-Sleep -Seconds 30");

        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", rootScript, childScript, lockPath },
            };
            var stopwatch = Stopwatch.StartNew();

            BoundedProcessResult? result = null;
            var observed = Record.Exception(() =>
                result = BoundedProcessTestHost.Run(startInfo, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)));
            Assert.True(observed is TimeoutException, $"Process unexpectedly exited {result?.ExitCode}: {result?.StandardError}{result?.StandardOutput}");
            var error = (TimeoutException)observed!;
            Assert.Contains("3000", error.Message, StringComparison.Ordinal);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Bounded process termination took {stopwatch.Elapsed}.");
            using var released = File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
