using System;
using System.Reflection;
using HarmonyLib;

namespace StellarFoundations;

internal sealed class CompatibilityFailure : NotSupportedException
{
    internal string PlayerMessage { get; }

    internal CompatibilityFailure(string message, string playerMessage) : base(message)
    { PlayerMessage = playerMessage; }
}

internal static class Compatibility
{
    internal const string SupportedGame = "0.8.3";
    internal const string SupportedKit = "1.3.3";
    private static readonly string[,] Modules = {
        { "Assembly-CSharp", "ad12ecf3-9f55-48b9-b3a6-7434851a54bf" },
        { "Core", "b55d8994-f58d-4aef-8a01-3b8fb22dd514" },
        { "Ships", "40d68276-d2e1-4372-8126-eb8ef3843cea" },
        { "Ships.Interface", "ae9e219b-6548-45c9-b374-2af51262ea2c" },
        { "Tools", "a17c2ccb-31b4-4348-98c2-7a6300ad8732" },
        { "Tools.Interface", "0fc4e128-a3b2-4e7c-8d49-79473978200b" },
        { "UI", "ec2f3c4a-05db-40dd-9b44-4d1efa65bab6" },
        { "UI.Interface", "a87b2cfa-400a-427b-bf93-3408fb9d533e" },
        { "Unity.TextMeshPro", "cf485f2b-d734-4dfa-9ff4-2627abe76f17" },
        { "UnityEngine.UI", "44187a17-305e-4f69-bb70-baffd98c8045" },
        { "WorldTracking", "8a607c92-38a3-4417-9cbc-2bee9fb22027" },
        { "WorldTracking.Interface", "6ba4b282-4834-4d04-838e-b5405398014c" },
        { "Saving", "87b6e505-313a-466a-b88f-ee5874d165ed" },
        { "Saving.Serialization", "2753a18d-a798-4bfa-b951-6186e3001956" },
        { "Research", "4b65e071-dff0-460c-b081-741c0cc298c9" },
        { "Planets", "3bf82828-84dd-463e-9d48-edf73a39ae46" },
        { "Planets.Interface", "3fb831a1-de86-41ba-a741-5d8fe9ccc6f6" },
        { "Players", "4aaabd19-2ad5-49fe-baec-770160117bb1" },
        { "SDModKit", "5f5977f6-e8b4-4aab-bc6e-c0179e0d3f9d" },
        { "UnityEngine.CoreModule", "9eb5f512-edf8-4b29-b73c-bc699a604447" },
        { "UnityEngine.PhysicsModule", "7916e147-eda7-4a34-9e34-e65df3a74249" },
        { "Unity.InputSystem", "35fba23b-51c9-4ebc-8010-04e6172278b9" },
        { "FishNet.Runtime", "f6b6c552-6a3f-4fec-b4ff-f9edae0f0238" }
    };
    internal static void Validate()
    {
        for (var i = 0; i < Modules.GetLength(0); i++)
        {
            var name = Modules[i, 0];
            var expected = new Guid(Modules[i, 1]);
            var installed = Assembly.Load(name).ManifestModule.ModuleVersionId;
            if (installed == expected) continue;
            throw new CompatibilityFailure($"{name} has MVID {installed}. Expected {expected}.", name == "SDModKit"
                ? $"Install SDModKit {SupportedKit} for StellarFoundations {Plugin.Version}, then restart the game."
                : $"StellarFoundations {Plugin.Version} supports Windows Demo {SupportedGame}. Check for a Foundations update for your game build.");
        }
    }
    internal static MethodInfo Method(Type type, string name, Type[]? arguments = null) =>
        AccessTools.Method(type, name, arguments) ?? throw new MissingMethodException(type.FullName, name);
    internal static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
    internal static void Exclusive(MethodBase method)
    {
        var patches = HarmonyRuntime.GetPatchInfo(method);
        if (patches != null)
            foreach (var owner in patches.Owners)
                if (owner != Plugin.Owner) throw PatchConflict(method, owner);
    }
    internal static CompatibilityFailure PatchConflict(MethodBase method, string owner, string? modName = null) => new CompatibilityFailure(
        $"Unqualified hook on {method.DeclaringType?.FullName}.{method.Name} ({owner}).",
        $"Foundations is incompatible with {modName ?? owner}. Use a compatible mod profile and restart the game.");
}
