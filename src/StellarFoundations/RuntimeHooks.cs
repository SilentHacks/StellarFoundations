using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Core.Services;
using FishNet.Connection;
using HarmonyLib;
using Managers.Interface.Model;
using Managers.Interface.Services;
using Managers.Server.Services;
using Managers.Server.Updaters;
using SDModKit.Game;
using Ships;
using Ships.Joints;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Parts.Common.Model;
using Ships.Parts.Common.Model.Reactions;
using Ships.Parts.DockingDoor;
using Ships.Parts.Thruster;
using Ships.Parts.WarpDrive;
using Ships.Parts.WoodThruster;
using Ships.Parts.Gyroscope;
using Ships.Parts.ReactionWheel;
using Ships.Physics;
using Ships.Physics.Collision;
using UnityEngine;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Interface.Services;

namespace StellarFoundations;

internal static class RuntimeHooks
{
    private static readonly Dictionary<uint, GroundRecord> Releases = new Dictionary<uint, GroundRecord>();
    private static FieldInfo Body = null!, BeforeVelocity = null!, BeforeAngular = null!, Contacts = null!, Changed = null!, ColliderParts = null!;
    private static PropertyInfo Nodes = null!, ParentNode = null!, ShipRef = null!;
    private static FieldInfo FloatingVisuals = null!;
    private static MethodInfo EnsureBody = null!;

    internal static void Install(HarmonyRuntime harmony)
    {
        Body = Compatibility.Field(typeof(ShipRigidbody), "_rigidbody");
        EnsureBody = Compatibility.Method(typeof(ShipRigidbody), "EnsureRigidBodyExists", Type.EmptyTypes);
        BeforeVelocity = Compatibility.Field(typeof(ShipRigidbody), "_velocityBeforeSim");
        BeforeAngular = Compatibility.Field(typeof(ShipRigidbody), "_angularVelocityBeforeSim");
        Contacts = Compatibility.Field(typeof(ShipCollisionDestruction), "_contacts");
        Changed = Compatibility.Field(typeof(ShipsJointsChangeProvider), "_changedShips");
        ColliderParts = Compatibility.Field(typeof(ShipStatefulColliders), "_statefulParts");
        FloatingVisuals = Compatibility.Field(typeof(ShipPartVisuals), "_floatingParts");
        BindGraph();

        Patch(harmony, typeof(ShipServerPhysics), "UpdateRootPhysics", nameof(BeforeRootPhysics), null, exclusive: true);
        Patch(harmony, typeof(ShipServerPhysics), "WritePhysicsState", nameof(BeforeWritePhysics), null, exclusive: true);
        Patch(harmony, typeof(ShipRigidbody), "RestoreVelocity", nameof(BeforeRestoreVelocity), null);
        Patch(harmony, typeof(ShipCollisionDestruction), "UpdateDestruction", null, null, nameof(ClearContacts));
        Patch(harmony, typeof(ShipsServerTracker), "LoadShip", null, nameof(AfterLoadShip));
        Patch(harmony, typeof(TrackedShipServer), "LoadRigidbody", null, nameof(AfterBodyLoaded));
        Patch(harmony, typeof(WorldPhysicsServerUpdater), "PrePhysics", nameof(BeforePhysicsTick), null);
        Patch(harmony, typeof(ShipsJointsTreeTracker), "BuildShipGraph", null, nameof(ChooseGroundedRoot), exclusive: true);
        Patch(harmony, typeof(DockingDoorsUpdater), "ConnectShips", nameof(BeforeDock), null, exclusive: true);
        Patch(harmony, typeof(ShipThrustersUpdater), "WriteFluidTargetConsumption", nameof(BeforeFluidDemand), null);
        Patch(harmony, typeof(ShipThrustersUpdater), "Update", nameof(BeforeThrusters), null);
        Patch(harmony, typeof(WoodThrustersUpdater), "Update", nameof(BeforeWoodThrusters), null);
        Patch(harmony, typeof(ShipWarpDrivesUpdater), "UpdateStates", nameof(BeforeWarp), null);
        Patch(harmony, typeof(GyroscopesUpdater), "CalculateShipGyros", nameof(BeforeGyroscopes), null);
        Patch(harmony, typeof(ReactionWheelsUpdater), "Update", nameof(BeforeReactionWheels), null);
        var add = Compatibility.Method(typeof(TrackedShipServer), "AddStatefulPart");
        harmony.Patch(add, prefix: new HarmonyMethod(typeof(RuntimeHooks), nameof(GuardAdd)));
        var remove = Compatibility.Method(typeof(TrackedShipServer), "RemoveStatefulPart", new[] { typeof(ushort) });
        harmony.Patch(remove, prefix: new HarmonyMethod(typeof(RuntimeHooks), nameof(GuardRemove)));
        Patch(harmony, typeof(ShipInteractionService), "RpcLogic___PlaceFloatingPart_ServerRPC___2366367339", nameof(GuardNativePlacement), null);
        Patch(harmony, typeof(ShipInteractionService), "RpcLogic___RemoveFloatingPart_ServerRPC___1471205784", nameof(GuardNativeRemoval), null);
        Patch(harmony, typeof(ShipInteractionService), "RpcLogic___RemoveHullPart_ServerRPC___930806135", nameof(GuardHullRemoval), null);
        Patch(harmony, typeof(ShipInteractionService), "RpcLogic___PickUpPart_ServerRPC___2228377996", nameof(GuardPickup), null);
        Patch(harmony, typeof(ShipInteractionService), "RpcLogic___ConfirmRepositionShip_ServerRPC___2616489143", nameof(GuardReposition), null);
        Patch(harmony, typeof(ShipInteractionService), "RpcLogic___StartRepositionShip_ServerRPC___328543758", nameof(GuardStartReposition), null);
        Patch(harmony, typeof(ShipPartVisuals), "AddStatefulPart", null, nameof(AfterVisualPart));
        Patch(harmony, typeof(ShipServerPhysics), "CreateColliderObject", null, nameof(AfterColliderObject));
        Patch(harmony, typeof(TrackedShip), "CreateInteriorColliderObject", null, nameof(AfterInteriorObject));
        Patch(harmony, typeof(ShipStatefulColliders), "AddStatefulPart", null, nameof(AfterColliderPart));
    }
    internal static void BindGraph()
    {
        var graph = typeof(ShipsJointsTreeTracker).GetNestedType("ShipsGraph", BindingFlags.NonPublic)!;
        var node = typeof(ShipsJointsTreeTracker).GetNestedType("ShipGraphNode", BindingFlags.NonPublic)!;
        Nodes = graph.GetProperty("Nodes")!; ParentNode = graph.GetProperty("ParentNode")!; ShipRef = node.GetProperty("ShipRef")!;
    }

    private static void Patch(HarmonyRuntime harmony, Type type, string name, string? prefix, string? postfix, string? finalizer = null, bool exclusive = false, string? optionalName = null)
    {
        var target = AccessTools.Method(type, name);
        if (target == null && optionalName != null)
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)) if (method.Name.StartsWith(optionalName, StringComparison.Ordinal)) { target = method; break; }
        if (target == null) throw new MissingMethodException(type.FullName, name);
        if (exclusive) Compatibility.Exclusive(target);
        harmony.Patch(target,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(RuntimeHooks), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(RuntimeHooks), postfix),
            finalizer: finalizer == null ? null : new HarmonyMethod(typeof(RuntimeHooks), finalizer));
    }

    private static bool BeforeRootPhysics(ShipRigidbody ____shipRigidbody, ShipState ____networkState)
    {
        FoundationNetwork.ObserveMotion(____networkState, ____shipRigidbody);
        var ground = FoundationRegistry.Get(____networkState.Id);
        if (ground == null) return true;
        Apply(____shipRigidbody, ____networkState, ground);
        return false;
    }

    private static void Apply(ShipRigidbody body, ShipState state, GroundRecord ground)
    {
        // LoadRigidbody creates the wrapper only. Native root-state initialization normally
        // creates its Unity body; grounding replaces that path and must preserve this step.
        var rigidbody = Body.GetValue(body) as Rigidbody;
        if (rigidbody == null)
        {
            EnsureBody.Invoke(body, null);
            rigidbody = (Rigidbody)Body.GetValue(body)!;
        }
        var scene = ServiceLocator.GetService<ISceneServerProvider>();
        var space = scene.TryGetSceneSpaceId(rigidbody.gameObject.scene, out var found) ? found : state.PhysicsState.SpaceId;
        var pose = Geometry.Resolve(ground, space);
        if (rigidbody.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative)
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        if (!rigidbody.isKinematic) { rigidbody.linearVelocity = Vector3.zero; rigidbody.angularVelocity = Vector3.zero; rigidbody.isKinematic = true; }
        rigidbody.detectCollisions = true;
        if (rigidbody.position != pose.Position) rigidbody.position = pose.Position;
        if (rigidbody.rotation != pose.Rotation) rigidbody.rotation = pose.Rotation;
        // Keep finite native mass for impact thresholds; kinematic immobility needs no artificial mass.
        var ship = ServiceLocator.GetService<ShipsServerTracker>().GetShip(state.Id);
        if (ship != null)
        {
            var mass = ship.GetMassIncludingConnectedShips();
            if (Math.Abs(rigidbody.mass - mass) > 0.001f) rigidbody.mass = Mathf.Max(0.1f, mass);
        }
        BeforeVelocity.SetValue(body, pose.Velocity); BeforeAngular.SetValue(body, Vector3.zero);
    }

    private static bool BeforeWritePhysics(ShipState ____networkState)
    {
        var ground = FoundationRegistry.Get(____networkState.Id);
        if (ground == null) return true;
        ____networkState.ServerSetPhysicsState(Geometry.Resolve(ground, ____networkState.PhysicsState.SpaceId)); return false;
    }
    private static bool BeforeRestoreVelocity(ShipRigidbody __instance)
    {
        var body = Body.GetValue(__instance)! as Rigidbody; return body == null || !body.isKinematic;
    }
    private static Exception? ClearContacts(ShipCollisionDestruction __instance, Exception? __exception)
    { ((IList)Contacts.GetValue(__instance)!).Clear(); return __exception; }
    private static void AfterLoadShip(ShipsServerTracker __instance, IShipState shipToLoad) => FoundationRegistry.Loaded(__instance.GetShip(shipToLoad.Id));
    private static void AfterBodyLoaded(TrackedShipServer __instance)
    {
        var ground = FoundationRegistry.Get(__instance.Id);
        if (ground == null) return;
        var state = __instance.RigidbodyObject.GetComponent<ShipRigidbody>();
        Apply(state, __instance.State, ground);
    }
    internal static void QueueRelease(TrackedShipServer ship, ShipPhysicsState state)
    {
        // Store a canonical attachment, not a velocity that becomes stale before the next tick.
        var ground = Geometry.Ground(ship.Id, state.SpaceId, state.Position, state.Rotation);
        Releases[ship.Id] = ground;
        Dirty(ship.Id, true);
    }
    private static void BeforePhysicsTick()
    {
        if (Releases.Count == 0) return;
        var ships = ServiceLocator.GetService<ShipsServerTracker>();
        foreach (var entry in Releases)
        {
            var ship = ships.GetShip(entry.Key);
            if (ship == null || !ship.IsRigidbodyLoaded) continue;
            var body = ship.RigidbodyObject.GetComponent<ShipRigidbody>();
            var rigidbody = Body.GetValue(body)! as Rigidbody;
            if (rigidbody == null) continue;
            var scenes = ServiceLocator.GetService<ISceneServerProvider>();
            var space = scenes.TryGetSceneSpaceId(rigidbody.gameObject.scene, out var current) ? current : ship.SpaceId;
            var pose = Geometry.Resolve(entry.Value, space);
            rigidbody.isKinematic = false;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.position = pose.Position; rigidbody.rotation = pose.Rotation;
            rigidbody.linearVelocity = pose.Velocity; rigidbody.angularVelocity = Vector3.zero;
            BeforeVelocity.SetValue(body, pose.Velocity); BeforeAngular.SetValue(body, Vector3.zero);
            var destruction = ship.RigidbodyObject.GetComponent<ShipCollisionDestruction>();
            if (destruction != null) ((IList)Contacts.GetValue(destruction)!).Clear();
            ship.SetPhysicsState(Geometry.Resolve(entry.Value, ship.SpaceId));
        }
        Releases.Clear();
    }
    internal static void Dirty(uint ship, bool server)
    {
        ShipsJointsChangeProvider? provider = server ? GameRefs.Service<ShipsJointsChangeProviderServer>() : GameRefs.Service<ShipsJointsChangeProviderClient>();
        if (provider != null) ((HashSet<uint>)Changed.GetValue(provider)!).Add(ship);
    }
    private static void ChooseGroundedRoot(object __1)
    {
        // Bind the out parameter directly. Harmony's __args array may retain its pre-call null.
        SelectGroundedRoot(__1);
    }
    internal static void SelectGroundedRoot(object graph)
    {
        object? preferred = null;
        foreach (DictionaryEntry pair in (IDictionary)Nodes.GetValue(graph, null)!)
        {
            var ship = (IShipStateRead)ShipRef.GetValue(pair.Value, null)!;
            if (!FoundationRegistry.Grounded(ship.Id)) continue;
            if (preferred != null) throw new InvalidOperationException("Two independently grounded bases are connected. Refusing competing attachments.");
            preferred = pair.Value;
        }
        if (preferred != null) ParentNode.SetValue(graph, preferred, null);
    }

    internal static uint? AssemblyGround(uint ship, bool live = false)
    {
        var ships = ServiceLocator.GetService<ShipsServerTracker>();
        if (FoundationRegistry.Count == 0) return null;
        if (live)
        {
            var seen = new HashSet<uint>(); var pending = new Stack<uint>(); pending.Push(ship);
            uint? found = null;
            var changes = ServiceLocator.GetService<ShipsJointsChangeProviderServer>();
            while (pending.Count > 0)
            {
                var id = pending.Pop(); if (!seen.Add(id)) continue;
                if (seen.Count > Policy.MaximumStructures) throw new InvalidOperationException("Docking graph exceeds the supported validation budget.");
                if (FoundationRegistry.Grounded(id))
                {
                    if (found.HasValue && found.Value != id) throw new InvalidOperationException("Existing docking graph contains competing ground attachments.");
                    found = id;
                }
                var member = ships.GetShip(id); if (member == null) continue;
                foreach (var part in member.GetAllStatefulParts<DockingDoorState>())
                    if (part.State is DockingDoorState door && door.IsDocked) pending.Push(door.DockedConnectedPart.ShipId);
                foreach (var rotor in changes.GetShipRotorJoints(id)) pending.Push(rotor.RotorPart.ShipId == id ? rotor.OtherShipId : rotor.RotorPart.ShipId);
            }
            return found;
        }
        if (!ships.TryGetRootShip(ship, out var root)) return FoundationRegistry.Grounded(ship) ? ship : (uint?)null;
        if (FoundationRegistry.Grounded(root.Id)) return root.Id;
        foreach (var member in ships.GetRootConnectedShips(root.Id)) if (FoundationRegistry.Grounded(member.Ship.Id)) return member.Ship.Id;
        return null;
    }
    private static bool BeforeDock(ShipMagnetData doorA, ShipMagnetData doorB)
    {
        var accepted = Policy.CanJoin(AssemblyGround(doorA.ShipId, true), AssemblyGround(doorB.ShipId, true));
        if (!accepted) FoundationTool.NotifyBlocked("Cannot dock two independently grounded bases.");
        return accepted;
    }

    internal static bool CanConvert(TrackedShipServer ship, out string reason)
    {
        reason = "";
        var spaces = ServiceLocator.GetService<ISpacesServerProvider>();
        if (!(spaces.GetSpace(ship.SpaceId) is PlanetSurfaceSpace)) { reason = "Undock this structure and detach it from moving rotor sections first."; return false; }
        foreach (var part in ship.StatefulParts)
        {
            if (part.State is DockingDoorState door && door.IsDocked && !FoundationRegistry.Grounded(ship.Id)) { reason = "Undock visiting ships before converting this structure."; return false; }
            if (part.State is WarpDriveState drive && (drive.State == WarpDriveStateType.WarmingUp || drive.State == WarpDriveStateType.Warping)) { reason = "Cancel warp before attaching a foundation."; return false; }
        }
        var provider = ServiceLocator.GetService<ShipsJointsChangeProviderServer>();
        foreach (var rotor in provider.GetShipRotorJoints(ship.Id))
            if (rotor.RotorPart.ShipId != ship.Id) { reason = "Foundations cannot ground a rotor's moving section."; return false; }
        if (!ship.IsRigidbodyLoaded) { reason = "Wait for the structure's physics to load."; return false; }
        return true;
    }

    private static bool BeforeFluidDemand(IShipStateWrite ____ship) => !FoundationRegistry.Grounded(____ship.Id);
    private static bool BeforeThrusters(IShipStateWrite ____ship)
    {
        if (!FoundationRegistry.Grounded(____ship.Id)) return true;
        foreach (var part in ____ship.GetAllStatefulParts<ThrusterState>())
        { var state = (ThrusterState)part.State; if (state.ThrusterStrength != 0) ____ship.UpdatePartState(part.Id, state with { ThrusterStrength = 0 }); }
        return false;
    }
    private static bool BeforeWoodThrusters(IShipStateWrite ____ship)
    {
        if (!FoundationRegistry.Grounded(____ship.Id)) return true;
        foreach (var part in ____ship.GetAllStatefulParts<WoodThrusterState>())
        { var state = (WoodThrusterState)part.State; if (state.NormalizedThrustLevel != 0 || state.IsHavingTroubleStarting) ____ship.UpdatePartState(part.Id, state with { NormalizedThrustLevel = 0, IsHavingTroubleStarting = false }); }
        return false;
    }
    private static bool BeforeWarp(IShipStateRead ____ship, ShipState ____networkState)
    {
        if (!AssemblyGround(____ship.Id).HasValue) return true;
        foreach (var part in ____ship.GetAllStatefulParts<WarpDriveState>())
        {
            var state = (WarpDriveState)part.State;
            if (state.State != WarpDriveStateType.CantWarpFromHere || state.WarpStartTimer != 0)
                ____networkState.SetPartState(part.Id, state with { State = WarpDriveStateType.CantWarpFromHere, WarpStartTimer = 0 });
        }
        return false;
    }
    private static bool BeforeGyroscopes(IShipStateWrite __0)
    {
        if (!FoundationRegistry.Grounded(__0.Id)) return true;
        foreach (var part in __0.GetAllStatefulParts<GyroscopeState>())
        {
            var state = (GyroscopeState)part.State;
            if (state.RotationStrength != Vector3.zero || state.TargetGyroscopeAnimation != Vector3.zero)
                __0.UpdatePartState(part.Id, state with { RotationStrength = Vector3.zero, TargetGyroscopeAnimation = Vector3.zero });
        }
        foreach (var part in __0.GetAllStatefulParts<PilotSeatState>())
        {
            var state = (PilotSeatState)part.State;
            if (state.InternalGyroStrength != Vector3.zero || state.TargetGyroscopeAnimation != Vector3.zero)
                __0.UpdatePartState(part.Id, state with { InternalGyroStrength = Vector3.zero, TargetGyroscopeAnimation = Vector3.zero });
        }
        return false;
    }
    private static bool BeforeReactionWheels(IShipStateWrite ____ship)
    {
        if (!FoundationRegistry.Grounded(____ship.Id)) return true;
        foreach (var part in ____ship.GetAllStatefulParts<ReactionWheelState>())
            if (((ReactionWheelState)part.State).RotationStrength != 0) ____ship.UpdatePartState(part.Id, new ReactionWheelState { RotationStrength = 0 });
        return false;
    }
    internal static bool GuardAdd(Ships.Interface.Settings.PartSettings settings) => settings.id != Policy.PartSettingsId || (Plugin.Ready && FoundationRegistry.Mutation);
    private static bool GuardRemove(TrackedShipServer __instance, ushort partId) => FoundationRegistry.Mutation || !__instance.TryGetStatefulPart(partId, out var part) || !PartRegistration.IsFoundation(part);
    internal static bool GuardNativePlacement(PlaceFloatingPartNetworkRequest __0) => __0.SettingsId != Policy.PartSettingsId;
    private static bool GuardNativeRemoval(RemoveFloatingPartNetworkRequest __0)
    {
        var ship = ServiceLocator.GetService<ShipsServerTracker>().GetShip(__0.ShipId);
        return ship == null || !ship.TryGetStatefulPart(__0.PartId, out var part) || !PartRegistration.IsFoundation(part);
    }
    private static bool GuardHullRemoval(RemoveHullPartNetworkRequest __0) =>
        !FoundationRegistry.HasFloorAttachment(__0.ShipId, __0.Position);
    private static bool GuardPickup(PartInteractNetworkRequest __0)
    {
        var ship = ServiceLocator.GetService<ShipsServerTracker>().GetShip(__0.ShipId);
        return ship == null || !ship.TryGetStatefulPart(__0.PartId, out var part) || !PartRegistration.IsFoundation(part);
    }
    private static bool GuardReposition(NetworkConnection __1)
    {
        if (__1 == null) return true;
        var player = GameRefs.PlayersServer?.GetTrackedPlayerFromConnectionId(__1.ClientId);
        return player == null || !AssemblyGround(player.State.EntityId).HasValue;
    }
    private static bool GuardStartReposition(NetworkConnection __0)
    {
        if (__0 == null || FoundationRegistry.Count == 0) return true;
        var player = GameRefs.PlayersServer?.GetTrackedPlayerFromConnectionId(__0.ClientId);
        if (player == null || !ServiceLocator.GetService<ShipsServerTracker>().TryGetNearestRepositionableShip(player.SpaceId, player.Position, out var ship)) return true;
        if (!AssemblyGround(ship.Id).HasValue) return true;
        FoundationTool.NotifyBlocked("Remove the last foundation before repositioning this structure."); return false;
    }
    private static void AfterVisualPart(ShipPartVisuals __instance, StatefulPart part)
    {
        if (!PartRegistration.IsFoundation(part)) return;
        var visuals = (IDictionary)FloatingVisuals.GetValue(__instance)!;
        if (visuals[part.Id] is IShipPartVisual visual) TrackAppearance(visual.GameObject.transform, __instance.ShipId, part.Id, false);
    }
    private static void AfterColliderObject(ShipState ____networkState, GameObject ____colliders) => FitColliderObject(____colliders, ____networkState.Id);
    private static void AfterInteriorObject(TrackedShip __instance) => FitColliderObject(__instance.InteriorColliders, __instance.Id);
    private static void FitColliderObject(GameObject root, uint ship)
    {
        if (root == null) return;
        var marker = root.GetComponent<FoundationColliderOwner>() ?? root.AddComponent<FoundationColliderOwner>(); marker.Ship = ship;
        foreach (var component in root.GetComponentsInChildren<ShipStatefulColliders>())
            foreach (DictionaryEntry pair in (IDictionary)ColliderParts.GetValue(component)!)
                if (FoundationRegistry.Pier(ship, (ushort)pair.Key) is PierRecord pier) TrackAppearance(((GameObject)pair.Value!).transform, ship, pier.Part, true);
    }
    private static void AfterColliderPart(ShipStatefulColliders __instance, StatefulPart part)
    {
        if (!PartRegistration.IsFoundation(part)) return;
        var owner = __instance.GetComponentInParent<FoundationColliderOwner>();
        if (owner == null) return;
        var parts = (IDictionary)ColliderParts.GetValue(__instance)!;
        if (parts[part.Id] is GameObject item) TrackAppearance(item.transform, owner.Ship, part.Id, true);
    }
    private static readonly List<FoundationAppearance> Appearances = new List<FoundationAppearance>();
    private static void TrackAppearance(Transform root, uint ship, ushort part, bool colliders)
    {
        var appearance = root.GetComponent<FoundationAppearance>() ?? root.gameObject.AddComponent<FoundationAppearance>();
        appearance.Ship = ship; appearance.Part = part; appearance.Colliders = colliders;
        if (!Appearances.Contains(appearance)) Appearances.Add(appearance);
        Fit(appearance);
    }
    private static void Fit(FoundationAppearance item)
    { var pier = FoundationRegistry.Pier(item.Ship, item.Part); if (pier != null) PartRegistration.Fit(item.transform, pier, item.Colliders); }
    internal static void RefreshAppearance()
    {
        for (var i = Appearances.Count - 1; i >= 0; i--) if (Appearances[i] == null) Appearances.RemoveAt(i); else Fit(Appearances[i]);
    }
    internal static void WorldExit() { Releases.Clear(); Appearances.Clear(); }
}

internal sealed class FoundationColliderOwner : MonoBehaviour { internal uint Ship; }
internal sealed class FoundationAppearance : MonoBehaviour { internal uint Ship; internal ushort Part; internal bool Colliders; }
