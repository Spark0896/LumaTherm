using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;

namespace LumaTherm.App.Tests.ViewModels;

public sealed class ThermalProfileEditorViewModelTests
{
    [Fact]
    public void AddMoveAndRemove_PreservesGradientAndEnforcesNeighborSpacing()
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);

        var added = editor.AddAt(50);

        Assert.Equal(new ColorEngine(ThermalProfile.Default, 50).Map(50), added.Color);
        Assert.Equal([35d, 50d, 65d, 85d], editor.Points.Select(point => point.Temperature));

        editor.Move(added.Id, 64.8);

        Assert.Equal(64, added.Temperature);

        editor.Remove(added.Id);

        Assert.Equal(3, editor.Points.Count);
    }

    [Fact]
    public void AddAt_HandlesEveryAvailableTemperatureSlotWithoutArtificialPointCap()
    {
        var editor = new ThermalProfileEditorViewModel(Profile((0, new RgbColor(0, 0, 255)), (120, new RgbColor(255, 0, 0))));

        for (var temperature = 2; temperature < 120; temperature += 2)
        {
            editor.AddAt(temperature);
        }

        Assert.Equal(61, editor.Points.Count);
        Assert.Equal(Enumerable.Range(0, 61).Select(index => index * 2d), editor.Points.Select(point => point.Temperature));
    }

    [Fact]
    public void Remove_WhenOnlyTwoPointsRemain_KeepsTheMinimumValidProfile()
    {
        var editor = new ThermalProfileEditorViewModel(Profile((0, new RgbColor(0, 0, 255)), (120, new RgbColor(255, 0, 0))));

        editor.Remove(editor.Points[0].Id);

        Assert.Equal(2, editor.Points.Count);
        Assert.Equal([0d, 120d], editor.Points.Select(point => point.Temperature));
    }

    [Fact]
    public void SelectAndMove_KeepTheSamePointSelectedAfterCollectionOrderChanges()
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var selected = editor.AddAt(50);
        var originalId = selected.Id;

        editor.Select(originalId);
        editor.Move(originalId, 36);

        Assert.Same(selected, editor.Points.Single(point => point.Id == originalId));
        Assert.True(selected.IsSelected);
        Assert.Equal([35d, 36d, 65d, 85d], editor.Points.Select(point => point.Temperature));
    }

    [Fact]
    public void BuildProfile_UsesEditedPointsAndValidatesSmoothing()
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var added = editor.AddAt(50);
        added.Color = new RgbColor(1, 2, 3);

        var profile = editor.BuildProfile(1.2);

        Assert.Equal(1.2, profile.SmoothingSeconds);
        Assert.Equal(new ThermalPoint(50, new RgbColor(1, 2, 3)), profile.Points[1]);
        Assert.Throws<ArgumentException>(() => editor.BuildProfile(0));
    }

    [Fact]
    public void ResetToDefault_ReplacesDraftPointsAndClearsSelection()
    {
        var editor = new ThermalProfileEditorViewModel(Profile((0, new RgbColor(0, 0, 0)), (120, new RgbColor(255, 255, 255))));
        editor.Select(editor.Points[0].Id);

        editor.ResetToDefault();

        Assert.Equal(ThermalProfile.Default.Points, editor.Points.Select(point => new ThermalPoint(point.Temperature, point.Color)));
        Assert.All(editor.Points, point => Assert.False(point.IsSelected));
    }

    private static ThermalProfile Profile(params (double Temperature, RgbColor Color)[] points) =>
        ThermalProfile.Create(points.Select(point => new ThermalPoint(point.Temperature, point.Color)), 0.8);
}
