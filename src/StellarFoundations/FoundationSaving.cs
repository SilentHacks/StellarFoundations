using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Saving.Interface.Model;
using Saving.Services;
using Saving.Services.IO;
using Saving.Services.Model;
using Saving.Serialization.V15;
using Saving.Settings;
using SDModKit;
using SDModKit.Content;
using UnityEngine;

namespace StellarFoundations;

internal static class FoundationSaving
{
    private sealed class Job
    {
        internal string Directory = "", SaveName = "";
        internal SerializableGameWorld World = null!;
        internal Encoding Encoding = Encoding.UTF8;
        internal JObject? Metadata;
        internal GameSaver? Saver;
        internal Task<Exception?>? Task;
        internal Exception? Failure;
        internal bool Captured, Shown;
        internal bool Export;
        internal string? ExportPath;
        internal string? ExportError;
        internal bool Completed => Failure != null || (Task?.IsCompleted ?? false);
        internal Exception? Error => Failure ?? (Task?.IsCompleted == true ? Task.GetAwaiter().GetResult() : null);
    }
    private static Job? _job;
    private static GameSaver? _queuedSaver;
    private static SaveGameParams? _queued;
    private static bool _exportRequested;
    private static GameSaver? _nativeSaver;
    private static FieldInfo _worldValues = null!, _parameters = null!;
    private static MethodInfo _importWorld = null!;
    internal static string Status = "No foundation save pending.";
    internal static bool Busy
    {
        get
        {
            if (_job != null) return true;
            if (_nativeSaver == null) _nativeSaver = UnityEngine.Object.FindFirstObjectByType<GameSaver>();
            return _nativeSaver != null && _nativeSaver.IsCurrentlySaving();
        }
    }

    internal static void Install(HarmonyRuntime harmony)
    {
        foreach (var method in new[] {
            Compatibility.Method(typeof(GameSaver), "SaveGame", new[] { typeof(string) }),
            Compatibility.Method(typeof(GameSaver), "SaveGame", new[] { typeof(SaveGameParams) }),
            Compatibility.Method(typeof(SaveFileWriter), "WriteGameFile"),
            Compatibility.Method(typeof(ContentSave), "AfterSaveGame"),
            Compatibility.Method(typeof(GameLoader), "LoadState")
        }) Compatibility.SaveHook(method);
        InstallWorkshopAdapter(harmony);
        _worldValues = Compatibility.Field(typeof(WorldData), "Values");
        _importWorld = Compatibility.Method(typeof(WorldData), "A", new[] { typeof(JObject) });
        _parameters = Compatibility.Field(typeof(GameSaver), "_currentSavingParams");
        var begin = new HarmonyMethod(typeof(FoundationSaving), nameof(Begin)) { priority = Priority.First };
        harmony.Patch(Compatibility.Method(typeof(GameSaver), "SaveGame", new[] { typeof(string) }), prefix: begin,
            finalizer: new HarmonyMethod(typeof(FoundationSaving), nameof(Finish)) { priority = Priority.Last });
        harmony.Patch(Compatibility.Method(typeof(GameSaver), "SaveGame", new[] { typeof(SaveGameParams) }), prefix: new HarmonyMethod(typeof(FoundationSaving), nameof(Queue)) { priority = Priority.First });
        harmony.Patch(Compatibility.Method(typeof(SaveFileWriter), "WriteGameFile"), prefix: new HarmonyMethod(typeof(FoundationSaving), nameof(Write)) { priority = Priority.First });
        harmony.Patch(Compatibility.Method(typeof(ContentSave), "AfterSaveGame"), prefix: new HarmonyMethod(typeof(FoundationSaving), nameof(Metadata)) { priority = Priority.First });
        harmony.Patch(Compatibility.Method(typeof(GameSaver), "SaveScreenshot"), postfix: new HarmonyMethod(typeof(FoundationSaving), nameof(Wait)));
        harmony.Patch(Compatibility.Method(typeof(GameSaver), "IsCurrentlySaving"), postfix: new HarmonyMethod(typeof(FoundationSaving), nameof(IsSaving)));
        harmony.Patch(Compatibility.Method(typeof(GameLoader), "LoadState"), prefix: new HarmonyMethod(typeof(FoundationSaving), nameof(Recover)) { priority = Priority.First });
        harmony.Patch(Compatibility.Method(typeof(GameLoader), "LoadState"), prefix: new HarmonyMethod(typeof(FoundationSaving), nameof(ValidateLoad)) { priority = Priority.Last, after = new[] { "sdmodkit.contentsave" } });
        harmony.Patch(Compatibility.Method(typeof(ContentSave), "BeforeLoadState"), prefix: new HarmonyMethod(typeof(FoundationSaving), nameof(LoadBackupMetadata)) { priority = Priority.First });
        harmony.Patch(Compatibility.Method(typeof(GameSaver), "BackupCurrentWorld"), postfix: new HarmonyMethod(typeof(FoundationSaving), nameof(PairNativeBackup)));
        harmony.Patch(Compatibility.Method(typeof(GameSaver), "DestroyOldBackups"), postfix: new HarmonyMethod(typeof(FoundationSaving), nameof(PruneNativeBackups)));
    }
    private static void InstallWorkshopAdapter(HarmonyRuntime harmony)
    {
        var writer = Compatibility.Method(typeof(SaveFileWriter), "WriteGameFile");
        if (HarmonyRuntime.GetPatchInfo(writer)?.Owners.Contains("stellarworkshop.saving") != true) return;
        var assembly = Assembly.Load("StellarWorkshop");
        if (assembly.ManifestModule.ModuleVersionId != new Guid("40054375-01b2-4786-8f92-7675b17a25d2")) throw new NotSupportedException("Unqualified Workshop save integration.");
        var capture = Compatibility.Method(assembly.GetType("StellarWorkshop.SafeSaving", true)!, "BeforeWrite");
        Compatibility.Exclusive(capture);
        harmony.Patch(capture, prefix: new HarmonyMethod(typeof(FoundationSaving), nameof(BeforeWorkshopCapture)));
    }
    private static bool BeforeWorkshopCapture(ref bool __result)
    {
        if (!FoundationRegistry.Initialized) return true;
        // The Foundations transaction includes all SDModKit metadata. Prevent a second
        // wrapper from opening an independent capture while it owns this world's save.
        __result = true; return false;
    }
    private static void Begin(string worldDirectoryFullName)
    {
        if (!FoundationRegistry.Initialized) return;
        if (_job != null) throw new InvalidOperationException("Foundation save already pending.");
        if (Plugin.Failure.Length != 0) throw new InvalidOperationException("Resolve the Foundations error before saving this world.");
        FoundationRegistry.Persist();
        _job = new Job { Directory = Path.GetFullPath(worldDirectoryFullName), Export = _exportRequested };
        _exportRequested = false;
        Status = "Capturing world and foundation attachment data...";
    }
    private static bool Write(SerializableGameWorld gameWorld, string directoryFullName, string saveName, SaveSettings ____saveSettings, ref GameFileInfo __result)
    {
        if (_job == null) return true;
        if (_job.Directory != Path.GetFullPath(directoryFullName)) throw new IOException("Save target changed during foundation capture.");
        _job.World = gameWorld; _job.SaveName = saveName; _job.Encoding = ____saveSettings.encoding;
        __result = new GameFileInfo(Path.Combine(directoryFullName, saveName + ".json"), Path.Combine(directoryFullName, saveName + ".jpg")); return false;
    }
    private static bool Metadata(string worldDirectoryFullName)
    {
        if (_job == null) return true;
        if (_job.Directory != Path.GetFullPath(worldDirectoryFullName)) throw new IOException("Metadata save target changed.");
        var ships = SDModKit.Game.GameRefs.Service<Ships.Services.ShipsServerTracker>();
        PartData.Prune((ship, part) => ships != null && ships.GetShip(ship) is Ships.TrackedShipServer tracked && tracked.TryGetStatefulPart(part, out _));
        var values = (Dictionary<string, string>)_worldValues.GetValue(null)!;
        _job.Metadata = new JObject {
            ["format"] = 1, ["game"] = ModStamp.GameVersion,
            ["mods"] = new JArray(ModStamp.Current()),
            ["packs"] = new JArray(ContentRegistry.Packs.Select(pack => pack.Id + "@" + pack.Version)),
            ["parts"] = PartData.Export(), ["world"] = JObject.FromObject(values)
        };
        return false;
    }
    private static Exception? Finish(GameSaver __instance, Exception? __exception)
    {
        var job = _job;
        if (job == null || job.Captured) return __exception;
        job.Captured = true; job.Saver = __instance;
        job.Failure = __exception;
        if (job.World == null || job.Metadata == null) job.Failure = new IOException("Incomplete foundation save capture. Previous files were retained.", __exception);
        if (job.Failure == null)
        {
            var rotations = SaveRotationSnapshot.Capture(job.World!);
            job.Task = Task.Run<Exception?>(() => {
                try
                {
                    var settings = new JsonSerializerSettings { ContractResolver = new WritableOnlyContractResolver() };
                    settings.Converters.Add(rotations);
                    var bytes = job.Encoding.GetBytes(JsonConvert.SerializeObject(job.World, Formatting.None, settings));
                    job.Metadata!["foundationsSnapshot"] = new JObject { ["format"] = 1, ["worldSha256"] = AtomicSaveBundle.Hash(bytes) };
                    AtomicSaveBundle.Commit(job.Directory, new Dictionary<string, byte[]> {
                        [job.SaveName + ".json"] = bytes,
                        ["sdmodkit-content.json"] = new UTF8Encoding(false).GetBytes(job.Metadata.ToString(Formatting.None))
                    });
                    if (job.Export)
                    {
                        try { job.ExportPath = FoundationExport.WriteCopy(job.Directory, bytes, job.Metadata); }
                        catch (Exception exportError) { job.ExportError = exportError.Message; }
                    }
                    return null;
                }
                catch (Exception error) { return error; }
            });
        }
        return __exception;
    }
    private static bool Queue(GameSaver __instance, SaveGameParams saveGameParams)
    {
        if (_job == null) return true;
        if (!_queued.HasValue || !saveGameParams.IsAutomatic) { _queued = saveGameParams; _queuedSaver = __instance; }
        return false;
    }
    private static void IsSaving(GameSaver __instance, ref bool __result) { if (ReferenceEquals(_job?.Saver, __instance)) __result = true; }
    private static void Wait(GameSaver __instance, ref IEnumerator __result)
    {
        if (_job == null) return;
        _job.Saver = __instance; __result = Await(_job, __result);
    }
    private static IEnumerator Await(Job job, IEnumerator screenshot)
    {
        while (!job.Completed) yield return null;
        if (job.Error != null) { ShowFailure(job); yield break; }
        yield return screenshot;
    }
    internal static void Update()
    {
        var job = _job;
        if (job == null || !job.Completed || job.Saver == null) return;
        if (job.Error != null) { ShowFailure(job); return; }
        if (_parameters.GetValue(job.Saver)! != null) return;
        _job = null;
        Status = job.ExportError != null ? "World saved; portable copy failed: " + job.ExportError
            : job.ExportPath == null ? "World and foundation data saved successfully." : "Portable copy created: " + job.ExportPath;
        var saver = _queuedSaver; var queued = _queued; _queued = null; _queuedSaver = null;
        if (saver != null && queued.HasValue) saver.SaveGame(queued.Value);
    }
    private static void ShowFailure(Job job)
    {
        if (job.Shown) return; job.Shown = true;
        Status = "Save failed. Previous complete save or recovery copies retained.";
        MelonLoader.MelonLogger.Error(Plugin.DisplayName + ": " + job.Error);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    internal static void Draw()
    {
        var job = _job; if (job == null || !job.Shown) return;
        GUILayout.BeginArea(new Rect(20, 120, 720, 140), GUI.skin.box);
        GUILayout.Label(Status); GUILayout.Label("New progress is not saved. The world and its attachment metadata must recover together.");
        if (GUILayout.Button("Retry saving")) { var saver = job.Saver; _job = null; _queued = null; if (saver != null) { _parameters.SetValue(saver, null); saver.SaveGame(new SaveGameParams { IsAutomatic = false }); } }
        GUILayout.EndArea();
    }
    internal static void Drain() { if (_job?.Task != null) _job.Task.GetAwaiter().GetResult(); }
    internal static void RequestExport()
    {
        if (Busy || !SDModKit.Game.GameRefs.IsServer || !FoundationRegistry.Initialized || Plugin.Failure.Length != 0) return;
        var saver = UnityEngine.Object.FindFirstObjectByType<GameSaver>();
        if (saver == null) return;
        _exportRequested = true;
        try { saver.SaveGame(new SaveGameParams { IsAutomatic = false }); }
        finally { _exportRequested = false; }
    }
    internal static void WorldExit()
    {
        Drain();
        if (_job?.Error != null) ShowFailure(_job);
        _job = null; _queued = null; _queuedSaver = null;
        _exportRequested = false;
        _nativeSaver = null;
        Status = "No foundation save pending.";
    }
    private static void Recover(ISavedGameFile fileEntry)
    {
        try { if (fileEntry?.SaveFileInfo?.Directory != null) AtomicSaveBundle.Recover(fileEntry.SaveFileInfo.Directory.FullName); }
        catch (Exception error) { Plugin.LoadFault(error); throw; }
    }

    private static string MetadataPath(ISavedGameFile entry)
    {
        var directory = entry.SaveFileInfo.Directory!.FullName;
        if (!string.Equals(entry.SaveFileInfo.Name, "world.json", StringComparison.OrdinalIgnoreCase))
        {
            var paired = Path.Combine(directory, ".stellarfoundations-backups", entry.SaveFileInfo.Name);
            if (File.Exists(paired)) return paired;
        }
        return Path.Combine(directory, "sdmodkit-content.json");
    }
    private static bool LoadBackupMetadata(ISavedGameFile fileEntry)
    {
        if (fileEntry?.SaveFileInfo?.Directory == null || fileEntry.SaveFileInfo.Name == "world.json") return true;
        var path = MetadataPath(fileEntry);
        if (Path.GetFileName(Path.GetDirectoryName(path)) != ".stellarfoundations-backups") return true;
        var metadata = JObject.Parse(File.ReadAllText(path));
        PartData.Clear(); PartData.Import(metadata["parts"] as JObject);
        // The native import also marks these values as loaded, preserving them at WorldEnter.
        _importWorld.Invoke(null, new object?[] { metadata["world"] as JObject });
        return false;
    }
    private static void ValidateLoad(ISavedGameFile fileEntry)
    {
        try { ValidateLoadCore(fileEntry); }
        catch (Exception error) { Plugin.LoadFault(error); throw; }
    }
    private static void ValidateLoadCore(ISavedGameFile fileEntry)
    {
        if (fileEntry?.SaveFileInfo == null) return;
        var bytes = File.ReadAllBytes(fileEntry.SaveFileInfo.FullName);
        var world = JObject.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        var expected = new Dictionary<uint, HashSet<ushort>>();
        if (world["ships"] is JArray ships)
            foreach (var ship in ships)
                foreach (var part in ship["statefulFloatingParts"] as JArray ?? new JArray())
                    if (part.Value<ushort>("settingsId") == Policy.PartSettingsId)
                    {
                        var id = ship.Value<uint>("id");
                        if (!expected.TryGetValue(id, out var parts)) expected[id] = parts = new HashSet<ushort>();
                        if (!parts.Add(part.Value<ushort>("id"))) throw new InvalidDataException("Duplicate saved foundation part.");
                    }
        var encoded = WorldData.Get(FoundationRegistry.MetadataKey);
        if (fileEntry.SaveFileInfo.Name != "world.json" && expected.Count == 0 && Path.GetDirectoryName(MetadataPath(fileEntry)) == fileEntry.SaveFileInfo.Directory!.FullName)
        { encoded = null; WorldData.Set(FoundationRegistry.MetadataKey, null!); }
        if (encoded == null && expected.Count != 0) throw new InvalidDataException("Foundation metadata is missing. Restore the matching world and sdmodkit-content.json together.");
        FoundationRegistry.Restore(encoded);
        if (encoded == null) return;
        var metadata = JObject.Parse(File.ReadAllText(MetadataPath(fileEntry)));
        var hash = metadata["foundationsSnapshot"]?.Value<string>("worldSha256");
        if (hash != AtomicSaveBundle.Hash(bytes)) throw new InvalidDataException("World and foundation metadata belong to different saves. Loading was stopped before physics.");
        if (expected.Count != FoundationRegistry.Count) throw new InvalidDataException("Saved foundation structure count differs from its metadata.");
        foreach (var ground in FoundationRegistry.Records)
        {
            if (!expected.TryGetValue(ground.Ship, out var parts) || parts.Count != ground.Piers.Count || ground.Piers.Any(pier => !parts.Contains(pier.Part)))
                throw new InvalidDataException("Saved foundation identities differ from their metadata.");
        }
    }
    private static void PairNativeBackup(string directoryFullName)
    {
        if (!FoundationRegistry.Initialized) return;
        var source = Path.Combine(directoryFullName, "sdmodkit-content.json");
        if (!File.Exists(source)) return;
        var metadata = JObject.Parse(File.ReadAllText(source));
        var hash = metadata["foundationsSnapshot"]?.Value<string>("worldSha256");
        if (hash == null) return;
        var folder = Path.Combine(directoryFullName, ".stellarfoundations-backups"); Directory.CreateDirectory(folder);
        foreach (var backup in Directory.GetFiles(directoryFullName, "backup_*.json"))
        {
            var target = Path.Combine(folder, Path.GetFileName(backup));
            if (!File.Exists(target) && AtomicSaveBundle.Hash(File.ReadAllBytes(backup)) == hash) File.Copy(source, target);
        }
    }
    private static void PruneNativeBackups(string directoryFullName)
    {
        var folder = Path.Combine(directoryFullName, ".stellarfoundations-backups");
        if (!Directory.Exists(folder)) return;
        foreach (var metadata in Directory.GetFiles(folder, "backup_*.json")) if (!File.Exists(Path.Combine(directoryFullName, Path.GetFileName(metadata)))) File.Delete(metadata);
    }
}
