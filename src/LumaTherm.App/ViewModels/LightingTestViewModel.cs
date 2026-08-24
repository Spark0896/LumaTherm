using System.Collections.Specialized;
using System.ComponentModel;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;

namespace LumaTherm.App.ViewModels;

public sealed class LightingTestViewModel : ObservableObject, IAsyncDisposable
{
    public const double MinimumTestTemperature = 0;
    public const double MaximumTestTemperature = 120;

    private readonly IThermalRuntime _runtime;
    private readonly Func<ThermalProfile, CancellationToken, Task> _saveProfileAsync;
    private readonly SemaphoreSlim _openGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly SynchronizationContext? _synchronizationContext;
    private ILightingTestSession? _session;
    private Task _temperatureUpdate = Task.CompletedTask;
    private Task? _completion;
    private double _testTemperature = 60;
    private RgbColor _previewColor;
    private string _errorMessage = string.Empty;
    private bool _editorDetached;

    public LightingTestViewModel(
        IThermalRuntime runtime,
        ThermalProfile profile,
        Func<ThermalProfile, CancellationToken, Task> saveProfileAsync)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _saveProfileAsync = saveProfileAsync ?? throw new ArgumentNullException(nameof(saveProfileAsync));
        _synchronizationContext = SynchronizationContext.Current;
        var validatedProfile = (profile ?? throw new ArgumentNullException(nameof(profile))).Validate();
        Editor = new ThermalProfileEditorViewModel(validatedProfile);
        SmoothingSeconds = validatedProfile.SmoothingSeconds;
        AttachEditor();
        _previewColor = MapPreviewColor();
    }

    public ThermalProfileEditorViewModel Editor { get; }
    public double SmoothingSeconds { get; }

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
        private set => SetProperty(ref _previewColor, value);
    }

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
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
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

            QueueTemperatureUpdate(TestTemperature);
            await TemperatureUpdate;
        }
        finally
        {
            _openGate.Release();
        }
    }

    public Task ApplyAsync() => ApplyAsync(CancellationToken.None);
    public Task ApplyAsync(CancellationToken cancellationToken) => CompleteAsync(apply: true, cancellationToken);
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
            ErrorMessage = exception.Message;
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
                    ErrorMessage = exception.Message;
                    failure = failure is null ? exception : new AggregateException(failure, exception);
                }
            }

            DetachEditor();
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
            SetErrorMessage(string.Empty);
        }
        catch (Exception exception)
        {
            SetErrorMessage(exception.Message);
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
    }

    private void OnPointChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ThermalPointEditorViewModel.Temperature)
            or nameof(ThermalPointEditorViewModel.Color))
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        try
        {
            PreviewColor = MapPreviewColor();
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private RgbColor MapPreviewColor()
    {
        var profile = Editor.BuildProfile(SmoothingSeconds);
        return new ColorEngine(profile, TestTemperature).Map(TestTemperature);
    }

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
