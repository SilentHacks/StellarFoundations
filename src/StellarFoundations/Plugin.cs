using System;
using HarmonyLib;
using MelonLoader;
using SDModKit;
using SDModKit.Api;
using SDModKit.Configuration;
using SDModKit.Input;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(StellarFoundations.Plugin), StellarFoundations.Plugin.DisplayName, StellarFoundations.Plugin.Version, "SilentHacks", null)]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: HarmonyDontPatchAll]
[assembly: StellarMod("foundations", StellarFoundations.Plugin.DisplayName, Description = "Ground anchors and adjustable piers for stationary planetary bases.")]

namespace StellarFoundations;

public sealed class Plugin : MelonMod
{
    internal const string DisplayName = "StellarFoundations";
    internal const string Version = "0.6.2";
    internal const string Owner = "silenthacks.stellarfoundations";
    internal static bool Ready;
    internal static string Failure = string.Empty;
    internal static FoundationMod Mod = null!;
    private static bool _failedLoad;
    public override void OnInitializeMelon()
    {
        try
        {
            // Install the orphan-attachment gate before optional compatibility checks: an
            // unsupported runtime must not silently load a grounded structure as a free ship.
            var loadGate = new HarmonyRuntime(Owner + ".loadgate");
            loadGate.Patch(Compatibility.Method(typeof(Ships.Services.ShipsServerTracker), "LoadShip"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(ValidateShipLoad)));
            loadGate.Patch(Compatibility.Method(typeof(Saving.Services.GameLoader), "LoadState"), prefix: new HarmonyMethod(typeof(Plugin), nameof(GuardDisabledWorld)) { priority = HarmonyLib.Priority.First });
            loadGate.Patch(Compatibility.Method(typeof(Ships.TrackedShipServer), "AddStatefulPart"), prefix: new HarmonyMethod(typeof(RuntimeHooks), nameof(RuntimeHooks.GuardAdd)));
            loadGate.Patch(Compatibility.Method(typeof(Managers.Server.Services.ShipInteractionService), "RpcLogic___PlaceFloatingPart_ServerRPC___2366367339"), prefix: new HarmonyMethod(typeof(RuntimeHooks), nameof(RuntimeHooks.GuardNativePlacement)));
            Compatibility.Validate();
            Mod = new FoundationMod();
            ModKit.Register(Mod, ConfigFile.For("stellardrive.foundations"));
            if (!ModToggles.Enabled(Mod.Id)) { Mod = null!; LoggerInstance.Msg("Foundations is disabled; saved-pier load protection remains active."); return; }
            if (Mod.HeightModifier == null) throw new InvalidOperationException("Foundations controls did not register.");
            PartRegistration.Register();
        }
        catch (Exception error) { Fault(error); }
    }
    private static void GuardDisabledWorld(Saving.Interface.Model.ISavedGameFile fileEntry)
    {
        if (Ready || fileEntry?.SaveFileInfo == null) return;
        var world = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(fileEntry.SaveFileInfo.FullName));
        foreach (var ship in world["ships"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
            foreach (var part in ship["statefulFloatingParts"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
                if (part.Value<ushort>("settingsId") == Policy.PartSettingsId)
                {
                    var error = new NotSupportedException("Enable a compatible Foundations build before loading this save, or load an exported copy without piers.");
                    LoadFault(error); throw error;
                }
    }
    private static void ValidateShipLoad(Ships.Interface.Model.State.IShipState shipToLoad)
    {
        try { FoundationRegistry.ValidateSnapshot(shipToLoad); }
        catch (Exception error) { if (!SDModKit.Game.GameRefs.InWorld) LoadFault(error); else Fault(error); throw; }
    }
    public override void OnLateInitializeMelon()
    {
        if (Mod == null) return;
        var harmony = new HarmonyRuntime(Owner);
        try
        {
            RuntimeHooks.Install(harmony);
            FoundationSaving.Install(harmony);
            FoundationTool.Install(harmony);
            FoundationHud.Install(harmony);
            FoundationNetwork.Initialize();
            Ready = true;
            LoggerInstance.Msg(DisplayName + " " + Version + " ready. Matching clients required.");
        }
        catch (Exception error) { harmony.UnpatchSelf(); Fault(error); }
    }
    public override void OnUpdate()
    {
        if (_failedLoad)
        {
            _failedLoad = false;
            if (FishNet.Managing.NetworkManager.Instances.Count > 0)
            {
                var network = FishNet.InstanceFinder.NetworkManager;
                network.ClientManager.StopConnection();
                if (network.ServerManager.Started) network.ServerManager.StopConnection(true);
            }
            FoundationNetwork.WorldExit(); FoundationRegistry.Clear(); RuntimeHooks.WorldExit(); FoundationTool.Clear(); FoundationSaving.WorldExit();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu", UnityEngine.SceneManagement.LoadSceneMode.Single);
            return;
        }
        if (!Ready) return;
        try { FoundationNetwork.Update(); FoundationSaving.Update(); FoundationTool.Update(); }
        catch (Exception error) { Fault(error); }
    }
    public override void OnGUI() { FoundationTool.Draw(); FoundationSaving.Draw(); }
    public override void OnApplicationQuit() => FoundationSaving.Drain();
    internal static void Fault(Exception error)
    {
        Failure = error.GetBaseException().Message;
        MelonLogger.Error(DisplayName + ": " + error);
        // Already-grounded roots remain controlled by the physics hooks after a runtime fault.
    }
    internal static void LoadFault(Exception error) { Fault(error); _failedLoad = true; }
}

public sealed class FoundationMod : SDMod
{
    internal Keybind HeightModifier = null!;
    public override string Id => "foundations";
    public override string Name => Plugin.DisplayName;
    public override bool HostOnly => false;
    public override void OnRegistered()
    {
        HeightModifier = Keybinds.Register(Config, "Controls", "HeightModifier", Key.LeftShift, "Adjust foundation height with scroll");
        AddPage("Foundations", () => {
            UnityEngine.GUILayout.Label("Select Foundation in the build menu. Ground contact and extended piers adapt automatically.");
            UnityEngine.GUILayout.Label("For new foundations, scroll rotates; hold " + HeightModifier.Display + " and scroll to adjust height.");
            UnityEngine.GUILayout.Label("Hold build to convert an existing structure. Hold the recycler on the last pier to release it.");
            UnityEngine.GUILayout.Label("Foundations support ordinary floors. Existing builds keep their current position and tilt.");
            UnityEngine.GUILayout.Label("Recycle an anchor at its mounting plate or the floor directly above it. Damage to its attached floor also destroys the anchor.");
            UnityEngine.GUILayout.Label("Grounded bases inhibit their own propulsion and warp; machinery remains active.");
            UnityEngine.GUILayout.Label(FoundationSaving.Status);
            UnityEngine.GUILayout.Label("To stop using Foundations, create a separate save copy with its piers removed. Those structures will use normal ship physics in that copy.");
            if (SDModKit.Game.GameRefs.InWorld && SDModKit.Game.GameRefs.IsServer && !FoundationSaving.Busy && UnityEngine.GUILayout.Button("Save a separate copy without Foundations")) FoundationSaving.RequestExport();
        });
    }
    public override void OnWorldExit()
    {
        FoundationSaving.WorldExit();
        FoundationNetwork.WorldExit();
        FoundationRegistry.Clear();
        FoundationTool.Clear();
        RuntimeHooks.WorldExit();
    }
}
