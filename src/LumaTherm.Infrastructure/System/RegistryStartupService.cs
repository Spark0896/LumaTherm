using LumaTherm.Core.System;
using Microsoft.Win32;

namespace LumaTherm.Infrastructure.System;

public sealed class RegistryStartupService : IStartupService
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LumaTherm";
    private readonly IRegistryRunPlatform _platform;
    private readonly string _command;

    public RegistryStartupService(IRegistryRunPlatform platform, string executablePath)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _command = $"\"{executablePath}\" --autostart";
    }

    public Task<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(string.Equals(_platform.Read(RunKeyPath, ValueName), _command, StringComparison.Ordinal));
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (enabled)
        {
            _platform.Write(RunKeyPath, ValueName, _command);
        }
        else
        {
            _platform.Delete(RunKeyPath, ValueName);
        }

        return Task.CompletedTask;
    }
}

public sealed class WindowsRegistryRunPlatform : IRegistryRunPlatform
{
    public string? Read(string keyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string keyPath, string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void Delete(string keyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
