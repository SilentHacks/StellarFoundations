using System;
using System.Collections;
using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using Managers.Interface.Model;
using Ships;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Settings;
using Ships.Interface.Model.State;
using Ships.Joints;
using UnityEngine;
using Xunit;

namespace StellarFoundations;

public class ShipReadProxy : DispatchProxy
{
    public uint Identity;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method!.Name == "get_Id" ? Identity : throw new NotSupportedException(method.Name);
    public static IShipStateRead Create(uint id)
    {
        var value = Create<IShipStateRead, ShipReadProxy>();
        ((ShipReadProxy)(object)value).Identity = id;
        return value;
    }
}

public sealed class RootGraphTests
{
    [Fact]
    public void ConvertedAnchorRefusesAnOrphanedSavedFloorAndProtectsTheExactTileFromRecycling()
    {
        var document = PolicyTests.Document();
        var pier = document.Structures[0].Piers[0];
        pier.LocalCap = pier.LocalFoot = new PositionD(2, 2.45, 4);
        pier.Length = pier.Clearance = 0; pier.Kind = AttachmentKind.Contact;
        pier.RequiresFloor = pier.HasCollar = true;
        FoundationRegistry.Restore(document.Encode());
        try
        {
            // Only managed settings fields are used; no native engine objects or detours.
            var settings = (PartSettings)RuntimeHelpers.GetUninitializedObject(typeof(PartSettings));
            settings.id = Policy.PartSettingsId;
            var snapshot = new ShipStateSnapshot { Id = 42, HullParts = Array.Empty<HullPart>(), StatefulParts = new[] {
                new StatefulPart(pier.Part, settings, new FloatingPartPlacement(Geometry.Vector(pier.LocalCap), PartOrientation.Forward)) } };
            Assert.Throws<InvalidDataException>(() => FoundationRegistry.ValidateSnapshot(snapshot));
            var floor = Geometry.FloorAtCap(Geometry.Vector(pier.LocalCap));
            snapshot.HullParts = new[] { new HullPart(null!, new HullPartPlacement(floor, PartOrientation.Up, PartOrientation.Forward), default) };
            FoundationRegistry.ValidateSnapshot(snapshot);
            var guard = typeof(RuntimeHooks).GetMethod("GuardHullRemoval", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.False((bool)guard.Invoke(null, new object[] { new RemoveHullPartNetworkRequest { ShipId = 42, Position = floor } })!);
            Assert.True((bool)guard.Invoke(null, new object[] { new RemoveHullPartNetworkRequest { ShipId = 99, Position = floor } })!);
            var otherFloor = new HullPartPosition(new Vector3Int(2, 4, 4), VoxelPartType.Floor);
            Assert.True((bool)guard.Invoke(null, new object[] { new RemoveHullPartNetworkRequest { ShipId = 42, Position = otherFloor } })!);
        }
        finally { FoundationRegistry.Clear(); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativeGraphPrefersGroundedNodeAndRefusesCompetingGrounds(bool secondGround)
    {
        var document = PolicyTests.Document();
        if (secondGround) { var other = PolicyTests.Document().Structures[0]; other.Ship = 99; document.Structures.Add(other); }
        FoundationRegistry.Restore(document.Encode());
        try
        {
            RuntimeHooks.BindGraph();
            var graphType = typeof(ShipsJointsTreeTracker).GetNestedType("ShipsGraph", BindingFlags.NonPublic)!;
            var nodeType = typeof(ShipsJointsTreeTracker).GetNestedType("ShipGraphNode", BindingFlags.NonPublic)!;
            var graph = Activator.CreateInstance(graphType, true)!;
            var first = Activator.CreateInstance(nodeType, ShipReadProxy.Create(42))!;
            var visitor = Activator.CreateInstance(nodeType, ShipReadProxy.Create(99))!;
            var nodes = (IDictionary)graphType.GetProperty("Nodes")!.GetValue(graph)!;
            nodes.Add(42u, first); nodes.Add(99u, visitor);
            var parent = graphType.GetProperty("ParentNode")!; parent.SetValue(graph, visitor);
            if (secondGround) Assert.Throws<InvalidOperationException>(() => RuntimeHooks.SelectGroundedRoot(graph));
            else { RuntimeHooks.SelectGroundedRoot(graph); Assert.Same(first, parent.GetValue(graph)); }
        }
        finally { FoundationRegistry.Clear(); }
    }
}
