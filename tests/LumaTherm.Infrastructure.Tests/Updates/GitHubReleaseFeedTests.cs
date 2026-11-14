using System.Net;
using System.Text;
using System.Text.Json.Nodes;
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
        Assert.Equal("LumaTherm/1.2.1", Assert.Single(captured.Headers.UserAgent).ToString());
        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
        Assert.Equal("1.2.0", release.Version.ToString());
        Assert.Equal("https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0", release.ReleasePageUrl.AbsoluteUri);
        Assert.Equal("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-1.2.0-win-x64-setup.exe", release.DownloadUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("LumaTherm-1.2.0-portable-win-x64.zip")]
    [InlineData("dashboard.png")]
    [InlineData("SHA256SUMS.txt")]
    public async Task GetLatestStableAsync_SelectsWindowsInstallerRegardlessOfAssetOrder(string leadingAsset)
    {
        var document = JsonNode.Parse(StableJson)!;
        document["assets"]!.AsArray().Insert(0, new JsonObject
        {
            ["name"] = leadingAsset,
            ["browser_download_url"] = $"https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/{leadingAsset}",
        });
        using var feed = FeedReturning(JsonResponse(document.ToJsonString()));

        var release = await feed.GetLatestStableAsync(CancellationToken.None);

        Assert.Equal("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-1.2.0-win-x64-setup.exe",
            release.DownloadUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("LumaTherm-1.1.0-win-x64-setup.exe")]
    [InlineData("LumaTherm-1.2.0-win-arm64-setup.exe")]
    public async Task GetLatestStableAsync_RejectsMissingInstallerForReleaseVersionAndArchitecture(string assetName)
    {
        var json = StableJson.Replace("LumaTherm-1.2.0-win-x64-setup.exe", assetName, StringComparison.Ordinal);
        using var feed = FeedReturning(JsonResponse(json));

        await Assert.ThrowsAsync<InvalidDataException>(() => feed.GetLatestStableAsync(CancellationToken.None));
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
            .Replace("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-1.2.0-win-x64-setup.exe", downloadUrl, StringComparison.Ordinal);
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
    public async Task GetLatestStableAsync_TimesOutWhenResponseBodyStopsArriving()
    {
        using var stream = new BlockingReadStream();
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        using var feed = new GitHubReleaseFeed(client);
        client.Timeout = TimeSpan.FromMilliseconds(100);
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var request = feed.GetLatestStableAsync(callerCancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(callerCancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.False(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task GetLatestStableAsync_HonorsCallerCancellationWhileReadingResponseBody()
    {
        using var stream = new BlockingReadStream();
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        using var feed = new GitHubReleaseFeed(client);
        using var callerCancellation = new CancellationTokenSource();

        var request = feed.GetLatestStableAsync(callerCancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
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
            "name":"LumaTherm-1.2.0-win-x64-setup.exe",
            "browser_download_url":"https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-1.2.0-win-x64-setup.exe"
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
            "name":"LumaTherm-1.2.0-win-x64-setup.exe",
            "browser_download_url":"https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm-1.2.0-win-x64-setup.exe"
          }]
        }
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class BlockingReadStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
