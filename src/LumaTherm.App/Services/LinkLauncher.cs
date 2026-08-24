using System.Diagnostics;

namespace LumaTherm.App.Services;

public sealed class LinkLauncher : ILinkLauncher
{
    private const string RepositoryRoot = "/Spark0896/LumaTherm/";
    private readonly Action<ProcessStartInfo> _start;

    public LinkLauncher()
        : this(info => Process.Start(info))
    {
    }

    internal LinkLauncher(Action<ProcessStartInfo> start) =>
        _start = start ?? throw new ArgumentNullException(nameof(start));

    public void Open(Uri uri)
    {
        var normalized = ValidateAndNormalize(uri);
        _start(new ProcessStartInfo(normalized.AbsoluteUri) { UseShellExecute = true });
    }

    private static Uri ValidateAndNormalize(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.OriginalString.Contains('\\'))
        {
            throw new ArgumentException("Only HTTPS links to the LumaTherm GitHub repository are allowed.", nameof(uri));
        }

        var escapedPath = "/" + uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (escapedPath.Contains('%')
            || (!escapedPath.Equals(RepositoryRoot[..^1], StringComparison.Ordinal)
                && !escapedPath.StartsWith(RepositoryRoot, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The link must remain under the LumaTherm GitHub repository.", nameof(uri));
        }

        if (escapedPath.Equals(RepositoryRoot[..^1], StringComparison.Ordinal))
        {
            var builder = new UriBuilder(uri) { Path = RepositoryRoot };
            return builder.Uri;
        }
        return uri;
    }
}
