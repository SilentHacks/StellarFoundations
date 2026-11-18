using System;
using System.Reflection;
using HarmonyLib;

namespace StellarFoundations;

internal static class SaveCompatibility
{
    internal enum Target { SaveWorld, SaveRequest, WriteWorld, SaveMetadata, LoadWorld }

    private static readonly (string Mod, Guid Module, string Type, string Save, string? Load, Target SaveTarget)[] Observers =
    {
        ("Custom Seat", new Guid("975042ef-976a-4f51-b398-11c29df2285f"), "KeySeat.SeatConfigs", "AfterSave", "AfterLoad", Target.SaveWorld),
        ("Arsenal", new Guid("349cf038-28ef-43da-b2bb-8f8852d8ef08"), "Arsenal.Damage.DamageSaves", "AfterSave", "AfterLoad", Target.SaveWorld),
        ("Ship Schematics", new Guid("d8ef13b4-2e4e-4b2e-9325-f2b4f30b537b"), "ShipSchematics.BlueprintSavePatch", "Postfix", null, Target.SaveRequest)
    };

    internal static bool QualifiedPostfix(Target target, Guid module, string? type, string name)
    {
        foreach (var observer in Observers)
            if (observer.Module == module && observer.Type == type &&
                (target == observer.SaveTarget && name == observer.Save || target == Target.LoadWorld && name == observer.Load)) return true;
        return false;
    }

    internal static bool Validate(MethodBase method, Target target, out MethodInfo? requestObserver)
    {
        requestObserver = null;
        var patches = HarmonyRuntime.GetPatchInfo(method);
        if (patches == null) return false;
        foreach (var patch in patches.Prefixes) ValidateOther(method, patch);
        foreach (var patch in patches.Transpilers) ValidateOther(method, patch);
        foreach (var patch in patches.Finalizers) ValidateOther(method, patch);
        var observedSave = false;
        foreach (var patch in patches.Postfixes)
        {
            if (Internal(patch)) continue;
            var callback = patch.PatchMethod;
            if (!QualifiedPostfix(target, callback.Module.ModuleVersionId, callback.DeclaringType?.FullName, callback.Name))
                throw Conflict(method, patch);
            var nested = HarmonyRuntime.GetPatchInfo(callback);
            if (nested != null)
                foreach (var owner in nested.Owners)
                    if (owner != Plugin.Owner) throw Compatibility.PatchConflict(callback, owner);
            if (target == Target.SaveRequest) requestObserver = callback;
            if (target != Target.LoadWorld) observedSave = true;
        }
        return observedSave;
    }

    private static void ValidateOther(MethodBase method, Patch patch)
    {
        if (!Internal(patch)) throw Conflict(method, patch);
    }

    private static bool Internal(Patch patch) => patch.owner == Plugin.Owner || patch.owner == Plugin.Owner + ".loadgate"
        || patch.owner == "sdmodkit.contentsave" && patch.PatchMethod.DeclaringType == typeof(SDModKit.Content.ContentSave)
        || patch.owner == "stellarworkshop.saving" && patch.PatchMethod.Module.ModuleVersionId == new Guid("40054375-01b2-4786-8f92-7675b17a25d2");

    private static CompatibilityFailure Conflict(MethodBase method, Patch patch)
    {
        var type = patch.PatchMethod.DeclaringType;
        foreach (var observer in Observers)
            if (observer.Type == type?.FullName) return Compatibility.PatchConflict(method, patch.owner, observer.Mod);
        var name = type?.Assembly.GetName().Name;
        if (name == "BuildZoop") name = "Build Zoop";
        if (name == "SafetyRails")
        {
            if (patch.PatchMethod.Module.ModuleVersionId == new Guid("2e16133b-f4d0-46c2-a3e0-5a9692f6efdd"))
                return new CompatibilityFailure("Safety Rails 1.0.0 reads the removed SnapDetectionState.partSettings field during native build preview.",
                    "Safety Rails 1.0.0 breaks build previews on Demo 0.8.3. Use a compatible mod profile and restart the game.");
            name = "Safety Rails";
        }
        return Compatibility.PatchConflict(method, patch.owner, name);
    }
}
