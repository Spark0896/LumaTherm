using LumaTherm.Core.System;
using Windows.ApplicationModel;

namespace LumaTherm.Infrastructure.System;

public sealed class PackagedStartupService : IStartupService
{
    private readonly IStartupTaskPlatform _platform;

    public PackagedStartupService(IStartupTaskPlatform platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    }

    public async Task<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _platform.GetStateAsync(cancellationToken) is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled)
        {
            await _platform.DisableAsync(cancellationToken);
            return;
        }

        var result = await _platform.RequestEnableAsync(cancellationToken);
        if (result == StartupTaskState.DisabledByUser)
            throw new StartupPermissionException(StartupBlockReason.User);
        if (result == StartupTaskState.DisabledByPolicy)
            throw new StartupPermissionException(StartupBlockReason.Policy);
        if (result is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
        {
            throw new InvalidOperationException("Windows не разрешила включить автозапуск LumaTherm.");
        }
    }
}

public sealed class WindowsStartupTaskPlatform : IStartupTaskPlatform
{
    public async Task<StartupTaskState> GetStateAsync(CancellationToken cancellationToken)
    {
        var task = await StartupTask.GetAsync("LumaThermStartup").AsTask(cancellationToken);
        return Map(task.State);
    }

    public async Task<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
    {
        var task = await StartupTask.GetAsync("LumaThermStartup").AsTask(cancellationToken);
        return Map(await task.RequestEnableAsync().AsTask(cancellationToken));
    }

    public async Task DisableAsync(CancellationToken cancellationToken)
    {
        var task = await StartupTask.GetAsync("LumaThermStartup").AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        task.Disable();
    }

    private static StartupTaskState Map(Windows.ApplicationModel.StartupTaskState state) => state switch
    {
        Windows.ApplicationModel.StartupTaskState.Enabled => StartupTaskState.Enabled,
        Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupTaskState.EnabledByPolicy,
        Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupTaskState.DisabledByUser,
        Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupTaskState.DisabledByPolicy,
        _ => StartupTaskState.Disabled,
    };
}
