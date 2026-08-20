namespace LumaTherm.Core.System;

public interface ISessionSentinel
{
    Task<bool> BeginAsync(CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}
