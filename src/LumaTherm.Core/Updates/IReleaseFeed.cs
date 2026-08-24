namespace LumaTherm.Core.Updates;

public interface IReleaseFeed
{
    Task<ReleaseInfo> GetLatestStableAsync(CancellationToken cancellationToken);
}
