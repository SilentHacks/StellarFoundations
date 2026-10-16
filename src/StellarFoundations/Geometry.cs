using System;
using System.Runtime.CompilerServices;
using Core.Services;
using Managers.Interface.Services;
using Players;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using UnityEngine;
using UnityEngine.SceneManagement;
using WorldProvision.Interface.Services;
using WorldTracking.Interface.Model;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Interface.Services;

namespace StellarFoundations;

internal sealed class CheckedPlacement
{
    internal TrackedShipServer? Ship;
    internal uint Space;
    internal PlanetSurfaceSpace Surface = null!;
    internal Vector3 Cap, Foot, Normal;
    internal Quaternion Rotation;
    internal float Length;
    internal AttachmentKind Kind;
    internal float Clearance;
    internal bool PendingTerrain;
    internal PierRecord Record(ushort part) => new PierRecord {
        Id = Guid.NewGuid().ToString("N"), Part = part,
        LocalCap = Ship == null ? Position(Vector3.down * 0.55f) : Position(Cap),
        LocalFoot = Position(Ship == null ? Foot + Vector3.down * 0.55f : Foot), LocalFootNormal = Position(Normal), Length = Length,
        Kind = Kind, Clearance = Clearance, RequiresFloor = Ship != null, HasCollar = true
    };
    private static PositionD Position(Vector3 value) => new PositionD(value.x, value.y, value.z);
}

internal struct SurfaceContact
{
    internal Vector3 Foot, Normal;
    internal float Clearance;
    internal AttachmentKind Kind;
    internal bool PendingTerrain, HasBlocker;
    internal Vector3 Blocker;
}

internal static class Geometry
{
    private static readonly Collider[] Overlaps = new Collider[64];
    private static readonly RaycastHit[] TerrainHits = new RaycastHit[128];
    private static Collider? _blocker;
    private static readonly ConditionalWeakTable<GroundRecord, PlanetSurfaceSpace> CanonicalSpaces = new ConditionalWeakTable<GroundRecord, PlanetSurfaceSpace>();
    internal static Vector3 Vector(PositionD value) => new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    internal static Vector3Double Double(PositionD value) => new Vector3Double(value.X, value.Y, value.Z);
    internal static PositionD Position(Vector3Double value) => new PositionD(value.x, value.y, value.z);
    internal static PositionD Position(Vector3 value) => new PositionD(value.x, value.y, value.z);
    internal static Quaternion Rotation(RotationF value) => new Quaternion(value.X, value.Y, value.Z, value.W);
    internal static RotationF Rotation(Quaternion value) => new RotationF(value.x, value.y, value.z, value.w);
    internal static HullPartPosition FloorAtCap(Vector3 cap) =>
        new HullPartPosition(Vector3Int.RoundToInt(cap + Vector3.up * 0.55f), VoxelPartType.Floor);
    internal static bool SupportsFloor(PierRecord pier, HullPartPosition floor) =>
        pier.RequiresFloor && FloorAtCap(Vector(pier.LocalCap)).Equals(floor);

    internal static Quaternion Level(Vector3 gravity, Vector3 heading)
    {
        if (gravity.sqrMagnitude < 0.0001f) throw new InvalidOperationException("Planetary gravity is required.");
        var up = -gravity.normalized;
        var forward = Vector3.ProjectOnPlane(heading, up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
        return Quaternion.LookRotation(forward.normalized, up);
    }
    internal static Quaternion SurfaceRotation(Quaternion platform, Vector3 normal)
    {
        var forward = Vector3.ProjectOnPlane(platform * Vector3.forward, normal);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(platform * Vector3.right, normal);
        return Quaternion.LookRotation(forward.normalized, normal.normalized);
    }

    internal static GroundRecord Ground(uint shipId, uint spaceId, Vector3 position, Quaternion rotation)
    {
        var spaces = ServiceLocator.GetService<ISpacesServerProvider>();
        var surface = spaces.GetSpace(spaceId) as PlanetSurfaceSpace ?? throw new InvalidOperationException("Foundation must be on a planet surface.");
        return new GroundRecord {
            Id = Guid.NewGuid().ToString("N"), Ship = shipId, Planet = surface.PlanetId,
            Position = Position(surface.Center + (Vector3Double)position), Rotation = Rotation(rotation.normalized), Revision = 1
        };
    }

    internal static ShipPhysicsState Resolve(GroundRecord ground, uint targetSpace)
    {
        var source = CanonicalSpaces.GetValue(ground, record => new PlanetSurfaceSpace { PlanetId = record.Planet, Center = Double(record.Position) });
        var state = ServiceLocator.GetService<IWorldTransposeServer>().PhysicsState(source, targetSpace, new LocalPhysicsState {
            Position = Vector3.zero, Rotation = Rotation(ground.Rotation), Velocity = Vector3.zero
        });
        return new ShipPhysicsState(targetSpace, state.Position, state.Rotation, state.Velocity);
    }

    internal static bool InReach(TrackedPlayerServer player, uint space, Vector3 point)
    {
        var position = ServiceLocator.GetService<IWorldTransposeServer>().Position(player.SpaceId, space, player.Position);
        return (position - point).sqrMagnitude <= Policy.BuildReach * Policy.BuildReach;
    }

    internal static bool Validate(FoundationCommand request, TrackedPlayerServer player, out CheckedPlacement placement, out string reason)
    {
        placement = new CheckedPlacement(); reason = "";
        if (!request.Position.ValidLocal || !request.Rotation.Valid) { reason = "Invalid placement coordinates."; return false; }
        var spaces = ServiceLocator.GetService<ISpacesServerProvider>();
        var transpose = ServiceLocator.GetService<IWorldTransposeServer>();
        var ships = ServiceLocator.GetService<ShipsServerTracker>();
        var cap = Vector(request.Position);
        var rotation = Rotation(request.Rotation);
        uint source;
        if (request.Action == CommandAction.New)
        {
            if (!spaces.TryGetSpace(request.Space, out _) || !(spaces.GetSpaceOutsideOfShip(request.Space) is PlanetSurfaceSpace outside)) { reason = "Build on planetary terrain."; return false; }
            source = spaces.GetOrCreateSpaceForPosition(outside).Item1;
            cap = transpose.Position(request.Space, source, cap);
            rotation = transpose.Rotation(request.Space, source, rotation);
        }
        else
        {
            var ship = ships.GetShip(request.Ship);
            if (ship == null) { reason = "Structure is no longer available."; return false; }
            if (!RuntimeHooks.CanConvert(ship, out reason)) return false;
            var floor = FloorAtCap(cap);
            if (!ship.State.ContainsHullPart(floor)) { reason = "Aim at an ordinary floor tile."; return false; }
            var expected = PartPositionHelper.GetHullPartInteriorPos(floor) - Vector3.up * 0.05f;
            if ((cap - expected).sqrMagnitude > 0.0001f) { reason = "Foundation must snap beneath the selected floor."; return false; }
            foreach (var existing in ship.StatefulParts)
                if (PartRegistration.IsFoundation(existing) && (existing.LocalPosition - cap).sqrMagnitude < 0.04f) { reason = "This floor already has a foundation."; return false; }
            placement.Ship = ship; source = ship.RelativeSpaceId; rotation = Quaternion.identity;
        }
        if (!InReach(player, source, cap)) { reason = "Foundation is out of build reach."; return false; }
        var surface = spaces.GetSpaceOutsideOfShip(source) as PlanetSurfaceSpace;
        if (surface == null) { reason = "Planetary terrain is required."; return false; }
        if (!FindSurfaceScene(surface.PlanetId, out var sceneSpace, out var physics, source, cap)) { placement.PendingTerrain = true; reason = "Move closer while planetary terrain loads."; return false; }
        var sceneCap = transpose.Position(source, sceneSpace, cap);
        var sceneRotation = transpose.Rotation(source, sceneSpace, rotation);
        var gravity = ServiceLocator.GetService<IWorldEnvironmentServerProvider>().GetGravityAtPosition(sceneSpace, sceneCap);
        if (gravity.sqrMagnitude < 0.0001f) { reason = "Planetary gravity is required."; return false; }
        var up = -gravity.normalized;
        if (request.Action == CommandAction.New && Vector3.Dot(sceneRotation * Vector3.up, up) < 0.9999f) { reason = "Foundation mounting plate must be level."; return false; }
        if (!CheckAttachment(physics, sceneCap, sceneRotation, up, placement.Ship?.Id, out var contact, out reason))
        { placement.PendingTerrain = contact.PendingTerrain; return false; }
        var foot = contact.Foot; var normal = contact.Normal;
        placement.Space = request.Action == CommandAction.New ? source : placement.Ship!.SpaceId;
        placement.Surface = surface; placement.Cap = cap; placement.Rotation = rotation; placement.Length = (sceneCap - foot).magnitude;
        placement.Kind = contact.Kind; placement.Clearance = contact.Clearance;
        if (placement.Ship == null)
        {
            var sourceFoot = transpose.Position(sceneSpace, source, foot);
            placement.Foot = Quaternion.Inverse(rotation) * (sourceFoot - cap);
            placement.Normal = Quaternion.Inverse(rotation) * (transpose.Rotation(sceneSpace, source, Quaternion.identity) * normal);
        }
        else
        {
            placement.Foot = transpose.Position(sceneSpace, source, foot);
            placement.Normal = transpose.Rotation(sceneSpace, source, Quaternion.identity) * normal;
        }
        return true;
    }

    internal static bool FindSurfaceScene(ulong planet, out uint space, out PhysicsScene physics, uint? sourceSpace = null, Vector3 point = default)
    {
        var sceneProvider = ServiceLocator.GetService<ISceneServerProvider>();
        var spaces = ServiceLocator.GetService<ISpacesServerProvider>();
        var nearest = float.PositiveInfinity;
        space = 0; physics = default;
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (sceneProvider.TryGetSceneSpaceId(scene, out var candidateSpace) && spaces.TryGetSpace(candidateSpace, out var candidate)
                && candidate is PlanetSurfaceSpace surface && surface.PlanetId == planet)
            {
                var candidatePhysics = scene.GetPhysicsScene(); if (!candidatePhysics.IsValid()) continue;
                var distance = sourceSpace.HasValue ? ServiceLocator.GetService<IWorldTransposeServer>().Position(sourceSpace.Value, candidateSpace, point).sqrMagnitude : 0;
                if (distance >= nearest) continue;
                nearest = distance; space = candidateSpace; physics = candidatePhysics;
            }
        }
        return physics.IsValid();
    }

    // Preview and authority use the same terrain and obstruction checks. A terrain
    // lookup ignores hulls; the separate volume checks still reject an intervening
    // floor, machinery, another structure, or an occupied mounting plate.
    internal static bool CheckAttachment(PhysicsScene physics, Vector3 cap, Quaternion rotation, Vector3 up, uint? ship,
        out SurfaceContact contact, out string reason)
    {
        contact = default;
        if (!TerrainBelow(physics, cap, up, out var center, out reason, out var pending)) { contact.PendingTerrain = pending; return false; }
        contact.Foot = center.point; contact.Normal = center.normal;
        var clearance = Vector3.Dot(cap - center.point, up);
        if (!Policy.ClassifyAttachment(clearance, out var kind, out reason)) return false;
        if (!ship.HasValue && kind == AttachmentKind.Contact) { reason = "Raise the new foundation slightly to leave room for its mounting plate."; return false; }
        if (Vector3.Dot(center.normal, up) < 0.5f) { reason = "Terrain is too steep for the foundation foot."; return false; }
        var footRotation = SurfaceRotation(rotation, center.normal);
        var right = footRotation * Vector3.right;
        var forward = footRotation * Vector3.forward;
        for (var i = 0; i < 4; i++)
        {
            var offset = right * ((i & 1) == 0 ? 0.25f : -0.25f) + forward * ((i & 2) == 0 ? 0.25f : -0.25f);
            if (!TerrainBelow(physics, cap + offset, up, out var hit, out reason, out pending)) { contact.PendingTerrain = pending; return false; }
            if (!Policy.ClassifyAttachment(Vector3.Dot(cap + offset - hit.point, up), out _, out reason)) return false;
            if (Vector3.Dot(hit.normal, up) < 0.5f || Math.Abs(Vector3.Dot(hit.point - center.point, center.normal)) > 0.12f)
            { reason = "Foundation foot needs a continuous terrain surface."; return false; }
        }
        if (kind == AttachmentKind.Pier)
        {
            var count = physics.OverlapCapsule(cap - up * 0.15f, center.point + up * 0.06f, 0.10f, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (!Clear(count, null, true, out reason)) { MarkBlocker(ref contact, cap); if (reason.Length == 0) reason = "Column blocked. Choose a clear path to the ground."; return false; }
            count = physics.OverlapBox(center.point, new Vector3(0.32f, 0.055f, 0.32f), Overlaps, footRotation, ~0, QueryTriggerInteraction.Ignore);
            if (!Clear(count, null, true, out reason)) { MarkBlocker(ref contact, cap); if (reason.Length == 0) reason = "Footing blocked. Choose another floor."; return false; }
        }
        var plateCount = physics.OverlapBox(cap - rotation * Vector3.up * 0.1f, new Vector3(0.43f, 0.08f, 0.43f), Overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
        if (!Clear(plateCount, ship, kind == AttachmentKind.Contact, out reason)) { MarkBlocker(ref contact, cap); if (reason.Length == 0) reason = "Mounting plate blocked. Choose another floor."; return false; }
        var collarCount = physics.OverlapBox(cap + rotation * Vector3.up * Policy.CollarHeight, new Vector3(0.35f, Policy.CollarThickness * 0.5f, 0.35f), Overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
        if (!Clear(collarCount, ship, false, out reason)) { MarkBlocker(ref contact, cap); if (reason.Length == 0) reason = "Leave clear space on top of this floor for the anchor plate."; return false; }
        contact = new SurfaceContact { Foot = center.point, Normal = center.normal, Clearance = clearance, Kind = kind };
        reason = ""; return true;
    }

    private static bool Clear(int count, uint? allowedFloorShip, bool allowTerrain, out string reason)
    {
        reason = "";
        _blocker = null;
        if (count == Overlaps.Length) { reason = "Too many obstructions to validate this placement."; return false; }
        for (var i = 0; i < count; i++)
        {
            var collider = Overlaps[i];
            if (allowTerrain && collider.CompareTag("Planet")) continue;
            if (allowedFloorShip.HasValue && collider.CompareTag("HullFloor") && collider.GetComponentInParent<IShipPartHelper>()?.ShipId == allowedFloorShip.Value) continue;
            _blocker = collider; return false;
        }
        return true;
    }

    private static void MarkBlocker(ref SurfaceContact contact, Vector3 cap)
    { if (_blocker != null) { contact.HasBlocker = true; contact.Blocker = _blocker.ClosestPoint(cap); } }
    private static bool TerrainBelow(PhysicsScene physics, Vector3 cap, Vector3 up, out RaycastHit hit, out string reason, out bool pending)
    {
        // The accepted contact band is only 10 cm above the cap. Starting 2 m
        // above it avoids casting from inside a valid ground-contact surface.
        // Collect all hits so the supported floor cannot hide the terrain.
        const float probeHeight = 2f;
        var count = physics.Raycast(cap + up * probeHeight, -up, TerrainHits, probeHeight + Policy.MaximumLength + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        hit = default;
        pending = false;
        if (count == TerrainHits.Length) { reason = "Too many surfaces to validate this foundation."; return false; }
        var nearest = float.PositiveInfinity;
        for (var i = 0; i < count; i++)
            if (TerrainHits[i].collider.CompareTag("Planet") && TerrainHits[i].distance < nearest)
            { nearest = TerrainHits[i].distance; hit = TerrainHits[i]; }
        reason = "Terrain surface unavailable here. Wait for terrain to load or choose an exposed floor.";
        pending = nearest == float.PositiveInfinity;
        return nearest < float.PositiveInfinity;
    }
}
