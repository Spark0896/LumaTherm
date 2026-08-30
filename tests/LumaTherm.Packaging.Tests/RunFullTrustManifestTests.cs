using System.Xml.Linq;

namespace LumaTherm.Packaging.Tests;

public sealed class RunFullTrustManifestTests
{
    [Fact]
    public void SparseManifestDeclaresRunFullTrustForItsExecutableApplication()
    {
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
        var manifest = XDocument.Load(Path.Combine(
            RepositoryLayout.Root, "packaging", "sparse", "AppxManifest.xml"));

        var capabilities = Assert.Single(manifest.Root!.Elements(foundation + "Capabilities"));
        var runFullTrust = Assert.Single(capabilities.Elements(rescap + "Capability"));
        Assert.Equal("runFullTrust", (string?)runFullTrust.Attribute("Name"));
    }
}
