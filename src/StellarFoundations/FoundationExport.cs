using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StellarFoundations;

internal static class FoundationExport
{
    // Export a complete saved snapshot. Live entities and the source save are never stripped.
    internal static Dictionary<string, byte[]> Transform(byte[] worldBytes, JObject sourceMetadata)
    {
        if (sourceMetadata["foundationsSnapshot"]?.Value<string>("worldSha256") != AtomicSaveBundle.Hash(worldBytes))
            throw new InvalidDataException("Export requires a matching world and foundation snapshot.");
        var world = JObject.Parse(Encoding.UTF8.GetString(worldBytes).TrimStart('\uFEFF'));
        var metadata = (JObject)sourceMetadata.DeepClone();
        foreach (var ship in world["ships"] as JArray ?? throw new InvalidDataException("Saved ship list is missing."))
        {
            if (ship["statefulFloatingParts"] is not JArray parts) continue;
            var piers = parts.Where(part => part.Value<ushort>("settingsId") == Policy.PartSettingsId).ToArray();
            if (piers.Length == 0) continue;
            if (piers.Length == parts.Count && (ship["hullParts"] as JArray)?.Count is not > 0)
                throw new InvalidDataException("Build an ordinary floor on each empty foundation, or recycle that foundation, before exporting.");
            foreach (var pier in piers)
            {
                (metadata["parts"] as JObject)?.Remove(ship.Value<uint>("id") + ":" + pier.Value<ushort>("id"));
                pier.Remove();
            }
        }
        (metadata["world"] as JObject)?.Remove(FoundationRegistry.MetadataKey);
        metadata.Remove("foundationsSnapshot");
        RemoveStamp(metadata["packs"] as JArray, "silenthacks.foundations@");
        RemoveStamp(metadata["mods"] as JArray, "foundations@");
        return new Dictionary<string, byte[]> {
            ["world.json"] = new UTF8Encoding(false).GetBytes(world.ToString(Formatting.None)),
            ["sdmodkit-content.json"] = new UTF8Encoding(false).GetBytes(metadata.ToString(Formatting.None))
        };
    }
    private static void RemoveStamp(JArray? entries, string prefix)
    {
        if (entries == null) return;
        foreach (var entry in entries.Where(value => value.Type == JTokenType.String && value.Value<string>()!.StartsWith(prefix, StringComparison.Ordinal)).ToArray()) entry.Remove();
    }
    internal static string WriteCopy(string sourceDirectory, byte[] worldBytes, JObject metadata)
    {
        var files = Transform(worldBytes, metadata);
        var source = new DirectoryInfo(Path.GetFullPath(sourceDirectory));
        if (source.Parent == null || (source.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe export directory.");
        var name = source.Name + "-without-foundations-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var target = Path.Combine(source.Parent.FullName, name);
        if (Directory.Exists(target)) throw new IOException("Export directory already exists.");
        Directory.CreateDirectory(target);
        AtomicSaveBundle.Commit(target, files);
        return target;
    }
}
