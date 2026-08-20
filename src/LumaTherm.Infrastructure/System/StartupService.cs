using System.Runtime.InteropServices;
using LumaTherm.Core.System;
using Windows.ApplicationModel;

namespace LumaTherm.Infrastructure.System;

public sealed class StartupService : IStartupService
{
    private readonly IPackageIdentityProbe _identity;
    private readonly IStartupService _packaged;
    private readonly IStartupService _portable;

    public StartupService(IPackageIdentityProbe identity, IStartupService packaged, IStartupService portable)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _packaged = packaged ?? throw new ArgumentNullException(nameof(packaged));
        _portable = portable ?? throw new ArgumentNullException(nameof(portable));
    }

    public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Select().GetEnabledAsync(cancellationToken);

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Select().SetEnabledAsync(enabled, cancellationToken);

    private IStartupService Select()
    {
        try
        {
            return _identity.IsPackaged() ? _packaged : _portable;
        }
        catch (PackageIdentityUnavailableException)
        {
            return _portable;
        }
    }
}

public sealed class WindowsPackageIdentityProbe : IPackageIdentityProbe
{
    public bool IsPackaged()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(Package.Current.Id.Name);
        }
        catch (InvalidOperationException exception)
        {
            throw new PackageIdentityUnavailableException(exception);
        }
        catch (COMException exception)
        {
            throw new PackageIdentityUnavailableException(exception);
        }
    }
}
