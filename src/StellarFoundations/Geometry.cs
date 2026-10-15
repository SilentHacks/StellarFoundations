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
    internal PierRecord Record(ushort part) => new PierRecord {
        Id = Guid.NewGuid().ToString("N"), Part = part,
        LocalCap = Ship == null ? Position(Vector3.down * 0.55f) : Position(Cap),
        LocalFoot = Position(Ship == null ? Foot + Vector3.down * 0.55f : Foot), LocalFootNormal = Position(Normal), Length = Length
    };
    private static PositionD Position(Vector3 value) => new PositionD(value.x, value.y, value.z);
}

internal static class Geometry
{
    private static readonly Collider[] Overlaps = new Collider[64];
    private static readonly ConditionalWeakTable<GroundRecord, PlanetSurfaceSpace> CanonicalSpaces = new ConditionalWeakTable<GroundRecord, PlanetSurfaceSpace>();
    internal static Vector3 Vector(PositionD value) => new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    internal static Vector3Double Double(PositionD value) => new Vector3Double(value.X, value.Y, value.Z);
    internal static PositionD Position(Vector3Double value) => new PositionD(value.x, value.y, value.z);
    internal static PositionD Position(Vector3 value) => new PositionD(value.x, value.y, value.z);
    internal static Quaternion Rotation(RotationF value) => new Quaternion(value.X, value.Y, value.Z, value.W);
    internal static RotationF Rotation(Quaternion value) => new RotationF(value.x, value.y, value.z, value.w);

    internal static Quaternion Level(Vector3 gravity, Vector3 heading)
    {
        if (gravity.sqrMagnitude < 0.0001f) throw new InvalidOperationException("Planetary gravity is required.");
        var up = -gravity.normalized;
        var forward = Vector3.ProjectOnPlane(heading, up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
        return Quaternion.LookRotation(forward.normalized, up);
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
            var coord = Vector3Int.RoundToInt(cap + new Vector3(0, 0.55f, 0));
            if (!ship.State.ContainsHullPart(new HullPartPosition(coord, VoxelPartType.Floor))) { reason = "Aim at an ordinary floor tile."; return false; }
            var expected = PartPositionHelper.GetHullPartInteriorPos(new HullPartPosition(coord, VoxelPartType.Floor)) - Vector3.up * 0.05f;
            if ((cap - expected).sqrMagnitude > 0.0001f) { reason = "Foundation must snap beneath the selected floor."; return false; }
            foreach (var existing in ship.StatefulParts)
                if (PartRegistration.IsFoundation(existing) && (existing.LocalPosition - cap).sqrMagnitude < 0.04f) { reason = "This floor already has a foundation."; return false; }
            placement.Ship = ship; source = ship.RelativeSpaceId; rotation = Quaternion.identity;
        }
        if (!InReach(player, source, cap)) { reason = "Foundation is out of build reach."; return false; }
        var surface = spaces.GetSpaceOutsideOfShip(source) as PlanetSurfaceSpace;
        if (surface == null) { reason = "Planetary terrain is required."; return false; }
        if (!FindSurfaceScene(surface.PlanetId, out var sceneSpace, out var physics, source, cap)) { reason = "Terrain is still loading."; return false; }
        var sceneCap = transpose.Position(source, sceneSpace, cap);
        var sceneRotation = transpose.Rotation(source, sceneSpace, rotation);
        var gravity = ServiceLocator.GetService<IWorldEnvironmentServerProvider>().GetGravityAtPosition(sceneSpace, sceneCap);
        if (gravity.sqrMagnitude < 0.0001f) { reason = "Planetary gravity is required."; return false; }
        var up = -gravity.normalized;
        if (request.Action == CommandAction.New && Vector3.Dot(sceneRotation * Vector3.up, up) < 0.9999f) { reason = "Foundation mounting plate must be level."; return false; }
        if (!Footprint(physics, sceneCap, up, out var foot, out var normal, out reason)) return false;
        var length = (sceneCap - foot).magnitude;
        var count = physics.OverlapCapsule(sceneCap - up * 0.22f, foot + up * 0.15f, 0.13f, Overlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count == Overlaps.Length) { reason = "Too many obstructions to validate this placement."; return false; }
        for (var i = 0; i < count; i++)
        {
            var collider = Overlaps[i];
            if (collider.CompareTag("Planet")) continue;
            reason = "Foundation column is obstructed."; return false;
        }
        count = physics.OverlapBox(sceneCap - sceneRotation * Vector3.up * 0.1f, new Vector3(0.43f, 0.08f, 0.43f), Overlaps, sceneRotation, ~0, QueryTriggerInteraction.Ignore);
        if (count == Overlaps.Length) { reason = "Too many obstructions to validate this placement."; return false; }
        for (var i = 0; i < count; i++)
        {
            var collider = Overlaps[i];
            // The mounting plate may touch the floor it supports, but not other geometry.
            if (placement.Ship != null && collider.CompareTag("HullFloor") && collider.GetComponentInParent<IShipPartHelper>()?.ShipId == placement.Ship.Id) continue;
            reason = "Foundation mounting plate is obstructed."; return false;
        }
        placement.Space = request.Action == CommandAction.New ? source : placement.Ship!.SpaceId;
        placement.Surface = surface; placement.Cap = cap; placement.Rotation = rotation; placement.Length = length;
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

    internal static bool Footprint(PhysicsScene physics, Vector3 cap, Vector3 up, out Vector3 foot, out Vector3 normal, out string reason)
    {
        foot = normal = Vector3.zero; reason = "Ground is beyond the six metre reach.";
        if (!physics.Raycast(cap - up * 0.12f, -up, out var center, Policy.MaximumLength, ~0, QueryTriggerInteraction.Ignore) || !center.collider.CompareTag("Planet")) return false;
        foot = center.point; normal = center.normal;
        if ((cap - foot).magnitude < Policy.MinimumLength || (cap - foot).magnitude > Policy.MaximumLength) { reason = "Not enough room for the foundation."; return false; }
        if (Vector3.Dot(normal, up) < 0.5f) { reason = "Terrain is too steep for the foundation foot."; return false; }
        var right = Vector3.Cross(up, Math.Abs(up.z) < 0.9 ? Vector3.forward : Vector3.right).normalized;
        var forward = Vector3.Cross(right, up);
        for (var i = 0; i < 4; i++)
        {
            var offset = (i < 2 ? right : forward) * (i % 2 == 0 ? 0.25f : -0.25f);
            if (!physics.Raycast(cap + offset - up * 0.12f, -up, out var hit, Policy.MaximumLength + 0.5f, ~0, QueryTriggerInteraction.Ignore)
                || !hit.collider.CompareTag("Planet") || Math.Abs(Vector3.Dot(hit.point - foot, normal)) > 0.12f)
            { reason = "Foundation foot needs a continuous terrain surface."; return false; }
        }
        reason = ""; return true;
    }
}
