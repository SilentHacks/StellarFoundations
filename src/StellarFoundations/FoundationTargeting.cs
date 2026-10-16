using System;
using HarmonyLib;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Tools.Common;
using Tools.Common.Hover;
using Tools.Recycler;
using UnityEngine;
using WorldTracking.Interface.Values;

namespace StellarFoundations;

// Resolves only the first normal recycler hit. A floor can expose the mounting
// spot of its own foundation; walls and neighbouring tiles never reveal anchors.
internal static class FoundationTargeting
{
    internal static void Install(HarmonyRuntime harmony) => harmony.Patch(
        Compatibility.Method(typeof(RecyclerTool), "UpdateHoveredObject"),
        prefix: new HarmonyMethod(typeof(FoundationTargeting), nameof(Hover)));

    private static bool Hover(RecyclerTool __instance, CameraPhysicsData cameraPhysics, ref IHoveredObject? ___hoveredObject,
        float ___rayCastDistance, int ____layerMask)
    {
        if (FoundationRegistry.Count == 0) return true;
        IHoveredObject? next = null;
        if (ClientScene.PhysicsScene.Raycast(cameraPhysics.CameraPos, cameraPhysics.Direction,
            out var hit, ___rayCastDistance, ____layerMask, QueryTriggerInteraction.Ignore))
        {
            if (Resolve(hit, out var targeter, out var pier))
            {
                if (___hoveredObject is FoundationHover previous && previous.Ship == targeter.ShipId && previous.Part == pier.Part
                    && ReferenceEquals(previous.Targeter, targeter)) return false;
                next = new FoundationHover(targeter, pier.Part);
            }
            else next = IHoveredObject.BuildHoveredObject(hit, cameraPhysics.Direction);
        }
        if (!Equals(___hoveredObject, next))
        {
            ___hoveredObject?.ClearHighlight(); next?.EnableRecycleHighlight();
        }
        ___hoveredObject = next;
        return false;
    }

    internal static bool Resolve(RaycastHit hit, out IShipPartTargeter targeter, out PierRecord pier)
    {
        targeter = hit.collider.GetComponentInParent<IShipPartTargeter>(); pier = null!;
        if (targeter == null) return false;
        var identity = hit.collider.GetComponentInParent<IShipFloatingPartId>();
        if (identity != null && FoundationRegistry.Pier(targeter.ShipId, identity.Id) is PierRecord direct)
        { pier = direct; return true; }
        if (!hit.collider.CompareTag("HullFloor")) return false;
        var ground = FoundationRegistry.Get(targeter.ShipId); if (ground == null) return false;
        var local = targeter.ConvertHitToLocalHit(hit);
        var orientation = ShipPartsTagDetection.GuessPartOrientationFromTag(hit.collider);
        var coord = orientation.HasValue ? RelativeDirectionsMap.Map[orientation.Value].GetCoord(local.Pos) : Vector3Int.RoundToInt(local.Pos);
        var floor = new HullPartPosition(coord, VoxelPartType.Floor);
        foreach (var item in ground.Piers)
        {
            var cap = Geometry.Vector(item.LocalCap);
            if (!Geometry.FloorAtCap(cap).Equals(floor)) continue;
            var offset = local.Pos - cap;
            if (!RemovalPolicy.InMountingSpot(offset.x, offset.z)) continue;
            pier = item; return true;
        }
        return false;
    }
}

internal sealed class FoundationHover : IHoveredObject, IEquatable<IHoveredObject>
{
    internal readonly IShipPartTargeter Targeter;
    internal uint Ship => Targeter.ShipId;
    internal readonly ushort Part;
    internal FoundationHover(IShipPartTargeter targeter, ushort part) { Targeter = targeter; Part = part; }
    public void EnableRecycleHighlight()
    { if (!AnchorModel.Highlight(Ship, Part, true)) Targeter.HighlightFloatingPart(Part); }
    public void ClearHighlight()
    {
        if (Targeter is Component component && component == null) return;
        if (!AnchorModel.Highlight(Ship, Part, false)) Targeter.RemoveFloatingPartHighlight(Part);
    }
    public void RemoveObject() => FoundationTool.BeginRemoval(Ship, Part);
    public bool Equals(IHoveredObject? other) => other is FoundationHover value && value.Ship == Ship && value.Part == Part;
    public override bool Equals(object? other) => other is FoundationHover value && value.Ship == Ship && value.Part == Part;
    public override int GetHashCode() => unchecked((int)Ship * 397 ^ Part);
}
