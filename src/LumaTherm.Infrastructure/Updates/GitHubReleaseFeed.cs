using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LumaTherm.Core.Updates;

namespace LumaTherm.Infrastructure.Updates;

public sealed class GitHubReleaseFeed : IReleaseFeed, IDisposable
{
    internal const int MaximumResponseBytes = 64 * 1024;
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/Spark0896/LumaTherm/releases/latest");
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private bool _disposed;

    public GitHubReleaseFeed()
        : this(new HttpClient(), ownsClient: true)
    {
    }

    public GitHubReleaseFeed(HttpClient client, bool ownsClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        _client.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<ReleaseInfo> GetLatestStableAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LumaTherm", "1.2.0"));
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);

        GitHubReleaseDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<GitHubReleaseDocument>(payload);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("GitHub returned malformed release data.", exception);
        }

        if (document is null || document.Draft is not false || document.Prerelease is not false)
        {
            throw new InvalidDataException("GitHub did not return a stable release.");
        }

        SemanticVersion version;
        try
        {
            version = SemanticVersion.Parse(document.TagName ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The release tag is not a stable semantic version.", exception);
        }

        var page = ParseHttpsUri(document.HtmlUrl, "release page");
        var asset = document.Assets?.FirstOrDefault()
            ?? throw new InvalidDataException("The release has no downloadable asset.");
        var download = ParseHttpsUri(asset.BrowserDownloadUrl, "release asset");
        return new ReleaseInfo(version, page, download);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient) _client.Dispose();
    }

    private static Uri ParseHttpsUri(string? value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The {field} URL must use HTTPS.");
        }
        return uri;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException("The GitHub release response is too large.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var remaining = MaximumResponseBytes + 1 - checked((int)buffer.Length);
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaximumResponseBytes)
            {
                throw new InvalidDataException("The GitHub release response is too large.");
            }
        }
        return buffer.ToArray();
    }

    private sealed class GitHubReleaseDocument
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }
        [JsonPropertyName("draft")]
        public bool? Draft { get; init; }
        [JsonPropertyName("prerelease")]
        public bool? Prerelease { get; init; }
        [JsonPropertyName("assets")]
        public GitHubAssetDocument[]? Assets { get; init; }
    }

    private sealed class GitHubAssetDocument
    {
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}
