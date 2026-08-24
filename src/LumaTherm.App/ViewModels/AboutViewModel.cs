using System.Reflection;
using LumaTherm.App.Localization;
using LumaTherm.App.Services;
using LumaTherm.Core.Updates;

namespace LumaTherm.App.ViewModels;

public enum UpdateState
{
    Idle,
    Checking,
    Current,
    Available,
    Failed,
}

public sealed class AboutViewModel : ObservableObject, IDisposable
{
    public static readonly Uri RepositoryUri = new("https://github.com/Spark0896/LumaTherm/");
    private readonly IReleaseFeed _releaseFeed;
    private readonly ILinkLauncher _linkLauncher;
    private readonly ILocalizationService _localization;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemanticVersion _currentVersion;
    private ReleaseInfo? _availableRelease;
    private UpdateState _state;
    private bool _disposed;

    public AboutViewModel(IReleaseFeed releaseFeed, ILinkLauncher linkLauncher, ILocalizationService localization)
    {
        _releaseFeed = releaseFeed ?? throw new ArgumentNullException(nameof(releaseFeed));
        _linkLauncher = linkLauncher ?? throw new ArgumentNullException(nameof(linkLauncher));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _currentVersion = ReadAssemblyVersion(typeof(AboutViewModel).Assembly);
        CurrentVersion = _currentVersion.ToString();
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !_disposed && State != UpdateState.Checking);
        OpenReleaseCommand = new RelayCommand(OpenRelease, () => !_disposed && State == UpdateState.Available && _availableRelease is not null);
        OpenRepositoryCommand = new RelayCommand(() => _linkLauncher.Open(RepositoryUri), () => !_disposed);
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public string CurrentVersion { get; }
    public string RepositoryUrl => RepositoryUri.AbsoluteUri.TrimEnd('/');
    public string? AvailableVersion => _availableRelease?.Version.ToString();
    public UpdateState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(AvailableVersion));
            OpenReleaseCommand.RaiseCanExecuteChanged();
        }
    }
    public string StatusText => _localization.Get(State switch
    {
        UpdateState.Idle => "Update.Ready",
        UpdateState.Checking => "Update.Checking",
        UpdateState.Current => "Update.UpToDate",
        UpdateState.Available => "Update.Available",
        UpdateState.Failed => "Update.Failed",
        _ => throw new InvalidOperationException("Unknown update state."),
    });

    public AsyncRelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }
    public RelayCommand OpenRepositoryCommand { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        OpenReleaseCommand.RaiseCanExecuteChanged();
        OpenRepositoryCommand.RaiseCanExecuteChanged();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_disposed || State == UpdateState.Checking) return;
        _availableRelease = null;
        State = UpdateState.Checking;
        try
        {
            var release = await _releaseFeed.GetLatestStableAsync(_lifetimeCancellation.Token);
            if (_disposed) return;
            if (release.Version > _currentVersion)
            {
                _availableRelease = release;
                State = UpdateState.Available;
            }
            else
            {
                State = UpdateState.Current;
            }
        }
        catch (OperationCanceledException) when (_disposed || _lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception) when (!_disposed)
        {
            State = UpdateState.Failed;
        }
    }

    private void OpenRelease()
    {
        if (_availableRelease is not null) _linkLauncher.Open(_availableRelease.DownloadUrl);
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        if (!_disposed) OnPropertyChanged(nameof(StatusText));
    }

    private static SemanticVersion ReadAssemblyVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return SemanticVersion.Parse(informational);
        var version = assembly.GetName().Version ?? throw new InvalidOperationException("Application assembly version is unavailable.");
        return new SemanticVersion(version.Major, version.Minor, Math.Max(0, version.Build));
    }
}
