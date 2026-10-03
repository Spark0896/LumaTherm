using System.Collections.ObjectModel;
using System.Globalization;
using LumaTherm.App.Localization;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;

namespace LumaTherm.App.ViewModels;

public sealed record TemperaturePoint(DateTimeOffset Timestamp, double Celsius);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int HistoryCapacity = 120;
    private readonly IThermalRuntime _runtime;
    private readonly ILocalizationService _localization;
    private readonly SynchronizationContext? _synchronizationContext;
    private SettingsViewModel? _settingsViewModel;
    private RuntimeSnapshot _lastSnapshot;
    private string? _statusOverrideKey;
    private DateTimeOffset? _lastReadingTimestamp;
    private bool _disposed;
    private double _currentTemperature = double.NaN;
    private RgbColor _displayColor = ThermalProfile.Default.ColdColor;
    private ThermalRange? _currentRange;
    private ThermalProfile _profile = ThermalProfile.Default;
    private string _temperatureText = "—°C";
    private string _statusText = string.Empty;
    private string _currentColorHex = ThermalProfile.Default.ColdColor.ToHex();
    private string _gpuName = string.Empty;
    private string _sensorSource = "—";
    private string _lightingDeviceName = string.Empty;
    private bool _isModeEnabled;
    private bool _hasLightingDevice;
    private string _smoothingText = string.Empty;
    private string _autostartText = string.Empty;
    private string _minimizeToTrayText = string.Empty;

    public MainViewModel(
        IThermalRuntime runtime,
        SynchronizationContext? synchronizationContext = null,
        ILocalizationService? localization = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localization = localization ?? LocalizationService.CreateFallback();
        _synchronizationContext = synchronizationContext;
        _lastSnapshot = _runtime.CurrentSnapshot;
        ToggleModeCommand = new AsyncRelayCommand(ToggleModeAsync, onException: RouteToggleFailure);
        _localization.LanguageChanged += OnLanguageChanged;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        ApplySnapshot(_lastSnapshot);
    }

    public ObservableCollection<TemperaturePoint> History { get; } = [];
    public double CurrentTemperature { get => _currentTemperature; private set => SetProperty(ref _currentTemperature, value); }
    public RgbColor DisplayColor { get => _displayColor; private set => SetProperty(ref _displayColor, value); }
    public ThermalRange? CurrentRange { get => _currentRange; private set => SetProperty(ref _currentRange, value); }
    public ThermalProfile Profile { get => _profile; private set { if (SetProperty(ref _profile, value)) OnPropertyChanged(nameof(ProfileSummary)); } }
    public string ProfileSummary => string.Join(" · ", Profile.Points.Select(point => $"{point.Temperature:0.#}° {point.Color.ToHex()}"));
    public string TemperatureText { get => _temperatureText; private set => SetProperty(ref _temperatureText, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string CurrentColorHex { get => _currentColorHex; private set => SetProperty(ref _currentColorHex, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string SensorSource { get => _sensorSource; private set => SetProperty(ref _sensorSource, value); }
    public string LightingDeviceName { get => _lightingDeviceName; private set => SetProperty(ref _lightingDeviceName, value); }
    public bool IsModeEnabled { get => _isModeEnabled; private set => SetProperty(ref _isModeEnabled, value); }
    public bool HasLightingDevice { get => _hasLightingDevice; private set => SetProperty(ref _hasLightingDevice, value); }
    public bool IsHealthy => _lastSnapshot.Status == RuntimeStatus.Active;
    public string SmoothingText { get => _smoothingText; private set => SetProperty(ref _smoothingText, value); }
    public string AutostartText { get => _autostartText; private set => SetProperty(ref _autostartText, value); }
    public string MinimizeToTrayText { get => _minimizeToTrayText; private set => SetProperty(ref _minimizeToTrayText, value); }
    public AsyncRelayCommand ToggleModeCommand { get; }

    internal void SynchronizeProfile(SettingsViewModel settingsViewModel)
    {
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        if (_disposed)
        {
            return;
        }

        if (_settingsViewModel is not null)
        {
            _settingsViewModel.ProfileSaved -= OnProfileSaved;
            _settingsViewModel.PropertyChanged -= OnSettingsChanged;
        }

        _settingsViewModel = settingsViewModel;
        _settingsViewModel.ProfileSaved += OnProfileSaved;
        _settingsViewModel.PropertyChanged += OnSettingsChanged;
        SetProfile(_settingsViewModel.LiveSettings.Profile);
        ApplySettingsProjection();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        if (_settingsViewModel is not null)
        {
            _settingsViewModel.ProfileSaved -= OnProfileSaved;
            _settingsViewModel.PropertyChanged -= OnSettingsChanged;
            _settingsViewModel = null;
        }
    }

    private void OnSnapshotChanged(object? sender, RuntimeSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        if (_synchronizationContext is null)
        {
            ApplySnapshot(snapshot);
            return;
        }

        _synchronizationContext.Post(
            state =>
            {
                if (!_disposed)
                {
                    ApplySnapshot((RuntimeSnapshot)state!);
                }
            },
            snapshot);
    }

    private void ApplySnapshot(RuntimeSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        _statusOverrideKey = null;
        CurrentTemperature = snapshot.Temperature?.Celsius ?? double.NaN;
        TemperatureText = snapshot.Temperature is { } temperature ? $"{temperature.Celsius:0.#}°C" : "—°C";
        DisplayColor = snapshot.Color ?? ThermalProfile.Default.ColdColor;
        CurrentColorHex = DisplayColor.ToHex();
        CurrentRange = snapshot.Range;
        StatusText = _localization.Get(StatusKey(snapshot.Status));
        GpuName = snapshot.Temperature?.DeviceName ?? _localization.Get("Runtime.GpuNotFound");
        SensorSource = snapshot.Temperature?.SourceName ?? "—";
        LightingDeviceName = snapshot.LightingDevice?.Name ?? _localization.Get("Runtime.LightingNotFound");
        HasLightingDevice = snapshot.LightingDevice is { IsAvailable: true };
        OnPropertyChanged(nameof(IsHealthy));

        IsModeEnabled = snapshot.IsModeEnabled;

        if (snapshot.Temperature is { } reading && _lastReadingTimestamp != reading.Timestamp)
        {
            _lastReadingTimestamp = reading.Timestamp;
            History.Add(new TemperaturePoint(reading.Timestamp, reading.Celsius));
            while (History.Count > HistoryCapacity)
            {
                History.RemoveAt(0);
            }
        }
    }

    private async Task ToggleModeAsync()
    {
        await _runtime.SetModeEnabledAsync(!IsModeEnabled, CancellationToken.None);
        IsModeEnabled = _runtime.CurrentSnapshot.IsModeEnabled;
    }

    private void RouteToggleFailure(Exception _)
    {
        _statusOverrideKey = "Runtime.ToggleFailed";
        StatusText = _localization.Get(_statusOverrideKey);
    }

    private void OnProfileSaved(object? sender, ThermalProfile profile)
    {
        if (_disposed)
        {
            return;
        }

        if (_synchronizationContext is null)
        {
            SetProfile(profile);
            return;
        }

        _synchronizationContext.Post(
            _ =>
            {
                if (!_disposed)
                {
                    SetProfile(profile);
                }
            },
            null);
    }

    private void SetProfile(ThermalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => RunOnContext(() =>
    {
        StatusText = _localization.Get(_statusOverrideKey ?? StatusKey(_lastSnapshot.Status));
        if (_lastSnapshot.Temperature is null) GpuName = _localization.Get("Runtime.GpuNotFound");
        if (_lastSnapshot.LightingDevice is null) LightingDeviceName = _localization.Get("Runtime.LightingNotFound");
        ApplySettingsProjection();
    });

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null or "" or nameof(SettingsViewModel.LiveSettings)) ApplySettingsProjection();
    }

    private void ApplySettingsProjection()
    {
        if (_settingsViewModel is null) return;
        var settings = _settingsViewModel.LiveSettings;
        var culture = _localization.CurrentLanguage == LumaTherm.Core.Settings.AppLanguage.Russian
            ? CultureInfo.GetCultureInfo("ru-RU")
            : CultureInfo.GetCultureInfo("en-US");
        SmoothingText = string.Format(culture, _localization.Get("Dashboard.SecondsFormat"), settings.Profile.SmoothingSeconds);
        AutostartText = _localization.Get(settings.IsAutostartEnabled ? "Common.On" : "Common.Off");
        MinimizeToTrayText = _localization.Get(settings.MinimizeToTray ? "Common.On" : "Common.Off");
    }

    private void RunOnContext(Action action)
    {
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext)) action();
        else _synchronizationContext.Post(_ => { if (!_disposed) action(); }, null);
    }

    private static string StatusKey(RuntimeStatus status) => status switch
    {
        RuntimeStatus.Disabled => "Runtime.ModeDisabled",
        RuntimeStatus.Connecting => "Runtime.Connecting",
        RuntimeStatus.Active => "Runtime.ModeActive",
        RuntimeStatus.HoldingLastColor => "Runtime.HoldingLastColor",
        RuntimeStatus.SensorUnavailable => "Runtime.SensorUnavailable",
        RuntimeStatus.LightingUnavailable => "Runtime.LightingUnavailable",
        RuntimeStatus.Suspended => "Runtime.Suspended",
        RuntimeStatus.Faulted => "Runtime.Faulted",
        _ => "Runtime.Unknown",
    };
}
