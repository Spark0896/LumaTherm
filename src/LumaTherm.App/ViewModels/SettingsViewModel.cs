using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using LumaTherm.App.Localization;
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
    private readonly ILocalizationService _localization;
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
    private string _gpuName = string.Empty;
    private string _lightingDeviceName = string.Empty;
    private string _lightingHardwareStatus = string.Empty;
    private string _lightingHardwareStatusKey = "Settings.HardwareUnchecked";
    private string? _lightingHardwareStatusArgument;
    private RuntimeSnapshot _lastSnapshot;
    private string? _selectedLightingDeviceId;
    private string? _unavailableSavedDeviceId;
    private bool _isLightingDeviceSelectorVisible;
    private string? _validationKey;
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
        ILightingDeviceDiscovery lightingDeviceDiscovery,
        ILocalizationService? localization = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _colorPickerService = colorPickerService ?? throw new ArgumentNullException(nameof(colorPickerService));
        _lightingDeviceDiscovery = lightingDeviceDiscovery ?? throw new ArgumentNullException(nameof(lightingDeviceDiscovery));
        _localization = localization ?? LocalizationService.CreateFallback();
        _synchronizationContext = SynchronizationContext.Current;
        _liveSettings = (settings ?? throw new ArgumentNullException(nameof(settings))).Validate();
        _lastSnapshot = runtime.CurrentSnapshot;
        _localization.LanguageChanged += OnLanguageChanged;
        SetLightingHardwareStatus("Settings.HardwareUnchecked");
        LoadEditableValues(_liveSettings);
        ApplySnapshot(_lastSnapshot);
        runtime.SnapshotChanged += OnRuntimeSnapshotChanged;

        SaveCommand = new AsyncRelayCommand(() => RunSettingsMutationAsync(SaveAsync), onException: _ => SetValidation("Validation.SaveFailed"));
        ApplyAutostartCommand = new AsyncRelayCommand(
            () => RunSettingsMutationAsync(token => SaveAsync(token, onlyAutostart: true)),
            onException: _ => SetValidation("Validation.AutostartFailed"));
        OpenStartupSettingsCommand = new RelayCommand(() =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
            catch (Exception) { SetValidation("Validation.OpenWindowsSettingsFailed"); }
        });
        PickSelectedColorCommand = new AsyncRelayCommand(PickSelectedColorAsync, () => SelectedPoint is not null, _ => SetValidation("Validation.ColorPickFailed"));
        DiscoverLightingDevicesCommand = new AsyncRelayCommand(DiscoverLightingDevicesAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        ResetColorsCommand = new RelayCommand(() => LoadEditableProfile(ThermalProfile.Default with { SmoothingSeconds = SmoothingSeconds }));
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
            SetLightingHardwareStatus("Settings.HardwareConnected");
            IsLightingDeviceSelectorVisible = LightingDevices.Count(device => device.IsAvailable) > 1;
        }
    }
    public bool IsLightingDeviceSelectorVisible { get => _isLightingDeviceSelectorVisible; private set => SetProperty(ref _isLightingDeviceSelectorVisible, value); }
    public string? ValidationMessage => _validationKey is null ? null : _localization.Get(_validationKey);
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ApplyAutostartCommand { get; }
    public RelayCommand OpenStartupSettingsCommand { get; }
    public AsyncRelayCommand PickSelectedColorCommand { get; }
    public AsyncRelayCommand DiscoverLightingDevicesCommand { get; }
    public RelayCommand ResetDefaultsCommand { get; }
    public RelayCommand ResetColorsCommand { get; }
    public RelayCommand OpenLightingTestCommand { get; }
    public Task AutostartInitialization { get; }
    public Task RefreshAutostartAsync(CancellationToken cancellationToken) => ReadAuthoritativeAutostartAsync(forceDraftRefresh: true, cancellationToken);
    public Task RefreshAutostartAfterActivationAsync() => ApplyAutostartCommand.IsExecuting || SaveCommand.IsExecuting
        ? Task.CompletedTask : InitializeAutostartAsync(_disposeCancellation.Token);

    internal async Task ApplyLightingTestProfileAsync(ThermalProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var lockTaken = false;
        try
        {
            await _settingsMutationGate.WaitAsync(linkedCancellation.Token);
            lockTaken = true;
            linkedCancellation.Token.ThrowIfCancellationRequested();
            await _runtime.UpdatePreferencesAsync(
                _liveSettings with { Profile = profile },
                linkedCancellation.Token);
            var committed = _runtime.CurrentSettings;
            LoadEditableProfile(committed.Profile);
            Commit(committed, null);
        }
        finally
        {
            if (lockTaken)
            {
                _settingsMutationGate.Release();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCancellation.Cancel();
        _localization.LanguageChanged -= OnLanguageChanged;
        _runtime.SnapshotChanged -= OnRuntimeSnapshotChanged;
        DetachProfileEditor();
    }

    private async Task RunSettingsMutationAsync(Func<CancellationToken, Task> mutation)
    {
        if (_disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        var lockTaken = false;
        try
        {
            await _settingsMutationGate.WaitAsync(_disposeCancellation.Token);
            lockTaken = true;
            _disposeCancellation.Token.ThrowIfCancellationRequested();
            await mutation(_disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (lockTaken)
            {
                _settingsMutationGate.Release();
            }
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
            SetLightingHardwareStatus("Settings.HardwareDiscoveryFailed");
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
            LightingDevices.Add(new LightingDeviceInfo(savedId, Format("Settings.UnavailableDeviceFormat", savedId), 0, false));
            _unavailableSavedDeviceId = savedId;
        }

        SelectedLightingDeviceId = _liveSettings.PreferredLightingDeviceId
            ?? devices.FirstOrDefault(device => device.IsAvailable)?.Id;
        var selectedDevice = LightingDevices.FirstOrDefault(device => device.Id == SelectedLightingDeviceId);
        IsLightingDeviceSelectorVisible = devices.Count(device => device.IsAvailable) > 1
            || selectedDevice is { IsAvailable: false };
        if (selectedDevice is { IsAvailable: false })
        {
            LightingDeviceName = _localization.Get("Runtime.LightingUnavailable");
            SetLightingHardwareStatus("Settings.HardwareSavedUnavailable");
        }
        else if (devices.Count == 0)
        {
            SetLightingHardwareStatus("Settings.HardwareNotFound");
        }
        else if (selectedDevice is { IsAvailable: true } selected)
        {
            LightingDeviceName = selected.Name;
            SetLightingHardwareStatus("Settings.HardwareConnected");
        }
        else
        {
            LightingDeviceName = _localization.Get("Runtime.LightingUnavailable");
            SetLightingHardwareStatus("Settings.HardwareSavedUnavailable");
        }
    }

    private Task SaveAsync(CancellationToken cancellationToken) => SaveAsync(cancellationToken, onlyAutostart: false);

    private async Task SaveAsync(CancellationToken cancellationToken, bool onlyAutostart)
    {
        await AutostartInitialization;
        cancellationToken.ThrowIfCancellationRequested();
        AppSettings candidate;

        try
        {
            candidate = onlyAutostart ? _liveSettings with { IsAutostartEnabled = IsAutostartEnabled } : CreateCandidate();
            await _runtime.UpdatePreferencesAsync(candidate, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (ArgumentException exception)
        {
            SetValidation(ValidationKey(exception));
            return;
        }
        catch (InvalidOperationException)
        {
            SetValidation("Validation.SaveFailed");
            return;
        }

        var autostartChanged = _hasAuthoritativeAutostart
            ? candidate.IsAutostartEnabled != _authoritativeAutostartEnabled
            : candidate.IsAutostartEnabled != _liveSettings.IsAutostartEnabled;
        if (autostartChanged)
        {
            try
            {
                await _startupService.SetEnabledAsync(candidate.IsAutostartEnabled, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _authoritativeAutostartEnabled = candidate.IsAutostartEnabled;
                _hasAuthoritativeAutostart = true;
                _isAutostartDirty = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var key = await RollBackRuntimeAfterStartupFailureAsync(cancellationToken);
                if (key == "Validation.AutostartFailed" && exception is StartupPermissionException blocked)
                    key = blocked.Reason == StartupBlockReason.User ? "Validation.AutostartDisabledByUser" : "Validation.AutostartDisabledByPolicy";
                SetValidation(key);
                if (onlyAutostart) ApplyAuthoritativeAutostart(_authoritativeAutostartEnabled, markKnown: true);
                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _isAutostartDirty = false;
        Commit(_runtime.CurrentSettings, null);
    }

    private void ResetDefaults()
    {
        LoadEditableValues(AppSettings.Default);
        _isAutostartDirty = true;
        SetValidation(null);
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
        LoadEditableProfile(settings.Profile);
        ApplyAuthoritativeAutostart(settings.IsAutostartEnabled, markKnown: false);
        MinimizeToTray = settings.MinimizeToTray;
        NotificationsEnabled = settings.NotificationsEnabled;
        SelectedLightingDeviceId = settings.PreferredLightingDeviceId;
        SelectedLanguage = settings.Language;
        ShowTrayTemperature = settings.TrayMenu.ShowTemperature;
        ShowTrayOpen = settings.TrayMenu.ShowOpenCommand;
        ShowTrayModeToggle = settings.TrayMenu.ShowModeToggle;
    }

    private void LoadEditableProfile(ThermalProfile profile)
    {
        ProfileEditor = new ThermalProfileEditorViewModel(profile);
        if (ProfileEditor.Points.FirstOrDefault() is { } first)
        {
            ProfileEditor.Select(first.Id);
            RefreshSelectedPoint();
        }

        SmoothingSeconds = profile.SmoothingSeconds;
    }

    private async Task<string> RollBackRuntimeAfterStartupFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.UpdatePreferencesAsync(_liveSettings, cancellationToken);
            return "Validation.AutostartFailed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return "Validation.AutostartRollbackFailed";
        }
    }

    private void Commit(AppSettings candidate, string? warningKey)
    {
        _liveSettings = candidate;
        var observerFailed = OnPropertyChanged(nameof(LiveSettings));
        foreach (EventHandler<ThermalProfile> handler in ProfileSaved?.GetInvocationList() ?? [])
        {
            try { handler(this, candidate.Profile); }
            catch (Exception) { observerFailed = true; }
        }
        SetValidation(observerFailed ? "Validation.UiRefreshFailed" : warningKey);
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
        _lastSnapshot = snapshot;
        TrayTemperaturePreview = snapshot.Temperature is { } reading
            ? string.Create(CultureInfo.InvariantCulture, $"{reading.Celsius:0}°C")
            : "—";
        GpuName = snapshot.Temperature?.DeviceName ?? _localization.Get("Runtime.GpuNotFound");
        var selectedDevice = LightingDevices.FirstOrDefault(device => device.Id == SelectedLightingDeviceId);
        LightingDeviceName = snapshot.LightingDevice?.Name ?? selectedDevice switch
        {
            { IsAvailable: false } => _localization.Get("Runtime.LightingUnavailable"),
            { IsAvailable: true } => selectedDevice.Name,
            _ => _localization.Get("Runtime.LightingNotFound"),
        };
    }

    private static string ValidationKey(ArgumentException exception) => exception.Message switch
    {
        "Temperatures must be between 0 and 120 °C." => "Validation.TemperatureRange",
        "Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points." => "Validation.TemperatureOrder",
        "Temperatures must be at least 1 °C apart." => "Validation.TemperatureOrder",
        "SmoothingSeconds must be between 0.1 and 5.0." => "Validation.SmoothingRange",
        _ => "Validation.Profile",
    };

    private async Task InitializeAutostartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReadAuthoritativeAutostartAsync(forceDraftRefresh: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                SetValidation("Validation.AutostartReadFailed");
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

    private void SetValidation(string? key)
    {
        if (_validationKey == key) return;
        _validationKey = key;
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void SetLightingHardwareStatus(string key, string? argument = null)
    {
        _lightingHardwareStatusKey = key;
        _lightingHardwareStatusArgument = argument;
        LightingHardwareStatus = argument is null ? _localization.Get(key) : Format(key, argument);
    }

    private string Format(string key, object argument)
    {
        var culture = _localization.CurrentLanguage == AppLanguage.Russian
            ? CultureInfo.GetCultureInfo("ru-RU")
            : CultureInfo.GetCultureInfo("en-US");
        return string.Format(culture, _localization.Get(key), argument);
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        void Refresh()
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(ValidationMessage));
            SetLightingHardwareStatus(_lightingHardwareStatusKey, _lightingHardwareStatusArgument);
            ApplySnapshot(_lastSnapshot);
            if (_unavailableSavedDeviceId is { } staleId
                && LightingDevices.FirstOrDefault(device => device.Id == staleId) is { } stale)
            {
                var index = LightingDevices.IndexOf(stale);
                LightingDevices[index] = stale with { Name = Format("Settings.UnavailableDeviceFormat", staleId) };
            }
        }

        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext)) Refresh();
        else _synchronizationContext.Post(_ => Refresh(), null);
    }
    private sealed record SnapshotUpdate(SettingsViewModel ViewModel, RuntimeSnapshot Snapshot);
}
