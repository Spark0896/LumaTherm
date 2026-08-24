using System.Net;
using System.Text;
using LumaTherm.Infrastructure.Updates;

namespace LumaTherm.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseFeedTests
{
    [Fact]
    public async Task GetLatestStableAsync_RequestsThePinnedRepositoryAndParsesStableHttpsRelease()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(JsonResponse(StableJson));
        });
        using var client = new HttpClient(handler);
        using var feed = new GitHubReleaseFeed(client);

        var release = await feed.GetLatestStableAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("https://api.github.com/repos/Spark0896/LumaTherm/releases/latest", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("LumaTherm/1.1.0", Assert.Single(captured.Headers.UserAgent).ToString());
        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
        Assert.Equal("1.2.0", release.Version.ToString());
        Assert.Equal("https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0", release.ReleasePageUrl.AbsoluteUri);
        Assert.Equal("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-Setup.exe", release.DownloadUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GetLatestStableAsync_RejectsDraftAndPrereleaseResponses(bool draft, bool prerelease)
    {
        var json = StableJson.Replace("\"draft\":false", $"\"draft\":{draft.ToString().ToLowerInvariant()}", StringComparison.Ordinal)
            .Replace("\"prerelease\":false", $"\"prerelease\":{prerelease.ToString().ToLowerInvariant()}", StringComparison.Ordinal);
        using var feed = FeedReturning(JsonResponse(json));

        await Assert.ThrowsAsync<InvalidDataException>(() => feed.GetLatestStableAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"draft\":false")]
    [InlineData("\"prerelease\":false")]
    [InlineData("\"draft\":null,\"prerelease\":false")]
    [InlineData("\"draft\":false,\"prerelease\":null")]
    [InlineData("\"draft\":\"false\",\"prerelease\":false")]
    [InlineData("\"draft\":false,\"prerelease\":0")]
    public async Task GetLatestStableAsync_RejectsMissingNullOrNonBooleanStabilityFields(string stabilityFields)
    {
        using var feed = FeedReturning(JsonResponse(ReleaseJsonWith(stabilityFields)));

        await Assert.ThrowsAsync<InvalidDataException>(() => feed.GetLatestStableAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("http://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0", "https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm.exe")]
    [InlineData("https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0", "http://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm.exe")]
    public async Task GetLatestStableAsync_RejectsNonHttpsBrowserOrAssetUrls(string pageUrl, string downloadUrl)
    {
        var json = StableJson.Replace("https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0", pageUrl, StringComparison.Ordinal)
            .Replace("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-Setup.exe", downloadUrl, StringComparison.Ordinal);
        using var feed = FeedReturning(JsonResponse(json));

        await Assert.ThrowsAsync<InvalidDataException>(() => feed.GetLatestStableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestStableAsync_RejectsResponseLargerThanTheBound()
    {
        var content = new ByteArrayContent(new byte[64 * 1024 + 1]);
        using var feed = FeedReturning(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        await Assert.ThrowsAsync<InvalidDataException>(() => feed.GetLatestStableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestStableAsync_HonorsCallerCancellation()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(StableJson);
        });
        using var client = new HttpClient(handler);
        using var feed = new GitHubReleaseFeed(client);
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feed.GetLatestStableAsync(cancellation.Token));
    }

    [Fact]
    public async Task GetLatestStableAsync_RejectsMalformedJson()
    {
        using var feed = FeedReturning(JsonResponse("{ definitely-not-json"));

        await Assert.ThrowsAsync<InvalidDataException>(() => feed.GetLatestStableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestStableAsync_RejectsNonSuccessStatus()
    {
        using var feed = FeedReturning(new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(() => feed.GetLatestStableAsync(CancellationToken.None));
    }

    private static GitHubReleaseFeed FeedReturning(HttpResponseMessage response)
    {
        var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(response)));
        return new GitHubReleaseFeed(client, ownsClient: true);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string ReleaseJsonWith(string stabilityFields) => $$"""
        {
          "html_url":"https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0",
          "tag_name":"v1.2.0",
          {{stabilityFields}}
          {{(stabilityFields.Length == 0 ? string.Empty : ",")}}
          "assets":[{
            "name":"LumaTherm-Setup.exe",
            "browser_download_url":"https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-Setup.exe"
          }]
        }
        """;

    private const string StableJson = """
        {
          "url":"https://api.github.com/repos/Spark0896/LumaTherm/releases/1",
          "html_url":"https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0",
          "tag_name":"v1.2.0",
          "draft":false,
          "prerelease":false,
          "assets":[{
            "name":"LumaTherm-Setup.exe",
            "browser_download_url":"https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-Setup.exe"
          }]
        }
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
