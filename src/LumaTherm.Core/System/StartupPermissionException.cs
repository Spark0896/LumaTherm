namespace LumaTherm.Core.System;

public enum StartupBlockReason { User, Policy }

public sealed class StartupPermissionException(StartupBlockReason reason)
    : InvalidOperationException("Windows заблокировала автозапуск LumaTherm.")
{
    public StartupBlockReason Reason { get; } = reason;
}
