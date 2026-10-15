using System;
using System.Linq;
using System.Reflection;
using FishNet.Serializing;
using Managers.Server.Services;
using Ships;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Physics;
using Xunit;

namespace StellarFoundations;

public sealed class NativeContractTests
{
    [Fact] public void InspectedAssemblyIdentitiesMatchInstalledGame() => Compatibility.Validate();

    [Theory]
    [InlineData("RpcLogic___PlaceFloatingPart_ServerRPC___2366367339", "GuardNativePlacement")]
    [InlineData("RpcLogic___RemoveFloatingPart_ServerRPC___1471205784", "GuardNativeRemoval")]
    [InlineData("RpcLogic___PickUpPart_ServerRPC___2228377996", "GuardPickup")]
    [InlineData("RpcLogic___ConfirmRepositionShip_ServerRPC___2616489143", "GuardReposition")]
    [InlineData("RpcLogic___StartRepositionShip_ServerRPC___328543758", "GuardStartReposition")]
    public void GeneratedRpcGuardsBindByPositionInsteadOfInventedDecompilerParameterNames(string target, string handler)
    {
        var original = typeof(ShipInteractionService).GetMethod(target, BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(original);
        var callback = typeof(RuntimeHooks).GetMethod(handler, BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var parameter in callback.GetParameters())
        {
            Assert.StartsWith("__", parameter.Name);
            var position = int.Parse(parameter.Name!.Substring(2));
            Assert.Equal(original.GetParameters()[position].ParameterType, parameter.ParameterType);
        }
    }

    [Fact] public void GroundingHooksTargetTheNativeRootBeforeForcesAndStateWriteback()
    {
        Assert.NotNull(typeof(ShipServerPhysics).GetMethod("UpdateRootPhysics"));
        Assert.NotNull(typeof(ShipServerPhysics).GetMethod("WritePhysicsState"));
        foreach (var pair in new[] { ("_shipRigidbody", typeof(ShipRigidbody)), ("_networkState", typeof(ShipState)) })
            Assert.Equal(pair.Item2, typeof(ShipServerPhysics).GetField(pair.Item1, BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType);
        Assert.NotNull(typeof(Ships.Services.ShipsServerTracker).GetMethod("LoadShip"));
        Assert.NotNull(typeof(TrackedShipServer).GetMethod("AddStatefulPart", new[] { typeof(Ships.Interface.Settings.PartSettings), typeof(IPartPlacement) }));
        var ensure = Compatibility.Method(typeof(ShipRigidbody), "EnsureRigidBodyExists", Type.EmptyTypes);
        Assert.Equal(typeof(void), ensure.ReturnType);
        Assert.False(ensure.IsStatic);
        Assert.Equal(typeof(UnityEngine.Rigidbody), Compatibility.Field(typeof(ShipRigidbody), "_rigidbody").FieldType);
    }

    [Fact] public void CommandWireRoundTripIsBoundedAndCannotSelectAnActingPlayer()
    {
        var command = new FoundationCommand { Version = 1, Sequence = uint.MaxValue, Action = CommandAction.Attach, Ship = 42, Space = 84, Revision = 12, Part = ushort.MaxValue,
            Position = new PositionD(9999999.123456789, -0.55, 71), Rotation = new RotationF(0, 0, 0, 1) };
        var writer = new Writer(); FoundationNetwork.WriteCommand(writer, command);
        Assert.Equal(61, writer.Length);
        var reader = new Reader(writer.GetArraySegment(), null);
        var read = FoundationNetwork.ReadCommand(reader);
        Assert.Equal(command.Position.X, read.Position.X); Assert.Equal(command.Part, read.Part); Assert.Equal(command.Revision, read.Revision); Assert.Equal(0, reader.Remaining);
        Assert.DoesNotContain(typeof(FoundationCommand).GetFields(), field => field.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact] public void TruncatedCommandsCannotReadPastTheirPacket() =>
        Assert.Throws<FormatException>(() => FoundationNetwork.ReadCommand(new Reader(new byte[60], null)));
}
