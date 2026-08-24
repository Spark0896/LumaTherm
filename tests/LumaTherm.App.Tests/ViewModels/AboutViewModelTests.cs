using System.Net.Http;
using System.Reflection;
using LumaTherm.App.Localization;
using LumaTherm.App.Services;
using LumaTherm.App.ViewModels;
using LumaTherm.Core.Settings;
using LumaTherm.Core.Updates;

namespace LumaTherm.App.Tests.ViewModels;

public sealed class AboutViewModelTests
{
    [Fact]
    public void Constructor_DerivesCurrentVersionFromTheApplicationAssemblyMetadata()
    {
        using var vm = Create(new FixedFeed(Release("1.1.0")));
        var metadata = typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.Equal("1.1.0", metadata);
        Assert.Equal(metadata, vm.CurrentVersion);
        Assert.Equal(UpdateState.Idle, vm.State);
        Assert.True(vm.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(vm.OpenReleaseCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NewerStableReleaseEnablesVerifiedDownloadNavigation()
    {
        var launcher = new RecordingLauncher();
        using var vm = Create(new FixedFeed(Release("1.2.0")), launcher);

        await vm.CheckForUpdatesCommand.ExecuteAsync();

        Assert.Equal(UpdateState.Available, vm.State);
        Assert.Equal("1.2.0", vm.AvailableVersion);
        Assert.True(vm.CheckForUpdatesCommand.CanExecute(null));
        Assert.True(vm.OpenReleaseCommand.CanExecute(null));
        vm.OpenReleaseCommand.Execute(null);
        Assert.Equal("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm.exe", launcher.LastOpened!.AbsoluteUri);
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.0.9")]
    public async Task CheckForUpdatesAsync_CurrentOrOlderReleaseKeepsDownloadNavigationDisabled(string latest)
    {
        using var vm = Create(new FixedFeed(Release(latest)));

        await vm.CheckForUpdatesCommand.ExecuteAsync();

        Assert.Equal(UpdateState.Current, vm.State);
        Assert.Null(vm.AvailableVersion);
        Assert.False(vm.OpenReleaseCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FeedFailureBecomesLocalizedRetryableFailedState()
    {
        var localization = new FakeLocalization();
        using var vm = Create(new ThrowingFeed(), localization: localization);

        await vm.CheckForUpdatesCommand.ExecuteAsync();

        Assert.Equal(UpdateState.Failed, vm.State);
        Assert.Equal("failed-en", vm.StatusText);
        Assert.True(vm.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(vm.OpenReleaseCommand.CanExecute(null));

        localization.Apply(AppLanguage.Russian);
        Assert.Equal("failed-ru", vm.StatusText);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DoesNotOverlapConcurrentChecksAndDisablesCommandsWhileChecking()
    {
        var feed = new PendingFeed();
        using var vm = Create(feed);

        var first = vm.CheckForUpdatesCommand.ExecuteAsync();
        Assert.True(SpinWait.SpinUntil(() => vm.State == UpdateState.Checking, TimeSpan.FromSeconds(1)));

        Assert.False(vm.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(vm.OpenReleaseCommand.CanExecute(null));
        await vm.CheckForUpdatesCommand.ExecuteAsync();
        Assert.Equal(1, feed.Calls);

        feed.Complete(Release("1.1.0"));
        await first;
        Assert.Equal(UpdateState.Current, vm.State);
    }

    [Fact]
    public async Task Dispose_CancelsPendingCheckAndPreventsPostDisposalUiUpdates()
    {
        var feed = new PendingFeed();
        var vm = Create(feed);
        var changes = 0;
        vm.PropertyChanged += (_, _) => changes++;
        var checking = vm.CheckForUpdatesCommand.ExecuteAsync();
        Assert.True(SpinWait.SpinUntil(() => vm.State == UpdateState.Checking, TimeSpan.FromSeconds(1)));
        var changesBeforeDispose = changes;

        vm.Dispose();
        await checking.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(feed.WasCanceled);
        Assert.Equal(changesBeforeDispose, changes);
        Assert.False(vm.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(vm.OpenRepositoryCommand.CanExecute(null));
    }

    private static AboutViewModel Create(
        IReleaseFeed feed,
        ILinkLauncher? launcher = null,
        ILocalizationService? localization = null) =>
        new(feed, launcher ?? new RecordingLauncher(), localization ?? new FakeLocalization());

    private static ReleaseInfo Release(string version) => new(
        SemanticVersion.Parse(version),
        new Uri($"https://github.com/Spark0896/LumaTherm/releases/tag/v{version}"),
        new Uri($"https://github.com/Spark0896/LumaTherm/releases/download/v{version}/LumaTherm.exe"));

    private sealed class FixedFeed(ReleaseInfo release) : IReleaseFeed
    {
        public Task<ReleaseInfo> GetLatestStableAsync(CancellationToken cancellationToken) => Task.FromResult(release);
    }

    private sealed class ThrowingFeed : IReleaseFeed
    {
        public Task<ReleaseInfo> GetLatestStableAsync(CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class PendingFeed : IReleaseFeed
    {
        private readonly TaskCompletionSource<ReleaseInfo> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public bool WasCanceled { get; private set; }
        public async Task<ReleaseInfo> GetLatestStableAsync(CancellationToken cancellationToken)
        {
            Calls++;
            try { return await _completion.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { WasCanceled = true; throw; }
        }
        public void Complete(ReleaseInfo release) => _completion.TrySetResult(release);
    }

    private sealed class RecordingLauncher : ILinkLauncher
    {
        public Uri? LastOpened { get; private set; }
        public void Open(Uri uri) => LastOpened = uri;
    }

    private sealed class FakeLocalization : ILocalizationService
    {
        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;
        public event EventHandler? LanguageChanged;
        public string Get(string key) => key switch
        {
            "Update.Ready" => CurrentLanguage == AppLanguage.Russian ? "ready-ru" : "ready-en",
            "Update.Checking" => CurrentLanguage == AppLanguage.Russian ? "checking-ru" : "checking-en",
            "Update.Available" => CurrentLanguage == AppLanguage.Russian ? "available-ru" : "available-en",
            "Update.UpToDate" => CurrentLanguage == AppLanguage.Russian ? "current-ru" : "current-en",
            "Update.Failed" => CurrentLanguage == AppLanguage.Russian ? "failed-ru" : "failed-en",
            _ => throw new KeyNotFoundException(key),
        };
        public void Apply(AppLanguage language)
        {
            CurrentLanguage = language;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
