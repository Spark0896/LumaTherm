using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LumaTherm.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected bool OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var failed = false;
        foreach (PropertyChangedEventHandler handler in PropertyChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, new PropertyChangedEventArgs(propertyName)); }
            catch (Exception) { failed = true; }
        }
        return failed;
    }
}
