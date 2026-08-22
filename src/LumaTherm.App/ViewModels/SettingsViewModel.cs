using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using LumaTherm.App.Services;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IThermalRuntime _runtime;
    private readonly IStartupService _startupService;
    private readonly IColorPickerService _colorPickerService;
    private readonly ILightingDeviceDiscovery _lightingDeviceDiscovery;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly SemaphoreSlim _settingsMutationGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private AppSettings _liveSettings;
    private ThermalProfileEditorViewModel _profileEditor = null!;
    private ThermalPointEditorViewModel? _selectedPoint;
    private double _smoothingSeconds;
    private bool _isAutostartEnabled;
    private bool _minimizeToTray;
    private bool _notificationsEnabled;
    private AppLanguage _selectedLanguage;
    private bool _showTrayTemperature;
    private bool _showTrayOpen;
    private bool _showTrayModeToggle;
    private string _trayTemperaturePreview = "—";
    private string _gpuName = "GPU не обнаружен";
    private string _lightingDeviceName = "Подсветка не обнаружена";
    private string _lightingHardwareStatus = "Устройства Windows LampArray не проверены.";
    private string? _selectedLightingDeviceId;
    private string? _unavailableSavedDeviceId;
    private bool _isLightingDeviceSelectorVisible;
    private string? _validationMessage;
    private bool _disposed;
    private bool _isApplyingAuthoritativeAutostart;
    private bool _isAutostartDirty;
    private bool _hasAuthoritativeAutostart;
    private bool _authoritativeAutostartEnabled;

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
        _synchronizationContext = SynchronizationContext.Current;
        _liveSettings = (settings ?? throw new ArgumentNullException(nameof(settings))).Validate();
        LoadEditableValues(_liveSettings);
        ApplySnapshot(runtime.CurrentSnapshot);
        runtime.SnapshotChanged += OnRuntimeSnapshotChanged;

        SaveCommand = new AsyncRelayCommand(() => RunSettingsMutationAsync(SaveAsync), onException: _ => ValidationMessage = "Не удалось сохранить настройки.");
        PickSelectedColorCommand = new AsyncRelayCommand(PickSelectedColorAsync, () => SelectedPoint is not null, _ => ValidationMessage = "Не удалось выбрать цвет.");
        DiscoverLightingDevicesCommand = new AsyncRelayCommand(DiscoverLightingDevicesAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        OpenLightingTestCommand = new RelayCommand(() => LightingTestRequested?.Invoke(this, EventArgs.Empty));
        AutostartInitialization = InitializeAutostartAsync(_disposeCancellation.Token);
    }

    internal event EventHandler<ThermalProfile>? ProfileSaved;
    public event EventHandler? LightingTestRequested;

    public AppSettings LiveSettings => _liveSettings;
    public ThermalProfileEditorViewModel ProfileEditor
    {
        get => _profileEditor;
        private set
        {
            if (ReferenceEquals(_profileEditor, value))
            {
                return;
            }

            DetachProfileEditor();
            _profileEditor = value;
            AttachProfileEditor();
            OnPropertyChanged();
        }
    }
    public ThermalPointEditorViewModel? SelectedPoint
    {
        get => _selectedPoint;
        private set
        {
            SetProperty(ref _selectedPoint, value);
        }
    }
    public IReadOnlyList<AppLanguage> AvailableLanguages { get; } = [AppLanguage.System, AppLanguage.Russian, AppLanguage.English];
    public AppLanguage SelectedLanguage { get => _selectedLanguage; set => SetProperty(ref _selectedLanguage, value); }
    public bool ShowTrayTemperature { get => _showTrayTemperature; set => SetProperty(ref _showTrayTemperature, value); }
    public bool ShowTrayOpen { get => _showTrayOpen; set => SetProperty(ref _showTrayOpen, value); }
    public bool ShowTrayModeToggle { get => _showTrayModeToggle; set => SetProperty(ref _showTrayModeToggle, value); }
    public string TrayTemperaturePreview { get => _trayTemperaturePreview; private set => SetProperty(ref _trayTemperaturePreview, value); }
    public double SelectedPointTemperature
    {
        get => SelectedPoint?.Temperature ?? 0;
        set
        {
            if (SelectedPoint is not { } point)
            {
                return;
            }

            ProfileEditor.Move(point.Id, value);
            OnPropertyChanged();
        }
    }
    public string SelectedPointColorHex => SelectedPoint?.Color.ToHex() ?? string.Empty;
    public double SmoothingSeconds { get => _smoothingSeconds; set => SetProperty(ref _smoothingSeconds, value); }
    public bool IsAutostartEnabled
    {
        get => _isAutostartEnabled;
        set
        {
            if (SetProperty(ref _isAutostartEnabled, value) && !_isApplyingAuthoritativeAutostart)
            {
                _isAutostartDirty = true;
            }
        }
    }
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
    public AsyncRelayCommand PickSelectedColorCommand { get; }
    public AsyncRelayCommand DiscoverLightingDevicesCommand { get; }
    public RelayCommand ResetDefaultsCommand { get; }
    public RelayCommand OpenLightingTestCommand { get; }
    public Task AutostartInitialization { get; }
    public Task RefreshAutostartAsync(CancellationToken cancellationToken) => ReadAuthoritativeAutostartAsync(forceDraftRefresh: true, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCancellation.Cancel();
        _runtime.SnapshotChanged -= OnRuntimeSnapshotChanged;
        DetachProfileEditor();
    }

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

    private Task PickSelectedColorAsync()
    {
        if (SelectedPoint is not { } point)
        {
            return Task.CompletedTask;
        }

        if (_colorPickerService.Pick(point.Color) is { } selected)
        {
            point.Color = selected;
        }

        return Task.CompletedTask;
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
        await AutostartInitialization;
        AppSettings candidate;

        try
        {
            candidate = CreateCandidate();
            await _runtime.UpdatePreferencesAsync(candidate, CancellationToken.None);
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

        var autostartChanged = _hasAuthoritativeAutostart
            ? candidate.IsAutostartEnabled != _authoritativeAutostartEnabled
            : candidate.IsAutostartEnabled != _liveSettings.IsAutostartEnabled;
        if (autostartChanged)
        {
            try
            {
                await _startupService.SetEnabledAsync(candidate.IsAutostartEnabled, CancellationToken.None);
                _authoritativeAutostartEnabled = candidate.IsAutostartEnabled;
                _hasAuthoritativeAutostart = true;
                _isAutostartDirty = false;
            }
            catch (Exception)
            {
                ValidationMessage = await RollBackRuntimeAfterStartupFailureAsync();
                return;
            }
        }

        _isAutostartDirty = false;
        Commit(_runtime.CurrentSettings, null);
    }

    private void ResetDefaults()
    {
        LoadEditableValues(AppSettings.Default);
        ValidationMessage = null;
    }

    private AppSettings CreateCandidate() => _liveSettings with
    {
        Profile = ProfileEditor.BuildProfile(SmoothingSeconds),
        IsAutostartEnabled = IsAutostartEnabled,
        MinimizeToTray = MinimizeToTray,
        NotificationsEnabled = NotificationsEnabled,
        PreferredLightingDeviceId = SelectedLightingDeviceId,
        Language = SelectedLanguage,
        TrayMenu = new TrayMenuOptions(ShowTrayTemperature, ShowTrayOpen, ShowTrayModeToggle),
    };

    private void LoadEditableValues(AppSettings settings)
    {
        ProfileEditor = new ThermalProfileEditorViewModel(settings.Profile);
        if (ProfileEditor.Points.FirstOrDefault() is { } first)
        {
            ProfileEditor.Select(first.Id);
            RefreshSelectedPoint();
        }

        SmoothingSeconds = settings.Profile.SmoothingSeconds;
        ApplyAuthoritativeAutostart(settings.IsAutostartEnabled, markKnown: false);
        MinimizeToTray = settings.MinimizeToTray;
        NotificationsEnabled = settings.NotificationsEnabled;
        SelectedLightingDeviceId = settings.PreferredLightingDeviceId;
        SelectedLanguage = settings.Language;
        ShowTrayTemperature = settings.TrayMenu.ShowTemperature;
        ShowTrayOpen = settings.TrayMenu.ShowOpenCommand;
        ShowTrayModeToggle = settings.TrayMenu.ShowModeToggle;
    }

    private async Task<string> RollBackRuntimeAfterStartupFailureAsync()
    {
        try
        {
            await _runtime.UpdatePreferencesAsync(_liveSettings, CancellationToken.None);
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

    private void AttachProfileEditor()
    {
        ProfileEditor.Points.CollectionChanged += OnProfilePointsChanged;
        foreach (var point in ProfileEditor.Points)
        {
            point.PropertyChanged += OnProfilePointChanged;
        }
    }

    private void DetachProfileEditor()
    {
        if (_profileEditor is null)
        {
            return;
        }

        _profileEditor.Points.CollectionChanged -= OnProfilePointsChanged;
        foreach (var point in _profileEditor.Points)
        {
            point.PropertyChanged -= OnProfilePointChanged;
        }
    }

    private void OnProfilePointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ThermalPointEditorViewModel point in e.OldItems)
            {
                point.PropertyChanged -= OnProfilePointChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ThermalPointEditorViewModel point in e.NewItems)
            {
                point.PropertyChanged += OnProfilePointChanged;
            }
        }

        RefreshSelectedPoint();
    }

    private void OnProfilePointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThermalPointEditorViewModel.IsSelected))
        {
            RefreshSelectedPoint();
        }
        else if (e.PropertyName == nameof(ThermalPointEditorViewModel.Temperature)
            && ReferenceEquals(sender, SelectedPoint))
        {
            OnPropertyChanged(nameof(SelectedPointTemperature));
        }
        else if (e.PropertyName == nameof(ThermalPointEditorViewModel.Color)
            && ReferenceEquals(sender, SelectedPoint))
        {
            OnPropertyChanged(nameof(SelectedPointColorHex));
        }
    }

    private void RefreshSelectedPoint()
    {
        SelectedPoint = ProfileEditor.Points.FirstOrDefault(point => point.IsSelected);
        OnPropertyChanged(nameof(SelectedPointTemperature));
        OnPropertyChanged(nameof(SelectedPointColorHex));
    }

    private void OnRuntimeSnapshotChanged(object? sender, RuntimeSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        if (_synchronizationContext is { } context && SynchronizationContext.Current != context)
        {
            context.Post(static state =>
            {
                var update = (SnapshotUpdate)state!;
                if (!update.ViewModel._disposed)
                {
                    update.ViewModel.ApplySnapshot(update.Snapshot);
                }
            }, new SnapshotUpdate(this, snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(RuntimeSnapshot snapshot)
    {
        TrayTemperaturePreview = snapshot.Temperature is { } reading
            ? string.Create(CultureInfo.InvariantCulture, $"{reading.Celsius:0}°C")
            : "—";
        GpuName = snapshot.Temperature?.DeviceName ?? GpuName;
        LightingDeviceName = snapshot.LightingDevice?.Name ?? LightingDeviceName;
    }

    private static string TranslateValidationError(ArgumentException exception) => exception.Message switch
    {
        "Temperatures must be between 0 and 120 °C." => "Температура должна быть от 0 до 120 °C.",
        "Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points." => "Температуры должны возрастать с шагом не менее 1 °C.",
        "Temperatures must be at least 1 °C apart." => "Температуры должны возрастать с шагом не менее 1 °C.",
        "SmoothingSeconds must be between 0.1 and 5.0." => "Сглаживание должно быть от 0,1 до 5,0 секунд.",
        _ => "Проверьте параметры температурного профиля.",
    };

    private async Task InitializeAutostartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReadAuthoritativeAutostartAsync(forceDraftRefresh: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                ValidationMessage = "Не удалось определить состояние автозапуска.";
            }
        }
    }

    private async Task ReadAuthoritativeAutostartAsync(bool forceDraftRefresh, CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
        var enabled = await _startupService.GetEnabledAsync(linkedCancellation.Token);
        linkedCancellation.Token.ThrowIfCancellationRequested();
        if (_disposed)
        {
            return;
        }

        _authoritativeAutostartEnabled = enabled;
        _hasAuthoritativeAutostart = true;
        if (forceDraftRefresh || !_isAutostartDirty)
        {
            ApplyAuthoritativeAutostart(enabled, markKnown: true);
        }
    }

    private void ApplyAuthoritativeAutostart(bool enabled, bool markKnown)
    {
        _isApplyingAuthoritativeAutostart = true;
        try
        {
            IsAutostartEnabled = enabled;
            _isAutostartDirty = false;
            if (markKnown)
            {
                _authoritativeAutostartEnabled = enabled;
                _hasAuthoritativeAutostart = true;
            }
        }
        finally
        {
            _isApplyingAuthoritativeAutostart = false;
        }
    }
    private sealed record SnapshotUpdate(SettingsViewModel ViewModel, RuntimeSnapshot Snapshot);
}
