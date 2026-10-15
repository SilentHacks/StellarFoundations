using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StellarFoundations;

public sealed class ExportTests
{
    private static (byte[] World, JObject Metadata) Snapshot(bool floor = true)
    {
        var world = new JObject {
            ["ships"] = new JArray(new JObject {
                ["id"] = 42, ["position"] = new JObject { ["x"] = 1.23456789123 },
                ["hullParts"] = floor ? new JArray(new JObject { ["settingsId"] = 10 }) : new JArray(),
                ["statefulFloatingParts"] = new JArray(new JObject { ["id"] = 9, ["settingsId"] = 61240 }),
                ["storageStates"] = new JArray(new JObject { ["items"] = new JArray("unchanged inventory") }),
                ["rooms"] = new JArray(new JObject { ["oxygen"] = 5.25 })
            }), ["players"] = new JArray(new JObject { ["steamId"] = "kept" })
        };
        var bytes = Encoding.UTF8.GetBytes(world.ToString(Formatting.None));
        return (bytes, new JObject {
            ["world"] = new JObject { [FoundationRegistry.MetadataKey] = "attachment", ["another.mod"] = "kept" },
            ["parts"] = new JObject { ["42:9"] = new JObject { ["foundation"] = "removed" }, ["42:8"] = new JObject { ["wire"] = "kept" } },
            ["mods"] = new JArray("foundations@0.1.0", "workshop@0.2.0"),
            ["packs"] = new JArray("silenthacks.foundations@0.1.0", "other@1.0"),
            ["foundationsSnapshot"] = new JObject { ["worldSha256"] = AtomicSaveBundle.Hash(bytes) }
        });
    }

    [Fact] public void PortableCopyPreservesNativeWorldAndOtherModsWithoutMutatingSource()
    {
        var snapshot = Snapshot(); var metadataBefore = snapshot.Metadata.ToString(Formatting.None);
        var original = JObject.Parse(Encoding.UTF8.GetString(snapshot.World));
        var files = FoundationExport.Transform(snapshot.World, snapshot.Metadata);
        var exported = JObject.Parse(Encoding.UTF8.GetString(files["world.json"]));
        var metadata = JObject.Parse(Encoding.UTF8.GetString(files["sdmodkit-content.json"]));
        ((JArray)original["ships"]![0]!["statefulFloatingParts"]!).Clear();
        Assert.True(JToken.DeepEquals(original, exported));
        Assert.Equal(metadataBefore, snapshot.Metadata.ToString(Formatting.None));
        Assert.Null(metadata["world"]![FoundationRegistry.MetadataKey]);
        Assert.Equal("kept", metadata["world"]!.Value<string>("another.mod"));
        Assert.Null(metadata["parts"]!["42:9"]);
        Assert.Equal("kept", metadata["parts"]!["42:8"]!.Value<string>("wire"));
        Assert.Null(metadata["foundationsSnapshot"]);
        Assert.Equal(new JArray("workshop@0.2.0").ToString(), metadata["mods"]!.ToString());
    }

    [Fact] public void ExportRejectsUnpairedSaveBeforeRemovingAnyParts()
    {
        var snapshot = Snapshot(); snapshot.Metadata["foundationsSnapshot"]!["worldSha256"] = "wrong";
        Assert.Throws<InvalidDataException>(() => FoundationExport.Transform(snapshot.World, snapshot.Metadata));
    }

    [Fact] public void EmptyFoundationMustBeRecycledOrHaveAFloorBeforeExport()
    {
        var snapshot = Snapshot(floor: false);
        Assert.Throws<InvalidDataException>(() => FoundationExport.Transform(snapshot.World, snapshot.Metadata));
    }
}
