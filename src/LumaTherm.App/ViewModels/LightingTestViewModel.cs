using System.Collections.Specialized;
using System.ComponentModel;
using LumaTherm.App.Localization;
using LumaTherm.App.Services;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;

namespace LumaTherm.App.ViewModels;

public sealed class LightingTestViewModel : ObservableObject, IAsyncDisposable
{
    public const double MinimumTestTemperature = 0;
    public const double MaximumTestTemperature = 120;

    private readonly IThermalRuntime _runtime;
    private readonly Func<ThermalProfile, CancellationToken, Task> _saveProfileAsync;
    private readonly ILocalizationService _localization;
    private readonly IColorPickerService _colorPickerService;
    private readonly SemaphoreSlim _openGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly SynchronizationContext? _synchronizationContext;
    private ILightingTestSession? _session;
    private Task _temperatureUpdate = Task.CompletedTask;
    private Task? _completion;
    private double _testTemperature = 60;
    private RgbColor _previewColor;
    private string _errorMessage = string.Empty;
    private string? _errorKey;
    private bool _editorDetached;
    private ThermalPointEditorViewModel? _selectedPoint;

    public LightingTestViewModel(
        IThermalRuntime runtime,
        ThermalProfile profile,
        Func<ThermalProfile, CancellationToken, Task> saveProfileAsync,
        ILocalizationService? localization = null,
        IColorPickerService? colorPickerService = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _saveProfileAsync = saveProfileAsync ?? throw new ArgumentNullException(nameof(saveProfileAsync));
        _localization = localization ?? LocalizationService.CreateFallback();
        _colorPickerService = colorPickerService ?? NullColorPickerService.Instance;
        _synchronizationContext = SynchronizationContext.Current;
        var validatedProfile = (profile ?? throw new ArgumentNullException(nameof(profile))).Validate();
        Editor = new ThermalProfileEditorViewModel(validatedProfile);
        Editor.Select(Editor.Points[0].Id);
        SmoothingSeconds = validatedProfile.SmoothingSeconds;
        AttachEditor();
        RefreshSelectedPoint();
        _localization.LanguageChanged += OnLanguageChanged;
        _previewColor = MapPreviewColor();
        PickSelectedColorCommand = new AsyncRelayCommand(
            PickSelectedColorAsync,
            () => SelectedPoint is not null,
            _ => SetErrorKey("Validation.ColorPickFailed"));
    }

    public ThermalProfileEditorViewModel Editor { get; }
    public double SmoothingSeconds { get; }
    public AsyncRelayCommand PickSelectedColorCommand { get; }
    public ThermalPointEditorViewModel? SelectedPoint
    {
        get => _selectedPoint;
        private set => SetProperty(ref _selectedPoint, value);
    }
    public double SelectedPointTemperature
    {
        get => SelectedPoint?.Temperature ?? 0;
        set
        {
            if (SelectedPoint is not { } point)
            {
                return;
            }

            Editor.Move(point.Id, value);
            OnPropertyChanged();
        }
    }
    public string SelectedPointColorHex => SelectedPoint?.Color.ToHex() ?? string.Empty;

    public double TestTemperature
    {
        get => _testTemperature;
        set
        {
            if (!double.IsFinite(value) || value is < MinimumTestTemperature or > MaximumTestTemperature)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Test temperature must be between 0 and 120 °C.");
            }

            if (!SetProperty(ref _testTemperature, value))
            {
                return;
            }

            RefreshPreview();
            QueueTemperatureUpdate(value);
        }
    }

    public RgbColor PreviewColor
    {
        get => _previewColor;
        private set { if (SetProperty(ref _previewColor, value)) OnPropertyChanged(nameof(PreviewHex)); }
    }
    public string PreviewHex => PreviewColor.ToHex();
    public System.Windows.Media.LinearGradientBrush PreviewGradient =>
        LumaTherm.App.Controls.ThermalGradientBrush.Create(Editor.BuildProfile(SmoothingSeconds), 0, 120);

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public Task TemperatureUpdate
    {
        get
        {
            lock (_stateLock)
            {
                return _temperatureUpdate;
            }
        }
    }

    public Task OpenAsync() => OpenAsync(CancellationToken.None);

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        await _openGate.WaitAsync(cancellationToken);
        try
        {
            lock (_stateLock)
            {
                if (_session is not null)
                {
                    return;
                }

                if (_completion is not null)
                {
                    throw new InvalidOperationException("A completed lighting test cannot be reopened.");
                }
            }

            ILightingTestSession session;
            try
            {
                session = await _runtime.BeginLightingTestAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                SetErrorKey("TestWindow.OpenFailed");
                throw;
            }

            var disposeImmediately = false;
            lock (_stateLock)
            {
                if (_completion is null)
                {
                    _session = session;
                }
                else
                {
                    disposeImmediately = true;
                }
            }

            if (disposeImmediately)
            {
                await session.DisposeAsync();
                return;
            }

            QueueProfileUpdate(Editor.BuildProfile(SmoothingSeconds));
            QueueTemperatureUpdate(TestTemperature);
            await TemperatureUpdate;
        }
        finally
        {
            _openGate.Release();
        }
    }

    public Task ApplyAsync() => ApplyAsync(CancellationToken.None);
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        SetErrorKey(null);
        try { await CompleteAsync(apply: true, cancellationToken); }
        catch
        {
            // The physical test has been released. Keep the editable draft so
            // a transient persistence failure can be retried without reopening it.
            lock (_stateLock) _completion = null;
            throw;
        }
    }
    public Task CancelAsync() => CancelAsync(CancellationToken.None);
    public Task CancelAsync(CancellationToken cancellationToken) => CompleteAsync(apply: false, cancellationToken);
    public Task CloseAsync() => CloseAsync(CancellationToken.None);
    public Task CloseAsync(CancellationToken cancellationToken) => CompleteAsync(apply: false, cancellationToken);

    public async ValueTask DisposeAsync() => await CloseAsync();

    private Task CompleteAsync(bool apply, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            return _completion ??= CompleteCoreAsync(apply, cancellationToken);
        }
    }

    private async Task CompleteCoreAsync(bool apply, CancellationToken cancellationToken)
    {
        await _openGate.WaitAsync(CancellationToken.None);
        try
        {
            await CompleteOwnedSessionAsync(apply, cancellationToken);
        }
        finally
        {
            _openGate.Release();
        }
    }

    private async Task CompleteOwnedSessionAsync(bool apply, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        ILightingTestSession? session;
        Task temperatureUpdate;
        lock (_stateLock)
        {
            session = _session;
            temperatureUpdate = _temperatureUpdate;
        }

        try
        {
            await temperatureUpdate;
            if (apply)
            {
                await _saveProfileAsync(Editor.BuildProfile(SmoothingSeconds), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            SetErrorKey(apply ? "TestWindow.ApplyFailed" : "TestWindow.CloseFailed");
        }
        finally
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                }
            }

            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch (Exception exception)
                {
                    SetErrorKey("TestWindow.CloseFailed");
                    failure = failure is null ? exception : new AggregateException(failure, exception);
                }
            }

            if (!apply || failure is null) DetachEditor();
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void QueueTemperatureUpdate(double temperature)
    {
        lock (_stateLock)
        {
            if (_session is not { } session || _completion is not null)
            {
                return;
            }

            _temperatureUpdate = _temperatureUpdate.IsCompleted
                ? SendTemperatureAsync(session, temperature)
                : SendTemperatureAfterAsync(_temperatureUpdate, session, temperature);
        }
    }

    private async Task SendTemperatureAfterAsync(Task previous, ILightingTestSession session, double temperature)
    {
        await previous;
        await SendTemperatureAsync(session, temperature);
    }

    private async Task SendTemperatureAsync(ILightingTestSession session, double temperature)
    {
        try
        {
            await session.SetTemperatureAsync(temperature, CancellationToken.None);
            if (_errorKey == "TestWindow.LightingFailed") SetErrorKey(null);
        }
        catch (Exception)
        {
            SetErrorKey("TestWindow.LightingFailed");
        }
    }

    private void AttachEditor()
    {
        Editor.Points.CollectionChanged += OnPointsChanged;
        foreach (var point in Editor.Points)
        {
            point.PropertyChanged += OnPointChanged;
        }
    }

    private void DetachEditor()
    {
        if (_editorDetached)
        {
            return;
        }

        _editorDetached = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        Editor.Points.CollectionChanged -= OnPointsChanged;
        foreach (var point in Editor.Points)
        {
            point.PropertyChanged -= OnPointChanged;
        }
    }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null)
        {
            foreach (ThermalPointEditorViewModel point in args.OldItems)
            {
                point.PropertyChanged -= OnPointChanged;
            }
        }

        if (args.NewItems is not null)
        {
            foreach (ThermalPointEditorViewModel point in args.NewItems)
            {
                point.PropertyChanged += OnPointChanged;
            }
        }

        RefreshPreview();
        RefreshSelectedPoint();
    }

    private void OnPointChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ThermalPointEditorViewModel.IsSelected))
        {
            RefreshSelectedPoint();
        }
        else if (args.PropertyName is nameof(ThermalPointEditorViewModel.Temperature)
            or nameof(ThermalPointEditorViewModel.Color))
        {
            if (ReferenceEquals(sender, SelectedPoint))
            {
                OnPropertyChanged(nameof(SelectedPointTemperature));
                OnPropertyChanged(nameof(SelectedPointColorHex));
            }
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        try
        {
            var profile = Editor.BuildProfile(SmoothingSeconds);
            PreviewColor = new ColorEngine(profile, TestTemperature).Map(TestTemperature);
            OnPropertyChanged(nameof(PreviewGradient));
            QueueProfileUpdate(profile);
            if (_errorKey is "Validation.TemperatureRange" or "Validation.TemperatureOrder" or "Validation.SmoothingRange" or "Validation.Profile")
            {
                SetErrorKey(null);
            }
        }
        catch (ArgumentException exception)
        {
            SetErrorKey(ValidationKey(exception));
        }
    }

    private RgbColor MapPreviewColor()
    {
        var profile = Editor.BuildProfile(SmoothingSeconds);
        return new ColorEngine(profile, TestTemperature).Map(TestTemperature);
    }

    private Task PickSelectedColorAsync()
    {
        if (SelectedPoint is { } point && _colorPickerService.Pick(point.Color) is { } selected)
        {
            point.Color = selected;
        }

        return Task.CompletedTask;
    }

    private void RefreshSelectedPoint()
    {
        SelectedPoint = Editor.Points.FirstOrDefault(point => point.IsSelected);
        OnPropertyChanged(nameof(SelectedPointTemperature));
        OnPropertyChanged(nameof(SelectedPointColorHex));
    }

    private void QueueProfileUpdate(ThermalProfile profile)
    {
        lock (_stateLock)
        {
            if (_session is not { } session || _completion is not null)
            {
                return;
            }

            _temperatureUpdate = _temperatureUpdate.IsCompleted
                ? SendProfileAsync(session, profile)
                : SendProfileAfterAsync(_temperatureUpdate, session, profile);
        }
    }

    private async Task SendProfileAfterAsync(Task previous, ILightingTestSession session, ThermalProfile profile)
    {
        await previous;
        await SendProfileAsync(session, profile);
    }

    private async Task SendProfileAsync(ILightingTestSession session, ThermalProfile profile)
    {
        try
        {
            await session.SetProfileAsync(profile, CancellationToken.None);
            if (_errorKey == "TestWindow.LightingFailed") SetErrorKey(null);
        }
        catch (Exception)
        {
            SetErrorKey("TestWindow.LightingFailed");
        }
    }

    private void SetErrorKey(string? key)
    {
        _errorKey = key;
        SetErrorMessage(key is null ? string.Empty : _localization.Get(key));
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        if (_errorKey is not null) SetErrorMessage(_localization.Get(_errorKey));
    }

    private static string ValidationKey(ArgumentException exception) => exception.Message switch
    {
        "Temperatures must be between 0 and 120 °C." => "Validation.TemperatureRange",
        "Expected ColdTemperature < WarmTemperature < HotTemperature with at least 1 °C between points." => "Validation.TemperatureOrder",
        "Temperatures must be at least 1 °C apart." => "Validation.TemperatureOrder",
        "SmoothingSeconds must be between 0.1 and 5.0." => "Validation.SmoothingRange",
        _ => "Validation.Profile",
    };

    private void SetErrorMessage(string message)
    {
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            ErrorMessage = message;
            return;
        }

        _synchronizationContext.Post(_ => ErrorMessage = message, null);
    }
}
