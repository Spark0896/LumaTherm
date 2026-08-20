using System.Diagnostics;

namespace LumaTherm.Packaging.Tests;

internal sealed record BoundedProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class BoundedProcessTestHost
{
    public static BoundedProcessResult Run(ProcessStartInfo startInfo, TimeSpan timeout, TimeSpan terminationTimeout)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Process did not start: {startInfo.FileName}");
        var outputTask = startInfo.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
        var errorTask = startInfo.RedirectStandardError ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);
        var timeoutMilliseconds = ToBoundedMilliseconds(timeout);
        var terminationMilliseconds = ToBoundedMilliseconds(terminationTimeout);

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            Exception? killFailure = null;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception)
            {
                killFailure = exception;
            }

            if (!process.WaitForExit(terminationMilliseconds))
            {
                throw new TimeoutException($"Process tree for {startInfo.FileName} did not terminate within {terminationMilliseconds} ms after its {timeoutMilliseconds} ms deadline.", killFailure);
            }
            if (!Task.WaitAll(new Task[] { outputTask, errorTask }, terminationTimeout))
            {
                throw new TimeoutException($"Process streams for {startInfo.FileName} did not drain within {terminationMilliseconds} ms after process-tree termination.");
            }
            throw new TimeoutException($"Process {startInfo.FileName} exceeded its {timeoutMilliseconds} ms deadline and its process tree was terminated.", killFailure);
        }

        if (!Task.WaitAll(new Task[] { outputTask, errorTask }, terminationTimeout))
        {
            throw new TimeoutException($"Process streams for {startInfo.FileName} did not drain within {terminationMilliseconds} ms after exit.");
        }
        return new BoundedProcessResult(process.ExitCode, outputTask.Result, errorTask.Result);
    }

    private static int ToBoundedMilliseconds(TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Process deadlines must be positive and fit in Int32 milliseconds.");
        }
        return Math.Max(1, (int)Math.Ceiling(value.TotalMilliseconds));
    }
}
