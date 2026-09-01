using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Tests.Colors;

public sealed class ColorEngineTests
{
    private static readonly ThermalProfile Profile = ThermalProfile.Default;

    [Theory]
    [InlineData(0, 0x00, 0x6B, 0xFF)]
    [InlineData(35, 0x00, 0x6B, 0xFF)]
    [InlineData(65, 0xD0, 0x00, 0xFF)]
    [InlineData(85, 0xFF, 0x18, 0x00)]
    [InlineData(110, 0xFF, 0x18, 0x00)]
    public void Map_ClampsAndHitsControlPoints(double temperature, byte r, byte g, byte b)
    {
        var engine = new ColorEngine(Profile, 35);

        Assert.Equal(new RgbColor(r, g, b), engine.Map(temperature));
    }

    [Theory]
    [InlineData(35)]
    [InlineData(45)]
    [InlineData(55)]
    [InlineData(65)]
    [InlineData(75)]
    [InlineData(85)]
    public void Map_DefaultProfileKeepsLedColorsFullyBrightAndSaturated(double temperature)
    {
        var color = new ColorEngine(Profile, 35).Map(temperature);
        var channels = new[] { color.R, color.G, color.B };

        Assert.Equal(255, channels.Max());
        Assert.Equal(0, channels.Min());
    }

    [Fact]
    public void Map_InterpolatesWithinTheContainingPairOfAnUnlimitedPointProfile()
    {
        var profile = ThermalProfile.Create(
            [new(20, new(0, 0, 255)), new(40, new(0, 255, 255)),
             new(60, new(0, 255, 0)), new(80, new(255, 0, 0))], 0.8);
        var engine = new ColorEngine(profile, 20);

        Assert.Equal(new RgbColor(0, 255, 0), engine.Map(60));
        Assert.Equal(new RgbColor(255, 255, 0), engine.Map(70));
    }

    [Fact]
    public void Create_RequiresAtLeastTwoPoints()
    {
        Assert.Throws<ArgumentException>(() => ThermalProfile.Create([new(20, new(0, 0, 255))], 0.8));
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(40, 20)]
    public void Create_RejectsDuplicateOrCrossedTemperatures(double first, double second)
    {
        Assert.Throws<ArgumentException>(() => ThermalProfile.Create(
            [new(first, new(0, 0, 255)), new(second, new(255, 0, 0))], 0.8));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Create_RejectsNonFiniteTemperatures(double temperature)
    {
        Assert.Throws<ArgumentException>(() => ThermalProfile.Create(
            [new(20, new(0, 0, 255)), new(temperature, new(255, 0, 0))], 0.8));
    }

    [Theory]
    [InlineData(20, 20.9)]
    [InlineData(-0.1, 20)]
    [InlineData(20, 120.1)]
    public void Create_EnforcesPointSpacingAndSupportedTemperatureRange(double first, double second)
    {
        Assert.Throws<ArgumentException>(() => ThermalProfile.Create(
            [new(first, new(0, 0, 255)), new(second, new(255, 0, 0))], 0.8));
    }

    [Fact]
    public void Create_OwnsAnImmutableSnapshotOfPoints()
    {
        var points = new[] { new ThermalPoint(20, new RgbColor(0, 0, 255)), new(40, new(255, 0, 0)) };
        var profile = ThermalProfile.Create(points, 0.8);
        points[0] = new ThermalPoint(20, new RgbColor(255, 255, 255));

        Assert.Equal(new ThermalPoint(20, new RgbColor(0, 0, 255)), profile.Points[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<ThermalPoint>)profile.Points)[0] = new(20, new(255, 255, 255)));
    }

    [Fact]
    public void ContentEquals_UsesEveryPointAndTheSmoothingValue()
    {
        var first = ThermalProfile.Create([new(20, new(0, 0, 255)), new(40, new(255, 0, 0))], 0.8);
        var sameContent = ThermalProfile.Create([new(20, new(0, 0, 255)), new(40, new(255, 0, 0))], 0.8);
        var differentSmoothing = ThermalProfile.Create([new(20, new(0, 0, 255)), new(40, new(255, 0, 0))], 1.0);
        var differentPoint = ThermalProfile.Create([new(20, new(0, 0, 255)), new(40, new(255, 255, 0))], 0.8);

        Assert.True(first.ContentEquals(sameContent));
        Assert.Equal(first, sameContent);
        Assert.False(first.ContentEquals(differentSmoothing));
        Assert.False(first.ContentEquals(differentPoint));
    }

    [Fact]
    public void Default_ContainsTheThreeHardwareValidatedSaturatedPoints()
    {
        Assert.Equal(
            [new ThermalPoint(35, new RgbColor(0x00, 0x6B, 0xFF)), new(65, new(0xD0, 0x00, 0xFF)), new(85, new(0xFF, 0x18, 0x00))],
            ThermalProfile.Default.Points);
    }

    [Fact]
    public void Step_IsIndependentOfTickSize()
    {
        var oneStep = new ColorEngine(Profile, 35);
        var eightSteps = new ColorEngine(Profile, 35);
        var expected = new RgbColor(234, 0, 255);

        var oneStepActual = oneStep.Step(85, TimeSpan.FromSeconds(0.8));
        RgbColor eightStepsActual = default;
        for (var i = 0; i < 8; i++)
        {
            eightStepsActual = eightSteps.Step(85, TimeSpan.FromSeconds(0.1));
        }

        Assert.Equal(expected, oneStepActual);
        Assert.Equal(expected, eightStepsActual);
        Assert.Equal(oneStepActual, eightStepsActual);
    }

    [Fact]
    public void Validate_RejectsCrossedTemperatures()
    {
        var invalid = Profile with { ColdTemperature = 65, WarmTemperature = 65 };

        var error = Assert.Throws<ArgumentException>(() => invalid.Validate());

        Assert.Contains("ColdTemperature < WarmTemperature < HotTemperature", error.Message);
    }

    [Fact]
    public void Validate_RequiresOneDegreeBetweenControlPoints()
    {
        var invalid = Profile with { ColdTemperature = 64.5, WarmTemperature = 65 };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Theory]
    [InlineData("#50C8FF", 0x50, 0xC8, 0xFF)]
    [InlineData("50c8ff", 0x50, 0xC8, 0xFF)]
    public void Hex_RoundTrips(string text, byte r, byte g, byte b)
    {
        Assert.True(RgbColor.TryParseHex(text, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
        Assert.Equal("#50C8FF", color.ToHex());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#50C8F")]
    [InlineData("#50C8FF0")]
    [InlineData("#50C8FG")]
    [InlineData("##50C8FF")]
    public void TryParseHex_RejectsValuesOtherThanSixOptionalPrefixedHexDigits(string? text)
    {
        Assert.False(RgbColor.TryParseHex(text, out _));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(120.1)]
    public void Validate_RejectsTemperaturesOutsideSupportedRange(double coldTemperature)
    {
        var invalid = Profile with { ColdTemperature = coldTemperature };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Fact]
    public void Validate_RejectsNaNColdTemperature()
    {
        var invalid = Profile with { ColdTemperature = double.NaN };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Fact]
    public void Validate_RejectsNaNWarmTemperature()
    {
        var invalid = Profile with { WarmTemperature = double.NaN };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Fact]
    public void Validate_RejectsNaNHotTemperature()
    {
        var invalid = Profile with { HotTemperature = double.NaN };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(5.01)]
    public void Validate_RejectsSmoothingOutsideSupportedRange(double smoothingSeconds)
    {
        var invalid = Profile with { SmoothingSeconds = smoothingSeconds };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Fact]
    public void Validate_RejectsNaNSmoothingSeconds()
    {
        var invalid = Profile with { SmoothingSeconds = double.NaN };

        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Fact]
    public void Map_UsesShortestHueArcAcrossZeroDegrees()
    {
        var profile = new ThermalProfile(0, new(255, 0, 43), 100, new(255, 43, 0), 120, new(255, 0, 0), 0.8);

        var middle = new ColorEngine(profile, 0).Map(50);

        Assert.Equal(new RgbColor(255, 0, 0), middle);
    }

    [Fact]
    public void Step_UsesExponentialSmoothingOverElapsedTime()
    {
        var grayscaleProfile = new ThermalProfile(0, new(0, 0, 0), 100, new(255, 255, 255), 120, new(255, 255, 255), 1);
        var engine = new ColorEngine(grayscaleProfile, 0);

        var color = engine.Step(100, TimeSpan.FromSeconds(1));

        Assert.Equal(new RgbColor(161, 161, 161), color);
    }

    [Fact]
    public void UpdateProfile_PreservesTheSmoothedTemperature()
    {
        var engine = new ColorEngine(Profile, 35);
        engine.Step(85, TimeSpan.FromMilliseconds(100));
        var slowerProfile = Profile with { SmoothingSeconds = 2 };

        engine.UpdateProfile(slowerProfile);
        var next = engine.Step(85, TimeSpan.FromMilliseconds(100));

        Assert.Equal(slowerProfile, engine.Profile);
        Assert.NotEqual(new RgbColor(0xFF, 0x56, 0x5D), next);
    }

    [Fact]
    public void UpdateProfile_RejectsInvalidProfilesWithoutReplacingTheActiveProfile()
    {
        var engine = new ColorEngine(Profile, 35);

        Assert.Throws<ArgumentException>(() => engine.UpdateProfile(Profile with { SmoothingSeconds = 0 }));
        Assert.Equal(Profile, engine.Profile);
    }

    [Theory]
    [InlineData(-10, ThermalRange.Cold)]
    [InlineData(35, ThermalRange.Cold)]
    [InlineData(35.0001, ThermalRange.Warm)]
    [InlineData(84.9999, ThermalRange.Warm)]
    [InlineData(85, ThermalRange.Hot)]
    [InlineData(120, ThermalRange.Hot)]
    public void Classify_UsesInclusiveOuterControlPointBoundaries(double temperature, ThermalRange expected)
    {
        var engine = new ColorEngine(Profile, 35);

        Assert.Equal(expected, engine.Classify(temperature));
    }
}
