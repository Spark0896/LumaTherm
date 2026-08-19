namespace LumaTherm.Core.System;

public interface IStartupService
{
    Task<bool> GetEnabledAsync(CancellationToken cancellationToken);
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}
