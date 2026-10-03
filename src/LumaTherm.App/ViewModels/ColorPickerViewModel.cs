using LumaTherm.Core.Colors;
namespace LumaTherm.App.ViewModels;

public sealed class ColorPickerViewModel : ObservableObject
{
    private RgbColor _color;
    private string _hex;
    private bool _isValid = true;
    public ColorPickerViewModel(RgbColor color) { _color = color; _hex = color.ToHex(); }
    public RgbColor Color => _color;
    public double Red { get => _color.R; set => SetColor(_color with { R = Channel(value) }); }
    public double Green { get => _color.G; set => SetColor(_color with { G = Channel(value) }); }
    public double Blue { get => _color.B; set => SetColor(_color with { B = Channel(value) }); }
    public bool IsValid { get => _isValid; private set => SetProperty(ref _isValid, value); }
    public string Hex
    {
        get => _hex;
        set
        {
            SetProperty(ref _hex, value);
            IsValid = RgbColor.TryParseHex(value.Trim(), out var color);
            if (IsValid) { _color = color; NotifyChannels(); }
        }
    }
    public IReadOnlyList<RgbColor> Presets { get; } = [new(0, 107, 255), new(0, 195, 255), new(0, 255, 160), new(60, 255, 0), new(255, 220, 0), new(255, 105, 0), new(255, 0, 0), new(255, 0, 120), new(208, 0, 255), new(112, 50, 255), new(255, 255, 255), new(0, 0, 0)];
    public void SetColor(RgbColor color)
    {
        _color = color; _hex = color.ToHex(); IsValid = true;
        NotifyChannels(); OnPropertyChanged(nameof(Hex));
    }
    private void NotifyChannels() { OnPropertyChanged(nameof(Color)); OnPropertyChanged(nameof(Red)); OnPropertyChanged(nameof(Green)); OnPropertyChanged(nameof(Blue)); }
    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(double.IsFinite(value) ? value : 0), 0, 255);
}
