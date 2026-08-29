using System.Diagnostics;

namespace LumaTherm.Packaging.Tests;

internal sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);

internal static class PowerShellTestHost
{
    public static PowerShellResult Run(
        string script,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        bool invokeViaCommand = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        if (invokeViaCommand)
        {
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("&");
            startInfo.ArgumentList.Add(script);
        }
        else
        {
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell did not start.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5_000))
            {
                throw new TimeoutException("PowerShell process tree did not terminate after timeout.");
            }

            if (!Task.WaitAll(new Task[] { outputTask, errorTask }, TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("PowerShell redirected streams did not drain after process-tree termination.");
            }
            throw new TimeoutException($"PowerShell script exceeded its {effectiveTimeout.TotalMilliseconds:F0} ms test bound.");
        }

        process.WaitForExit();
        if (!Task.WaitAll(new Task[] { outputTask, errorTask }, TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("PowerShell redirected streams did not drain after process exit.");
        }
        return new PowerShellResult(process.ExitCode, outputTask.Result, errorTask.Result);
    }
}
