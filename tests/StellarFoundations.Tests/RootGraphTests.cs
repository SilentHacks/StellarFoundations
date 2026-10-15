using System;
using System.Collections;
using System.Reflection;
using Ships.Interface.Model.State;
using Ships.Joints;
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
