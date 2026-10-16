using System;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StellarFoundations;

public sealed class ReleaseContractTests
{
    private static string Repository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "StellarFoundations.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root is missing.");
    }

    [Fact]
    public void ManagerLoaderContentAndShippingAssemblyAgreeOnReleaseIdentity()
    {
        var root = Repository();
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, "mod.json")));
        var content = JObject.Parse(PartRegistration.Definition);
        var project = XDocument.Load(Path.Combine(root, "src/StellarFoundations/StellarFoundations.csproj"));
        Assert.Equal("stellarfoundations", manifest.Value<string>("id"));
        Assert.Equal("StellarFoundations", manifest.Value<string>("name"));
        Assert.Equal(Plugin.DisplayName, content.Value<string>("name"));
        Assert.Equal(Plugin.Version, manifest.Value<string>("version"));
        Assert.Equal(Plugin.Version, content.Value<string>("version"));
        Assert.Equal(Plugin.Version, project.Root!.Element("PropertyGroup")!.Element("Version")!.Value);
        Assert.Equal(Plugin.Version, AssemblyName.GetAssemblyName(Path.Combine(root,
            "src/StellarFoundations/bin/Release/net472/StellarFoundations.dll")).Version!.ToString(3));
        Assert.Equal(Policy.PartSettingsId, content["parts"]![0]!.Value<ushort>("id"));
        Assert.Equal(PartRegistration.Asset, content["parts"]![0]!.Value<string>("asset"));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Value<string>("description")));
    }

    [Fact]
    public void ManagerThumbnailResolvesInsideRepositoryToAPng()
    {
        var root = Repository();
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, "mod.json")));
        var path = manifest.Value<string>("thumbnail")!;
        Assert.False(Path.IsPathRooted(path));
        var absolute = Path.GetFullPath(Path.Combine(root, path));
        Assert.StartsWith(root + Path.DirectorySeparatorChar, absolute);
        using var file = File.OpenRead(absolute);
        var header = new byte[8]; Assert.Equal(8, file.Read(header));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, header);
    }
}
