using System;
using Core.Services;
using Ships;
using Ships.Interface.Model.Gases;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using UnityEngine;
using WorldTracking.Interface.Services;

namespace StellarFoundations;

internal static class FoundationTransactions
{
    internal static void Place(CheckedPlacement placement)
    {
        var ships = ServiceLocator.GetService<ShipsServerTracker>();
        if (placement.Ship == null)
        {
            var id = ServiceLocator.GetService<IWorldObjectsIdsServerProvider>().CreateUniqueIdForObject();
            var rootPosition = placement.Cap + placement.Rotation * Vector3.up * 0.55f;
            var ground = Geometry.Ground(id, placement.Space, rootPosition, placement.Rotation);
            ground.Piers.Add(placement.Record(0));
            FoundationRegistry.Set(ground);
            try
            {
                ships.LoadShip(new ShipStateSnapshot {
                    Id = id, RelativeSpaceId = ServiceLocator.GetService<ISpacesServerProvider>().GetOrCreateShipRelativeSpace(id), SpaceId = placement.Space,
                    Position = rootPosition, Rotation = placement.Rotation, Velocity = Vector3.zero,
                    HullParts = Array.Empty<HullPart>(), RoomGasState = Array.Empty<RoomGasState>(),
                    StatefulParts = new[] { new StatefulPart(0, PartRegistration.Settings, new FloatingPartPlacement(Vector3.down * 0.55f, PartOrientation.Forward)) }
                });
            }
            catch { FoundationRegistry.RemoveFailed(id); var ship = ships.GetShip(id); if (ship != null) ships.RemoveShip(ship.State); throw; }
        }
        else
        {
            var ship = placement.Ship;
            var old = FoundationRegistry.Get(ship.Id);
            var record = old == null ? Geometry.Ground(ship.Id, ship.SpaceId, ship.Position, ship.Rotation)
                : GroundDocument.Decode(new GroundDocument { Structures = new System.Collections.Generic.List<GroundRecord> { old } }.Encode()).Structures[0];
            if (record.Piers.Count >= Policy.MaximumPiersPerStructure) throw new InvalidOperationException("Maximum foundations per structure reached.");
            var next = (ushort)Compatibility.Field(typeof(TrackedShipServer), "_nextStatefulPartId").GetValue(ship)!;
            if (ship.TryGetStatefulPart(next, out _)) throw new InvalidOperationException("Native part ID space exhausted; foundation was not placed.");
            record.Piers.Add(placement.Record(next));
            if (old != null) record.Revision = checked(record.Revision + 1);
            FoundationRegistry.Set(record);
            FoundationRegistry.Mutation = true;
            try
            {
                if (ship.AddStatefulPart(PartRegistration.Settings, new FloatingPartPlacement(placement.Cap, PartOrientation.Forward)) != next)
                    throw new InvalidOperationException("Native part identity changed during foundation placement.");
            }
            catch
            {
                if (ship.TryGetStatefulPart(next, out _)) ship.RemoveStatefulPart(next);
                if (old != null) FoundationRegistry.Set(old); else FoundationRegistry.RemoveFailed(ship.Id);
                throw;
            }
            finally { FoundationRegistry.Mutation = false; }
            RuntimeHooks.Dirty(ship.Id, true);
        }
        FoundationRegistry.Persist(); FoundationNetwork.Publish();
    }
}
