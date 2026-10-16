using System;
using System.Collections.Generic;
using System.IO;
using Core.Services;
using SDModKit.Content;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model;
using Ships.Interface.Model.Placement;
using UnityEngine;

namespace StellarFoundations;

internal static class FoundationRegistry
{
    internal const string MetadataKey = "silenthacks.foundations.v1";
    private static readonly Dictionary<uint, GroundRecord> Grounds = new Dictionary<uint, GroundRecord>();
    internal static IEnumerable<GroundRecord> Records => Grounds.Values;
    internal static bool Initialized;
    internal static int Count => Grounds.Count;
    internal static bool Mutation;
    internal static GroundRecord? Get(uint ship) => Grounds.TryGetValue(ship, out var record) ? record : null;
    internal static uint Revision(uint ship) => Get(ship)?.Revision ?? 0;
    internal static bool Grounded(uint ship) => Grounds.ContainsKey(ship);
    internal static PierRecord? Pier(uint ship, ushort part)
    {
        if (Grounds.TryGetValue(ship, out var ground)) foreach (var pier in ground.Piers) if (pier.Part == part) return pier;
        return null;
    }
    internal static bool HasFloorAttachment(uint ship, HullPartPosition floor)
    {
        var ground = Get(ship);
        if (ground != null) foreach (var pier in ground.Piers) if (Geometry.SupportsFloor(pier, floor)) return true;
        return false;
    }
    internal static void Restore(string? json)
    {
        Clear();
        if (json == null) return;
        var document = GroundDocument.Decode(json);
        foreach (var record in document.Structures) Grounds.Add(record.Ship, record);
        Initialized = true;
    }
    internal static GroundDocument Snapshot()
    {
        var document = new GroundDocument { Structures = new List<GroundRecord>(Grounds.Values) };
        // An immutable copy crosses the worker boundary.
        return GroundDocument.Decode(document.Encode());
    }
    internal static void Set(GroundRecord ground)
    {
        var old = Get(ground.Ship); var wasInitialized = Initialized;
        Grounds[ground.Ship] = ground; Initialized = true;
        try { Persist(); }
        catch
        {
            if (old == null) Grounds.Remove(ground.Ship); else Grounds[ground.Ship] = old;
            Initialized = wasInitialized; throw;
        }
    }
    internal static void Persist()
    {
        if (Initialized) WorldData.Set(MetadataKey, new GroundDocument { Structures = new List<GroundRecord>(Grounds.Values) }.Encode());
    }
    internal static void Loaded(TrackedShipServer ship)
    {
        var ground = Get(ship.Id);
        var found = 0;
        foreach (var part in ship.StatefulParts)
        {
            if (!PartRegistration.IsFoundation(part)) continue;
            found++;
            var pier = Pier(ship.Id, part.Id);
            if (pier == null || (part.LocalPosition - Geometry.Vector(pier.LocalCap)).sqrMagnitude > 0.0001f)
                throw new InvalidDataException("Foundation part has no matching saved attachment. The world cannot safely start physics.");
            ValidateFloor(pier, !pier.RequiresFloor || ship.State.ContainsHullPart(Geometry.FloorAtCap(Geometry.Vector(pier.LocalCap))));
        }
        if (ground != null && found != ground.Piers.Count) throw new InvalidDataException("Saved foundation supports are missing from the structure.");
        if (ground != null) ship.SetPhysicsState(Geometry.Resolve(ground, ship.SpaceId));
        ship.State.HullPartAdded += _ => FoundationNetwork.StructureChanged(ship.Id);
        ship.State.HullPartRemoved += _ => FoundationNetwork.StructureChanged(ship.Id);
        ship.State.HullPartRemoved += part => FloorDestroyed(ship, part.HullPlacement.Position);
        ship.State.StatefulPartAdded += _ => FoundationNetwork.StructureChanged(ship.Id);
        ship.State.StatefulPartRemoved += _ => FoundationNetwork.StructureChanged(ship.Id);
        ship.State.StatefulPartRemoved += part => Removed(ship, part);
    }
    internal static void ValidateSnapshot(IShipState ship)
    {
        var ground = Get(ship.Id); var found = 0;
        var floors = new HashSet<HullPartPosition>();
        if (ground != null) foreach (var floor in ship.HullParts) floors.Add(floor.HullPlacement.Position);
        foreach (var part in ship.StatefulParts)
        {
            if (!PartRegistration.IsFoundation(part)) continue;
            found++;
            var pier = Pier(ship.Id, part.Id);
            if (pier == null || (part.LocalPosition - Geometry.Vector(pier.LocalCap)).sqrMagnitude > 0.0001f)
                throw new InvalidDataException("Foundation copies require a new validated attachment. This structure was refused before it entered physics.");
            ValidateFloor(pier, !pier.RequiresFloor || floors.Contains(Geometry.FloorAtCap(Geometry.Vector(pier.LocalCap))));
        }
        if (ground != null && found != ground.Piers.Count) throw new InvalidDataException("Saved foundation supports are missing from the structure.");
    }
    private static void ValidateFloor(PierRecord pier, bool present)
    {
        if (pier.RequiresFloor && !present)
            throw new InvalidDataException("A saved foundation anchor is missing its supporting floor.");
    }
    private static void FloorDestroyed(TrackedShipServer ship, HullPartPosition floor)
    {
        var ground = Get(ship.Id);
        if (ground == null) return;
        // Copy identities before native removal callbacks mutate the support list.
        var removed = new List<ushort>();
        foreach (var pier in ground.Piers) if (Geometry.SupportsFloor(pier, floor)) removed.Add(pier.Part);
        var previousMutation = Mutation;
        Mutation = true;
        try { foreach (var part in removed) ship.DestroyStatefulPart(part); }
        finally { Mutation = previousMutation; }
    }
    internal static void Removed(TrackedShipServer ship, StatefulPart part)
    {
        if (!PartRegistration.IsFoundation(part) || !Grounds.TryGetValue(ship.Id, out var ground)) return;
        ground.Piers.RemoveAll(pier => pier.Part == part.Id);
        if (ground.Piers.Count == 0)
        {
            var state = Geometry.Resolve(ground, ship.SpaceId);
            Grounds.Remove(ship.Id);
            RuntimeHooks.QueueRelease(ship, state);
        }
        else ground.Revision = checked(ground.Revision + 1);
        Persist(); FoundationNetwork.Publish();
    }
    internal static void Clear() { Grounds.Clear(); Initialized = false; Mutation = false; }
    internal static void RemoveFailed(uint ship) { Grounds.Remove(ship); Persist(); }
}
