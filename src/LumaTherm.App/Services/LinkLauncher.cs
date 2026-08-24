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
            || uri.OriginalString.Contains('\\')
            || ContainsTraversalSyntax(uri.OriginalString))
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

    private static bool ContainsTraversalSyntax(string original)
    {
        var schemeSeparator = original.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0) return true;
        var pathStart = original.IndexOf('/', schemeSeparator + 3);
        if (pathStart < 0) return false;

        var pathEnd = original.Length;
        var queryStart = original.IndexOf('?', pathStart);
        if (queryStart >= 0) pathEnd = queryStart;
        var fragmentStart = original.IndexOf('#', pathStart);
        if (fragmentStart >= 0 && fragmentStart < pathEnd) pathEnd = fragmentStart;
        var path = original[pathStart..pathEnd];

        for (var decodingPass = 0; decodingPass < 3; decodingPass++)
        {
            if (path.Contains('\\') || HasDotSegment(path)) return true;

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(path);
            }
            catch (UriFormatException)
            {
                return true;
            }

            if (decoded.Equals(path, StringComparison.Ordinal)) return false;
            path = decoded;
        }

        return path.Contains('\\') || HasDotSegment(path);
    }

    private static bool HasDotSegment(string path) =>
        path.Split('/', StringSplitOptions.None)
            .Any(segment => segment is "." or "..");
}
