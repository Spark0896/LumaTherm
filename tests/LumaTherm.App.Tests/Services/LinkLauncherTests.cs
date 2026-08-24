using System.Diagnostics;
using LumaTherm.App.Services;

namespace LumaTherm.App.Tests.Services;

public sealed class LinkLauncherTests
{
    [Theory]
    [InlineData("https://github.com/Spark0896/LumaTherm")]
    [InlineData("https://github.com/Spark0896/LumaTherm/")]
    [InlineData("https://github.com/Spark0896/LumaTherm/releases/tag/v1.2.0")]
    [InlineData("https://github.com/Spark0896/LumaTherm/releases/download/v1.2.0/LumaTherm.exe")]
    public void Open_AllowsOnlyNormalizedRepositoryPaths(string value)
    {
        ProcessStartInfo? launched = null;
        var launcher = new LinkLauncher(info => launched = info);

        launcher.Open(new Uri(value));

        Assert.NotNull(launched);
        Assert.Equal(value.EndsWith("/LumaTherm", StringComparison.Ordinal) ? value + "/" : value, launched.FileName);
        Assert.True(launched.UseShellExecute);
    }

    [Theory]
    [InlineData("http://github.com/Spark0896/LumaTherm/")]
    [InlineData("//github.com/Spark0896/LumaTherm/")]
    [InlineData("https://evil.example/Spark0896/LumaTherm/")]
    [InlineData("https://github.com.evil.example/Spark0896/LumaTherm/")]
    [InlineData("https://sub.github.com/Spark0896/LumaTherm/")]
    [InlineData("https://user:password@github.com/Spark0896/LumaTherm/")]
    [InlineData("https://github.com/Spark0896/LumaThermal/")]
    [InlineData("https://github.com/Spark0896/LumaThermEvil/")]
    [InlineData("https://github.com/Spark0896/LumaTherm/../Other/")]
    [InlineData("https://github.com/Spark0896/LumaTherm/%2e%2e/Other/")]
    [InlineData("https://github.com/Spark0896/LumaTherm/%2F%2Fevil.example/")]
    [InlineData("https://github.com/Spark0896%2fLumaTherm/releases/")]
    public void Open_RejectsLookalikeCredentialsTraversalAndEncodedPathsBeforeLaunching(string value)
    {
        var launches = 0;
        var launcher = new LinkLauncher(_ => launches++);

        Assert.Throws<ArgumentException>(() => launcher.Open(new Uri(value, UriKind.RelativeOrAbsolute)));
        Assert.Equal(0, launches);
    }
}
