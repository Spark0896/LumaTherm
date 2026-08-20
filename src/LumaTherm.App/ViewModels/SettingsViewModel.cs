using System.Collections.ObjectModel;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;
using LumaTherm.App.Services;

namespace LumaTherm.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IThermalRuntime _runtime;
    private readonly IStartupService _startupService;
    private readonly IColorPickerService _colorPickerService;
    private readonly ILightingDeviceDiscovery _lightingDeviceDiscovery;
    private readonly SemaphoreSlim _settingsMutationGate = new(1, 1);
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
    private string _lightingHardwareStatus = "Устройства Windows LampArray не проверены.";
    private string? _selectedLightingDeviceId;
    private string? _unavailableSavedDeviceId;
    private bool _isLightingDeviceSelectorVisible;
    private string? _validationMessage;

    public SettingsViewModel(IThermalRuntime runtime, IStartupService startupService, AppSettings settings)
        : this(runtime, startupService, settings, NullColorPickerService.Instance, EmptyLightingDeviceDiscovery.Instance)
    {
    }

    public SettingsViewModel(
        IThermalRuntime runtime,
        IStartupService startupService,
        AppSettings settings,
        IColorPickerService colorPickerService)
        : this(runtime, startupService, settings, colorPickerService, EmptyLightingDeviceDiscovery.Instance)
    {
    }

    public SettingsViewModel(
        IThermalRuntime runtime,
        IStartupService startupService,
        AppSettings settings,
        IColorPickerService colorPickerService,
        ILightingDeviceDiscovery lightingDeviceDiscovery)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _colorPickerService = colorPickerService ?? throw new ArgumentNullException(nameof(colorPickerService));
        _lightingDeviceDiscovery = lightingDeviceDiscovery ?? throw new ArgumentNullException(nameof(lightingDeviceDiscovery));
        _liveSettings = (settings ?? throw new ArgumentNullException(nameof(settings))).Validate();
        LoadEditableValues(_liveSettings);
        var snapshot = runtime.CurrentSnapshot;
        GpuName = snapshot.Temperature?.DeviceName ?? GpuName;
        LightingDeviceName = snapshot.LightingDevice?.Name ?? LightingDeviceName;
        SaveCommand = new AsyncRelayCommand(() => RunSettingsMutationAsync(SaveAsync), onException: _ => ValidationMessage = "Не удалось сохранить настройки.");
        PickColdColorCommand = new AsyncRelayCommand(() => RunSettingsMutationAsync(PickColdColorAsync), onException: _ => ValidationMessage = "Не удалось выбрать цвет.");
        PickWarmColorCommand = new AsyncRelayCommand(() => RunSettingsMutationAsync(PickWarmColorAsync), onException: _ => ValidationMessage = "Не удалось выбрать цвет.");
        PickHotColorCommand = new AsyncRelayCommand(() => RunSettingsMutationAsync(PickHotColorAsync), onException: _ => ValidationMessage = "Не удалось выбрать цвет.");
        DiscoverLightingDevicesCommand = new AsyncRelayCommand(DiscoverLightingDevicesAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
    }

    internal event EventHandler<ThermalProfile>? ProfileSaved;

    public AppSettings LiveSettings => _liveSettings;
    public double ColdTemperature { get => _coldTemperature; set => SetProperty(ref _coldTemperature, value); }
    public RgbColor ColdColor
    {
        get => _coldColor;
        set
        {
            if (SetProperty(ref _coldColor, value))
            {
                OnPropertyChanged(nameof(ColdColorHex));
            }
        }
    }
    public string ColdColorHex => ColdColor.ToHex();
    public double WarmTemperature { get => _warmTemperature; set => SetProperty(ref _warmTemperature, value); }
    public RgbColor WarmColor
    {
        get => _warmColor;
        set
        {
            if (SetProperty(ref _warmColor, value))
            {
                OnPropertyChanged(nameof(WarmColorHex));
            }
        }
    }
    public string WarmColorHex => WarmColor.ToHex();
    public double HotTemperature { get => _hotTemperature; set => SetProperty(ref _hotTemperature, value); }
    public RgbColor HotColor
    {
        get => _hotColor;
        set
        {
            if (SetProperty(ref _hotColor, value))
            {
                OnPropertyChanged(nameof(HotColorHex));
            }
        }
    }
    public string HotColorHex => HotColor.ToHex();
    public double SmoothingSeconds { get => _smoothingSeconds; set => SetProperty(ref _smoothingSeconds, value); }
    public bool IsModeEnabled { get => _isModeEnabled; set => SetProperty(ref _isModeEnabled, value); }
    public bool IsAutostartEnabled { get => _isAutostartEnabled; set => SetProperty(ref _isAutostartEnabled, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => SetProperty(ref _minimizeToTray, value); }
    public bool NotificationsEnabled { get => _notificationsEnabled; set => SetProperty(ref _notificationsEnabled, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string LightingDeviceName { get => _lightingDeviceName; private set => SetProperty(ref _lightingDeviceName, value); }
    public string LightingHardwareStatus { get => _lightingHardwareStatus; private set => SetProperty(ref _lightingHardwareStatus, value); }
    public ObservableCollection<LightingDeviceInfo> LightingDevices { get; } = [];
    public string? SelectedLightingDeviceId
    {
        get => _selectedLightingDeviceId;
        set
        {
            if (!SetProperty(ref _selectedLightingDeviceId, value))
            {
                return;
            }

            if (LightingDevices.FirstOrDefault(device => device.Id == value) is not { IsAvailable: true } selected)
            {
                return;
            }

            if (_unavailableSavedDeviceId is { } staleId
                && LightingDevices.FirstOrDefault(device => device.Id == staleId) is { } stale)
            {
                LightingDevices.Remove(stale);
                _unavailableSavedDeviceId = null;
            }

            LightingDeviceName = selected.Name;
            LightingHardwareStatus = "Подключено напрямую через Windows LampArray.";
            IsLightingDeviceSelectorVisible = LightingDevices.Count(device => device.IsAvailable) > 1;
        }
    }
    public bool IsLightingDeviceSelectorVisible { get => _isLightingDeviceSelectorVisible; private set => SetProperty(ref _isLightingDeviceSelectorVisible, value); }
    public string? ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand PickColdColorCommand { get; }
    public AsyncRelayCommand PickWarmColorCommand { get; }
    public AsyncRelayCommand PickHotColorCommand { get; }
    public AsyncRelayCommand DiscoverLightingDevicesCommand { get; }
    public RelayCommand ResetDefaultsCommand { get; }

    private async Task RunSettingsMutationAsync(Func<Task> mutation)
    {
        await _settingsMutationGate.WaitAsync();
        try
        {
            await mutation();
        }
        finally
        {
            _settingsMutationGate.Release();
        }
    }

    private async Task PickColdColorAsync()
    {
        var selected = _colorPickerService.Pick(ColdColor);
        if (selected is null)
        {
            return;
        }

        ColdColor = selected.Value;
        await SaveAsync();
    }

    private async Task PickWarmColorAsync()
    {
        var selected = _colorPickerService.Pick(WarmColor);
        if (selected is null)
        {
            return;
        }

        WarmColor = selected.Value;
        await SaveAsync();
    }

    private async Task PickHotColorAsync()
    {
        var selected = _colorPickerService.Pick(HotColor);
        if (selected is null)
        {
            return;
        }

        HotColor = selected.Value;
        await SaveAsync();
    }

    private async Task DiscoverLightingDevicesAsync()
    {
        IReadOnlyList<LightingDeviceInfo> devices;
        try
        {
            devices = await _lightingDeviceDiscovery.DiscoverAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            LightingHardwareStatus = "Не удалось получить устройства Windows LampArray. Подсветка может быть занята другим контроллером.";
            return;
        }

        LightingDevices.Clear();
        _unavailableSavedDeviceId = null;
        foreach (var device in devices)
        {
            LightingDevices.Add(device);
        }

        if (_liveSettings.PreferredLightingDeviceId is { Length: > 0 } savedId
            && !LightingDevices.Any(device => device.Id == savedId))
        {
            LightingDevices.Add(new LightingDeviceInfo(savedId, $"{savedId} (недоступно)", 0, false));
            _unavailableSavedDeviceId = savedId;
        }

        SelectedLightingDeviceId = _liveSettings.PreferredLightingDeviceId
            ?? devices.FirstOrDefault(device => device.IsAvailable)?.Id;
        var selectedDevice = LightingDevices.FirstOrDefault(device => device.Id == SelectedLightingDeviceId);
        IsLightingDeviceSelectorVisible = devices.Count(device => device.IsAvailable) > 1
            || selectedDevice is { IsAvailable: false };
        if (selectedDevice is { IsAvailable: false })
        {
            LightingDeviceName = "Подсветка недоступна";
            LightingHardwareStatus = "Сохранённое устройство недоступно или занято другим контроллером.";
        }
        else if (devices.Count == 0)
        {
            LightingHardwareStatus = "Устройства Windows LampArray не найдены.";
        }
        else if (selectedDevice is { IsAvailable: true } selected)
        {
            LightingDeviceName = selected.Name;
            LightingHardwareStatus = "Подключено напрямую через Windows LampArray.";
        }
        else
        {
            LightingDeviceName = "Подсветка недоступна";
            LightingHardwareStatus = "Сохранённое устройство недоступно или занято другим контроллером.";
        }
    }

    private async Task SaveAsync()
    {
        var candidate = CreateCandidate();

        try
        {
            await _runtime.UpdateSettingsAsync(candidate, CancellationToken.None);
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = TranslateValidationError(exception);
            return;
        }
        catch (InvalidOperationException)
        {
            ValidationMessage = "Не удалось сохранить настройки.";
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
                ValidationMessage = await RollBackRuntimeAfterStartupFailureAsync();
                return;
            }
        }


        Commit(candidate, null);
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
        PreferredLightingDeviceId = SelectedLightingDeviceId,
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
        SelectedLightingDeviceId = settings.PreferredLightingDeviceId;
    }

    private async Task<string> RollBackRuntimeAfterStartupFailureAsync()
    {
        try
        {
            await _runtime.UpdateSettingsAsync(_liveSettings, CancellationToken.None);
            return "Не удалось изменить автозапуск.";
        }
        catch (Exception)
        {
            return "Не удалось изменить автозапуск. Не удалось вернуть настройки.";
        }
    }

    private void Commit(AppSettings candidate, string? warning)
    {
        _liveSettings = candidate;
        var observerFailed = OnPropertyChanged(nameof(LiveSettings));
        foreach (EventHandler<ThermalProfile> handler in ProfileSaved?.GetInvocationList() ?? [])
        {
            try { handler(this, candidate.Profile); }
            catch (Exception) { observerFailed = true; }
        }
        ValidationMessage = observerFailed ? "Настройки сохранены, но обновление интерфейса выполнено не полностью." : warning;
    }

    private static string TranslateValidationError(ArgumentException exception) => exception.Message switch
    {
        "Temperatures must be between 0 and 120 °C." => "Температура должна быть от 0 до 120 °C.",
        "Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points." => "Температуры должны возрастать с шагом не менее 1 °C.",
        "SmoothingSeconds must be between 0.1 and 5.0." => "Сглаживание должно быть от 0,1 до 5,0 секунд.",
        _ => "Проверьте параметры температурного профиля.",
    };
}
