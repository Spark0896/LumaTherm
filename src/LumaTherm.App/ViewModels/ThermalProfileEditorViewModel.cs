using System.Collections.ObjectModel;
using LumaTherm.Core.Colors;

namespace LumaTherm.App.ViewModels;

public sealed class ThermalProfileEditorViewModel
{
    private const double MinimumTemperature = 0;
    private const double MaximumTemperature = 120;
    private const double MinimumGap = 1;

    public ThermalProfileEditorViewModel(ThermalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Points = [];
        Load(profile.Validate());
    }

    public ObservableCollection<ThermalPointEditorViewModel> Points { get; }

    public ThermalPointEditorViewModel AddAt(double temperature)
    {
        ValidateTemperature(temperature);
        EnsureAvailableSlot(temperature, excludedPoint: null);

        var snapshot = ThermalProfile.Create(Points.Select(ToThermalPoint), smoothingSeconds: 0.8);
        var point = new ThermalPointEditorViewModel(Guid.NewGuid(), temperature, new ColorEngine(snapshot, temperature).Map(temperature));
        Points.Insert(Points.TakeWhile(existing => existing.Temperature < temperature).Count(), point);
        Select(point.Id);
        return point;
    }

    public void Move(Guid id, double temperature)
    {
        ValidateTemperature(temperature);
        var index = IndexOf(id);
        if (index < 0)
        {
            return;
        }

        var otherPoints = Points.Where(point => point.Id != id).ToArray();
        var insertionIndex = otherPoints.TakeWhile(point => point.Temperature < temperature).Count();
        var minimum = insertionIndex == 0 ? MinimumTemperature : otherPoints[insertionIndex - 1].Temperature + MinimumGap;
        var maximum = insertionIndex == otherPoints.Length ? MaximumTemperature : otherPoints[insertionIndex].Temperature - MinimumGap;

        if (minimum > maximum)
        {
            throw new ArgumentException("No valid temperature slot is available.", nameof(temperature));
        }

        Points[index].Temperature = Math.Clamp(temperature, minimum, maximum);
        SortPoints();
    }

    public void Remove(Guid id)
    {
        if (Points.Count <= 2)
        {
            return;
        }

        var index = IndexOf(id);
        if (index < 0)
        {
            return;
        }

        var wasSelected = Points[index].IsSelected;
        Points.RemoveAt(index);
        if (wasSelected) Select(Points[Math.Min(index, Points.Count - 1)].Id);
    }

    public void Select(Guid id)
    {
        foreach (var point in Points)
        {
            point.IsSelected = point.Id == id;
        }
    }

    public ThermalProfile BuildProfile(double smoothingSeconds) =>
        ThermalProfile.Create(Points.OrderBy(point => point.Temperature).Select(ToThermalPoint), smoothingSeconds);

    public void ResetToDefault() => Load(ThermalProfile.Default);

    private void Load(ThermalProfile profile)
    {
        Points.Clear();
        foreach (var point in profile.Points)
        {
            Points.Add(new ThermalPointEditorViewModel(Guid.NewGuid(), point.Temperature, point.Color));
        }
    }

    private void EnsureAvailableSlot(double temperature, ThermalPointEditorViewModel? excludedPoint)
    {
        var insertionIndex = Points
            .Where(point => point != excludedPoint)
            .TakeWhile(point => point.Temperature < temperature)
            .Count();
        var lower = Points.Where(point => point != excludedPoint).ElementAtOrDefault(insertionIndex - 1);
        var upper = Points.Where(point => point != excludedPoint).ElementAtOrDefault(insertionIndex);

        if ((lower is not null && temperature - lower.Temperature < MinimumGap)
            || (upper is not null && upper.Temperature - temperature < MinimumGap))
        {
            throw new ArgumentException("Temperatures must be at least 1 °C apart.", nameof(temperature));
        }
    }

    private int IndexOf(Guid id)
    {
        for (var index = 0; index < Points.Count; index++)
        {
            if (Points[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private void SortPoints()
    {
        var sorted = Points.OrderBy(point => point.Temperature).ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            var currentIndex = Points.IndexOf(sorted[index]);
            if (currentIndex != index)
            {
                Points.Move(currentIndex, index);
            }
        }
    }

    private static ThermalPoint ToThermalPoint(ThermalPointEditorViewModel point) => new(point.Temperature, point.Color);

    private static void ValidateTemperature(double temperature)
    {
        if (!double.IsFinite(temperature) || temperature is < MinimumTemperature or > MaximumTemperature)
        {
            throw new ArgumentException("Temperatures must be between 0 and 120 °C.", nameof(temperature));
        }
    }
}
