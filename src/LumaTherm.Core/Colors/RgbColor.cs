using System.Globalization;

namespace LumaTherm.Core.Colors;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool TryParseHex(string? value, out RgbColor color)
    {
        color = default;
        if (value is null)
        {
            return false;
        }

        var digits = value.AsSpan();
        if (!digits.IsEmpty && digits[0] == '#')
        {
            digits = digits[1..];
        }

        if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = new RgbColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
