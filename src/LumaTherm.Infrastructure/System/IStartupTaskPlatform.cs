namespace LumaTherm.Infrastructure.System;

public enum StartupTaskState
{
    Disabled,
    DisabledByUser,
    Enabled,
    DisabledByPolicy,
    EnabledByPolicy,
}

public interface IStartupTaskPlatform
{
    Task<StartupTaskState> GetStateAsync(CancellationToken cancellationToken);
    Task<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken);
    Task DisableAsync(CancellationToken cancellationToken);
}

public interface IRegistryRunPlatform
{
    string? Read(string keyPath, string valueName);
    void Write(string keyPath, string valueName, string value);
    void Delete(string keyPath, string valueName);
}

public interface IPackageIdentityProbe
{
    bool IsPackaged();
}

public sealed class PackageIdentityUnavailableException : Exception
{
    public PackageIdentityUnavailableException() { }
    public PackageIdentityUnavailableException(Exception innerException)
        : base("Package identity is unavailable.", innerException) { }
}
