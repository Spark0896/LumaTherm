using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Tests.Colors;

public sealed class ColorEngineTests
{
    private static readonly ThermalProfile Profile = ThermalProfile.Default;

    [Theory]
    [InlineData(0, 0x50, 0xC8, 0xFF)]
    [InlineData(35, 0x50, 0xC8, 0xFF)]
    [InlineData(65, 0xFF, 0xC6, 0x4A)]
    [InlineData(85, 0xFF, 0x56, 0x5D)]
    [InlineData(110, 0xFF, 0x56, 0x5D)]
    public void Map_ClampsAndHitsControlPoints(double temperature, byte r, byte g, byte b)
    {
        var engine = new ColorEngine(Profile, 35);

        Assert.Equal(new RgbColor(r, g, b), engine.Map(temperature));
    }

    [Fact]
    public void Step_IsIndependentOfTickSize()
    {
        var oneStep = new ColorEngine(Profile, 35);
        var eightSteps = new ColorEngine(Profile, 35);
        var expected = new RgbColor(255, 188, 75);

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
