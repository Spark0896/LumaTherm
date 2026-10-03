using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LumaTherm.App.ViewModels;

namespace LumaTherm.App.Views;

public partial class LightingTestWindow : Window
{
    private bool _closeInProgress;
    private bool _cleanupCompleted;

    public LightingTestWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    public LightingTestWindow(LightingTestViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    internal LightingTestViewModel? ViewModel => DataContext as LightingTestViewModel;

    internal async Task PrepareCloseAsync()
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.CloseAsync();
        }
    }

    internal void CloseAfterCleanup()
    {
        _cleanupCompleted = true;
        Close();
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs args)
    {
        if (_cleanupCompleted)
        {
            return;
        }

        args.Cancel = true;
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        try
        {
            await PrepareCloseAsync();
        }
        catch (Exception)
        {
            // The ViewModel records the cleanup error for diagnostics; closing is reissued only after the attempt completes.
        }
        finally
        {
            _cleanupCompleted = true;
            _closeInProgress = false;
            await Dispatcher.InvokeAsync(Close, DispatcherPriority.Send);
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs args)
    {
        _closeInProgress = true;
        ApplyButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        try
        {
            if (ViewModel is { } viewModel)
            {
                await viewModel.ApplyAsync();
            }
            CloseAfterCleanup();
        }
        catch (Exception)
        {
            // Keep the localized error and draft visible for a retry.
        }
        finally
        {
            _closeInProgress = false;
            ApplyButton.ClearValue(IsEnabledProperty);
            CancelButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
        }
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            if (ViewModel is { } viewModel)
            {
                await viewModel.CancelAsync();
            }
        }
        catch (Exception)
        {
            // Cleanup was attempted and the ViewModel retains the error.
        }
        finally
        {
            CloseAfterCleanup();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs args) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
