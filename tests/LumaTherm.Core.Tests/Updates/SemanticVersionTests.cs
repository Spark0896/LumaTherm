using LumaTherm.Core.Updates;

namespace LumaTherm.Core.Tests.Updates;

public sealed class SemanticVersionTests
{
    [Fact]
    public void Parse_StableLiteralAndLeadingVProduceComparableVersions()
    {
        Assert.True(SemanticVersion.Parse("1.2.0") > SemanticVersion.Parse("1.1.9"));
        Assert.Equal(new SemanticVersion(1, 1, 0), SemanticVersion.Parse("v1.1.0"));
        Assert.True(SemanticVersion.Parse("1.1.9") < SemanticVersion.Parse("1.2.0"));
        Assert.True(SemanticVersion.Parse("2.0.0") >= SemanticVersion.Parse("2.0.0"));
        Assert.True(SemanticVersion.Parse("1.0.0") <= SemanticVersion.Parse("1.0.1"));
        Assert.Equal("1.1.0", SemanticVersion.Parse("v1.1.0").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("-1.2.3")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.-3")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-alpha")]
    [InlineData("1.2.3+build")]
    [InlineData("vv1.2.3")]
    [InlineData("2147483648.0.0")]
    [InlineData("1. 2.3")]
    public void Parse_MalformedOrNonStableInputThrowsFormatException(string value)
    {
        Assert.Throws<FormatException>(() => SemanticVersion.Parse(value));
    }

    [Fact]
    public void Parse_NullInputThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SemanticVersion.Parse(null!));
    }
}
