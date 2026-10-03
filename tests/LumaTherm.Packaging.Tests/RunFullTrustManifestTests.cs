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
        Assert.Equal(new[] { "runFullTrust", "unvirtualizedResources" },
            capabilities.Elements(rescap + "Capability").Select(capability => (string?)capability.Attribute("Name")).ToArray());
    }
}
