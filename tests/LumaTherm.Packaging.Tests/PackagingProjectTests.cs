using System.Xml.Linq;
using System.Diagnostics;

namespace LumaTherm.Packaging.Tests;

public sealed class PackagingProjectTests
{
    [Fact]
    public void ProjectPinsPrivateSdkBuildToolsAndExposesPath()
    {
        var path = Path.Combine(RepositoryLayout.Root, "packaging", "LumaTherm.Packaging.csproj");
        var document = XDocument.Load(path);
        var propertyGroup = Assert.Single(document.Root!.Elements("PropertyGroup"));
        Assert.Equal("net8.0", propertyGroup.Element("TargetFramework")?.Value);
        Assert.Equal("false", propertyGroup.Element("IsPackable")?.Value);
        var reference = Assert.Single(document.Descendants("PackageReference"), e => (string?)e.Attribute("Include") == "Microsoft.Windows.SDK.BuildTools");
        Assert.Equal("10.0.26100.8249", (string?)reference.Attribute("VersionOverride"));
        Assert.Equal("all", (string?)reference.Attribute("PrivateAssets"));
        Assert.Equal("true", (string?)reference.Attribute("GeneratePathProperty"));
    }

    [Fact]
    public void PackagingProjectsAreIncludedInSolution()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "LumaTherm.sln"));
        Assert.Contains("packaging\\LumaTherm.Packaging.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("tests\\LumaTherm.Packaging.Tests\\LumaTherm.Packaging.Tests.csproj", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagingTestAssemblyUsesReleaseVersionMetadata()
    {
        var assembly = typeof(PackagingProjectTests).Assembly;
        var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location);

        Assert.Equal("1.2.0", fileVersion.ProductVersion);
        Assert.Equal("1.2.0.0", fileVersion.FileVersion);
    }

}
