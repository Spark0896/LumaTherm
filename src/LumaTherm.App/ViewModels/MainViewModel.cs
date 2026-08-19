using System.Collections.ObjectModel;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;

namespace LumaTherm.App.ViewModels;

public sealed record TemperaturePoint(DateTimeOffset Timestamp, double Celsius);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int HistoryCapacity = 120;
    private readonly IThermalRuntime _runtime;
    private readonly SynchronizationContext? _synchronizationContext;
    private SettingsViewModel? _settingsViewModel;
    private DateTimeOffset? _lastReadingTimestamp;
    private bool _disposed;
    private double _currentTemperature = double.NaN;
    private RgbColor _displayColor = ThermalProfile.Default.ColdColor;
    private ThermalProfile _profile = ThermalProfile.Default;
    private string _temperatureText = "—°C";
    private string _statusText = "Подключение…";
    private string _currentColorHex = ThermalProfile.Default.ColdColor.ToHex();
    private string _gpuName = "GPU не обнаружен";
    private string _sensorSource = "—";
    private string _lightingDeviceName = "Подсветка не обнаружена";
    private bool _isModeEnabled;

    public MainViewModel(IThermalRuntime runtime, SynchronizationContext? synchronizationContext = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _synchronizationContext = synchronizationContext;
        ToggleModeCommand = new AsyncRelayCommand(ToggleModeAsync, onException: RouteToggleFailure);
        _runtime.SnapshotChanged += OnSnapshotChanged;
        ApplySnapshot(_runtime.CurrentSnapshot);
    }

    public ObservableCollection<TemperaturePoint> History { get; } = [];
    public double CurrentTemperature { get => _currentTemperature; private set => SetProperty(ref _currentTemperature, value); }
    public RgbColor DisplayColor { get => _displayColor; private set => SetProperty(ref _displayColor, value); }
    public ThermalProfile Profile { get => _profile; private set => SetProperty(ref _profile, value); }
    public string TemperatureText { get => _temperatureText; private set => SetProperty(ref _temperatureText, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string CurrentColorHex { get => _currentColorHex; private set => SetProperty(ref _currentColorHex, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string SensorSource { get => _sensorSource; private set => SetProperty(ref _sensorSource, value); }
    public string LightingDeviceName { get => _lightingDeviceName; private set => SetProperty(ref _lightingDeviceName, value); }
    public bool IsModeEnabled { get => _isModeEnabled; private set => SetProperty(ref _isModeEnabled, value); }
    public AsyncRelayCommand ToggleModeCommand { get; }

    public void UpdateProfile(ThermalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }

    public void SynchronizeProfile(SettingsViewModel settingsViewModel)
    {
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        if (_settingsViewModel is not null)
        {
            _settingsViewModel.ProfileSaved -= OnProfileSaved;
        }

        _settingsViewModel = settingsViewModel;
        _settingsViewModel.ProfileSaved += OnProfileSaved;
        UpdateProfile(_settingsViewModel.LiveSettings.Profile);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        if (_settingsViewModel is not null)
        {
            _settingsViewModel.ProfileSaved -= OnProfileSaved;
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
        CurrentTemperature = snapshot.Temperature?.Celsius ?? double.NaN;
        TemperatureText = snapshot.Temperature is { } temperature ? $"{temperature.Celsius:0.#}°C" : "—°C";
        DisplayColor = snapshot.Color ?? ThermalProfile.Default.ColdColor;
        CurrentColorHex = DisplayColor.ToHex();
        StatusText = ToStatusText(snapshot.Status);
        GpuName = snapshot.Temperature?.DeviceName ?? "GPU не обнаружен";
        SensorSource = snapshot.Temperature?.SourceName ?? "—";
        LightingDeviceName = snapshot.LightingDevice?.Name ?? "Подсветка не обнаружена";

        IsModeEnabled = snapshot.Status != RuntimeStatus.Disabled;

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
        var enabled = !IsModeEnabled;
        await _runtime.SetModeEnabledAsync(enabled, CancellationToken.None);
        IsModeEnabled = enabled;
    }

    private void RouteToggleFailure(Exception _)
    {
        StatusText = "Не удалось изменить режим.";
    }

    private void OnProfileSaved(object? sender, ThermalProfile profile)
    {
        if (_disposed)
        {
            return;
        }

        if (_synchronizationContext is null)
        {
            UpdateProfile(profile);
            return;
        }

        _synchronizationContext.Post(
            _ =>
            {
                if (!_disposed)
                {
                    UpdateProfile(profile);
                }
            },
            null);
    }

    private static string ToStatusText(RuntimeStatus status) => status switch
    {
        RuntimeStatus.Disabled => "Режим выключен",
        RuntimeStatus.Connecting => "Подключение…",
        RuntimeStatus.Active => "Режим активен",
        RuntimeStatus.HoldingLastColor => "Сохранение последнего цвета",
        RuntimeStatus.SensorUnavailable => "Датчик температуры недоступен",
        RuntimeStatus.LightingUnavailable => "Подсветка недоступна",
        RuntimeStatus.Suspended => "Работа приостановлена",
        RuntimeStatus.Faulted => "Ошибка работы",
        _ => "Неизвестное состояние",
    };
}
