namespace LumaTherm.Core.Updates;

public sealed record ReleaseInfo(
    SemanticVersion Version,
    Uri ReleasePageUrl,
    Uri DownloadUrl);
