using System;
using System.Reflection;
using HarmonyLib;

namespace StellarFoundations;

internal static class Compatibility
{
    private static readonly string[,] Modules = {
        { "Assembly-CSharp", "0379aaca-5dff-430e-b4db-aa2538738891" },
        { "Ships", "f1589ce1-5692-4187-a0d8-d2d85bb4aa43" },
        { "Ships.Interface", "ebf1fc59-7637-4f52-9fec-0ff2589c6bc0" },
        { "Tools", "03619ecd-93e2-4257-b19c-34b344ba2e86" },
        { "Tools.Interface", "cfc23780-fd66-45dd-91b2-ee7594413a63" },
        { "UI", "1e4d82f5-832b-4320-bca2-cd5da4fa7ddd" },
        { "UI.Interface", "0007052d-91d7-488f-9739-a7834a0905ff" },
        { "Unity.TextMeshPro", "cf485f2b-d734-4dfa-9ff4-2627abe76f17" },
        { "UnityEngine.UI", "44187a17-305e-4f69-bb70-baffd98c8045" },
        { "WorldTracking", "9e7dd5cb-71fb-4ad7-b191-2136eb4e6e60" },
        { "WorldTracking.Interface", "95f8a341-460f-41f0-ab5f-4a73c54bb3d6" },
        { "Saving", "2a99be51-4517-4b4f-82c8-f553c7c99567" },
        { "Saving.Serialization", "95dcd6cc-8154-47a9-adfe-94b4b7e34608" },
        { "Research", "57974d75-9a2e-4c9c-99fc-719932332295" },
        { "Planets", "98365094-6cc6-4889-87a1-e0b749f4a868" },
        { "Planets.Interface", "b35f2a77-b160-46a5-84e8-a839c7391e21" },
        { "Players", "e20d6376-9433-4ae4-b34a-857934659cb5" },
        { "SDModKit", "5f5977f6-e8b4-4aab-bc6e-c0179e0d3f9d" },
        { "UnityEngine.CoreModule", "9eb5f512-edf8-4b29-b73c-bc699a604447" },
        { "UnityEngine.PhysicsModule", "7916e147-eda7-4a34-9e34-e65df3a74249" },
        { "Unity.InputSystem", "35fba23b-51c9-4ebc-8010-04e6172278b9" },
        { "FishNet.Runtime", "f6b6c552-6a3f-4fec-b4ff-f9edae0f0238" }
    };
    internal static void Validate()
    {
        for (var i = 0; i < Modules.GetLength(0); i++)
            if (Assembly.Load(Modules[i, 0]).ManifestModule.ModuleVersionId != new Guid(Modules[i, 1]))
                throw new NotSupportedException(Modules[i, 0] + " differs from the inspected game. Foundations cannot safely load grounded worlds.");
    }
    internal static MethodInfo Method(Type type, string name, Type[]? arguments = null) =>
        AccessTools.Method(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name);
    internal static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
    internal static void Exclusive(MethodBase method)
    {
        var patches = HarmonyRuntime.GetPatchInfo(method);
        if (patches != null)
            foreach (var owner in patches.Owners)
                if (owner != Plugin.Owner) throw new NotSupportedException("Conflicting foundation movement hook: " + method.DeclaringType?.Name + "." + method.Name + " (" + owner + ").");
    }
    internal static void SaveHook(MethodBase method)
    {
        var patches = HarmonyRuntime.GetPatchInfo(method);
        if (patches == null) return;
        foreach (var owner in patches.Owners)
        {
            if (owner == Plugin.Owner || owner == Plugin.Owner + ".loadgate" || owner == "sdmodkit.contentsave") continue;
            if (owner == "stellarworkshop.saving" && Assembly.Load("StellarWorkshop").ManifestModule.ModuleVersionId == new Guid("40054375-01b2-4786-8f92-7675b17a25d2")) continue;
            throw new NotSupportedException("Unqualified save hook on " + method.DeclaringType?.Name + "." + method.Name + " (" + owner + ").");
        }
    }
}
