using System.Globalization;
using System.Windows.Controls;

namespace LumaTherm.App.Controls;

public sealed class NumericRangeValidationRule : ValidationRule
{
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        if (double.TryParse(value?.ToString(), NumberStyles.Float, cultureInfo, out var number)
            && double.IsFinite(number) && number >= Minimum && number <= Maximum)
            return ValidationResult.ValidResult;
        var template = System.Windows.Application.Current?.TryFindResource("Validation.NumberRange") as string
            ?? "Enter a number between {0} and {1}.";
        return new ValidationResult(false, string.Format(cultureInfo, template, Minimum, Maximum));
    }
}
