using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IThermalRuntime _runtime;
    private readonly IStartupService _startupService;
    private AppSettings _liveSettings;
    private double _coldTemperature;
    private RgbColor _coldColor;
    private double _warmTemperature;
    private RgbColor _warmColor;
    private double _hotTemperature;
    private RgbColor _hotColor;
    private double _smoothingSeconds;
    private bool _isModeEnabled;
    private bool _isAutostartEnabled;
    private bool _minimizeToTray;
    private bool _notificationsEnabled;
    private string _gpuName = "GPU не обнаружен";
    private string _lightingDeviceName = "Подсветка не обнаружена";
    private string? _validationMessage;

    public SettingsViewModel(IThermalRuntime runtime, IStartupService startupService, AppSettings settings)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _liveSettings = (settings ?? throw new ArgumentNullException(nameof(settings))).Validate();
        LoadEditableValues(_liveSettings);
        var snapshot = runtime.CurrentSnapshot;
        GpuName = snapshot.Temperature?.DeviceName ?? GpuName;
        LightingDeviceName = snapshot.LightingDevice?.Name ?? LightingDeviceName;
        SaveCommand = new AsyncRelayCommand(SaveAsync, onException: _ => ValidationMessage = "Не удалось сохранить настройки.");
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
    }

    public event EventHandler<ThermalProfile>? ProfileSaved;

    public AppSettings LiveSettings => _liveSettings;
    public double ColdTemperature { get => _coldTemperature; set => SetProperty(ref _coldTemperature, value); }
    public RgbColor ColdColor { get => _coldColor; set => SetProperty(ref _coldColor, value); }
    public double WarmTemperature { get => _warmTemperature; set => SetProperty(ref _warmTemperature, value); }
    public RgbColor WarmColor { get => _warmColor; set => SetProperty(ref _warmColor, value); }
    public double HotTemperature { get => _hotTemperature; set => SetProperty(ref _hotTemperature, value); }
    public RgbColor HotColor { get => _hotColor; set => SetProperty(ref _hotColor, value); }
    public double SmoothingSeconds { get => _smoothingSeconds; set => SetProperty(ref _smoothingSeconds, value); }
    public bool IsModeEnabled { get => _isModeEnabled; set => SetProperty(ref _isModeEnabled, value); }
    public bool IsAutostartEnabled { get => _isAutostartEnabled; set => SetProperty(ref _isAutostartEnabled, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => SetProperty(ref _minimizeToTray, value); }
    public bool NotificationsEnabled { get => _notificationsEnabled; set => SetProperty(ref _notificationsEnabled, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string LightingDeviceName { get => _lightingDeviceName; private set => SetProperty(ref _lightingDeviceName, value); }
    public string? ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand ResetDefaultsCommand { get; }

    public async Task SaveAsync()
    {
        AppSettings candidate;
        try
        {
            candidate = CreateCandidate();
            candidate.Validate();
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = TranslateValidationError(exception);
            return;
        }

        var autostartChanged = candidate.IsAutostartEnabled != _liveSettings.IsAutostartEnabled;
        if (autostartChanged)
        {
            try
            {
                await _startupService.SetEnabledAsync(candidate.IsAutostartEnabled, CancellationToken.None);
            }
            catch (Exception)
            {
                ValidationMessage = "Не удалось изменить автозапуск.";
                return;
            }
        }

        try
        {
            await _runtime.UpdateSettingsAsync(candidate, CancellationToken.None);
        }
        catch (Exception)
        {
            ValidationMessage = await RollBackAutostartIfNeededAsync(autostartChanged);
            return;
        }

        _liveSettings = candidate;
        OnPropertyChanged(nameof(LiveSettings));
        ValidationMessage = null;
        ProfileSaved?.Invoke(this, candidate.Profile);
    }

    private void ResetDefaults()
    {
        LoadEditableValues(AppSettings.Default);
        ValidationMessage = null;
    }

    private AppSettings CreateCandidate() => _liveSettings with
    {
        Profile = new ThermalProfile(ColdTemperature, ColdColor, WarmTemperature, WarmColor, HotTemperature, HotColor, SmoothingSeconds),
        IsModeEnabled = IsModeEnabled,
        IsAutostartEnabled = IsAutostartEnabled,
        MinimizeToTray = MinimizeToTray,
        NotificationsEnabled = NotificationsEnabled,
    };

    private void LoadEditableValues(AppSettings settings)
    {
        ColdTemperature = settings.Profile.ColdTemperature;
        ColdColor = settings.Profile.ColdColor;
        WarmTemperature = settings.Profile.WarmTemperature;
        WarmColor = settings.Profile.WarmColor;
        HotTemperature = settings.Profile.HotTemperature;
        HotColor = settings.Profile.HotColor;
        SmoothingSeconds = settings.Profile.SmoothingSeconds;
        IsModeEnabled = settings.IsModeEnabled;
        IsAutostartEnabled = settings.IsAutostartEnabled;
        MinimizeToTray = settings.MinimizeToTray;
        NotificationsEnabled = settings.NotificationsEnabled;
    }

    private async Task<string> RollBackAutostartIfNeededAsync(bool autostartChanged)
    {
        if (!autostartChanged)
        {
            return "Не удалось сохранить настройки.";
        }

        try
        {
            await _startupService.SetEnabledAsync(_liveSettings.IsAutostartEnabled, CancellationToken.None);
            return "Не удалось сохранить настройки.";
        }
        catch (Exception)
        {
            return "Не удалось сохранить настройки. Не удалось вернуть настройку автозапуска.";
        }
    }

    private static string TranslateValidationError(ArgumentException exception) => exception.Message switch
    {
        "Temperatures must be between 0 and 120 °C." => "Температура должна быть от 0 до 120 °C.",
        "Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points." => "Температуры должны возрастать с шагом не менее 1 °C.",
        "SmoothingSeconds must be between 0.1 and 5.0." => "Сглаживание должно быть от 0,1 до 5,0 секунд.",
        _ => "Проверьте параметры температурного профиля.",
    };
}
