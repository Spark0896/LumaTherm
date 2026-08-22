using LumaTherm.Core.Colors;

namespace LumaTherm.App.ViewModels;

public sealed class ThermalPointEditorViewModel : ObservableObject
{
    private double _temperature;
    private RgbColor _color;
    private bool _isSelected;

    internal ThermalPointEditorViewModel(Guid id, double temperature, RgbColor color)
    {
        Id = id;
        _temperature = temperature;
        _color = color;
    }

    public Guid Id { get; }

    public double Temperature
    {
        get => _temperature;
        internal set => SetProperty(ref _temperature, value);
    }

    public RgbColor Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}
