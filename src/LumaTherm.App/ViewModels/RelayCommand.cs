using System.Windows.Input;

namespace LumaTherm.App.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute(parameter);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private readonly Action<Exception>? _onException;
    private int _isExecuting;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null, Action<Exception>? onException = null)
        : this(_ => executeAsync(), canExecute is null ? null : _ => canExecute(), onException)
    {
    }

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null, Action<Exception>? onException = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
        _onException = onException;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public bool CanExecute(object? parameter) => !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter) || Interlocked.Exchange(ref _isExecuting, 1) != 0)
        {
            return;
        }

        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _executeAsync(parameter);
        }
        catch (Exception exception)
        {
            _onException?.Invoke(exception);
        }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
