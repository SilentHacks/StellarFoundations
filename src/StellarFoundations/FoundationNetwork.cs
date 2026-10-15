using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Core.Services;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Serializing;
using FishNet.Transporting;
using HarmonyLib;
using Players;
using SDModKit.Game;
using Ships;
using Ships.Interface.Model.Gases;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using UnityEngine;
using WorldTracking.Interface.Services;

namespace StellarFoundations;

internal enum CommandAction : byte { Hello, Ready, New, Attach, Remove, Cancel }
internal struct FoundationCommand : IBroadcast
{
    public ushort Version;
    public uint Sequence;
    public CommandAction Action;
    public uint Ship, Space, Revision;
    public ushort Part;
    public PositionD Position;
    public RotationF Rotation;
}
internal struct FoundationReply : IBroadcast { public uint Sequence; public bool Success; public string Message; }
internal struct FoundationSnapshot : IBroadcast { public ushort Version, Index, Total; public uint Generation; public string Chunk; }

internal static class FoundationNetwork
{
    private sealed class Identity : IEqualityComparer<NetworkConnection>
    { public bool Equals(NetworkConnection? a, NetworkConnection? b) => ReferenceEquals(a, b); public int GetHashCode(NetworkConnection value) => RuntimeHelpers.GetHashCode(value); }
    private sealed class Session
    {
        internal readonly RequestWindow Requests = new RequestWindow();
        internal readonly Confirmation Hold = new Confirmation();
        internal double Created = Now, StillSince;
        internal string Selection = "";
        internal bool Hello, Ready;
        internal uint Generation;
        internal uint TargetShip;
        internal float TargetRadius;
    }
    private static readonly Dictionary<NetworkConnection, Session> Sessions = new Dictionary<NetworkConnection, Session>(new Identity());
    private static NetworkManager? _manager;
    private static MethodInfo _authenticate = null!;
    private static uint _sequence, _generation;
    private static bool _sentHello, _clientReady;
    private static double _started;
    private static uint _incomingGeneration;
    private static string[]? _chunks;
    private static int _received;
    // Keep the entire UTF-8 broadcast below a 512-byte MTU. The shipped Steam
    // host loopback path does not reliably reassemble pre-authentication Split packets.
    // GroundDocument contains ASCII property names, GUIDs and numeric values only.
    private const int ChunkCharacters = 384;
    internal static string ConnectionError = "";
    internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal static bool ClientReady => _clientReady;
    internal static void ObserveMotion(ShipState state, Ships.Physics.ShipRigidbody body)
    {
        // Sample pending conversions at every physics boundary, including gaps between packets.
        foreach (var session in Sessions.Values)
            if (session.TargetShip == state.Id && session.Selection.Length != 0
                && !Policy.Still(state.PhysicsState.Velocity.magnitude, body.AngularVelocity.magnitude, session.TargetRadius))
            { session.StillSince = Now; session.Hold.Reset(); }
    }
    internal static void StructureChanged(uint ship)
    {
        foreach (var session in Sessions.Values)
            if (session.TargetShip == ship) { session.Selection = ""; session.TargetShip = 0; session.Hold.Reset(); }
    }

    internal static void Initialize()
    {
        GenericWriter<FoundationCommand>.SetWrite(WriteCommand); GenericReader<FoundationCommand>.SetRead(ReadCommand);
        GenericWriter<FoundationReply>.SetWrite(WriteReply); GenericReader<FoundationReply>.SetRead(ReadReply);
        GenericWriter<FoundationSnapshot>.SetWrite(WriteSnapshot); GenericReader<FoundationSnapshot>.SetRead(ReadSnapshot);
        _authenticate = Compatibility.Method(typeof(ServerManager), "ClientAuthenticated");
        Compatibility.Exclusive(_authenticate);
        new HarmonyRuntime(Plugin.Owner).Patch(_authenticate, prefix: new HarmonyMethod(typeof(FoundationNetwork), nameof(BeforeAuthentication)));
    }
    private static bool BeforeAuthentication(NetworkConnection connection)
    {
        EnsureNetwork();
        if (!Sessions.TryGetValue(connection, out var session)) Sessions[connection] = session = new Session();
        // Delay the native authenticated/spawn path until the client has installed the grounding snapshot.
        return session.Ready;
    }
    internal static void Update()
    {
        EnsureNetwork();
        var manager = _manager;
        if (manager == null) return;
        if (manager.ClientManager.Started && !_sentHello)
        {
            _sentHello = true; _started = Now; ConnectionError = "";
            Send(new FoundationCommand { Action = CommandAction.Hello, Rotation = new RotationF(0, 0, 0, 1) });
        }
        if (!manager.ClientManager.Started) { _sentHello = _clientReady = false; _chunks = null; }
        if (_sentHello && !_clientReady && Now - _started > 20)
        { ConnectionError = "Foundations handshake timed out. Host and clients need the matching mod. Return to the menu and check the loader log."; manager.ClientManager.StopConnection(); }
        if (manager.IsServerStarted)
        {
            var expired = new List<NetworkConnection>();
            foreach (var pair in Sessions) if (!pair.Key.IsActive || (!pair.Value.Ready && Now - pair.Value.Created > 15)) expired.Add(pair.Key);
            foreach (var connection in expired) { if (connection.IsActive) connection.Disconnect(true); Sessions.Remove(connection); }
        }
    }
    private static void EnsureNetwork()
    {
        var manager = NetworkManager.Instances.Count == 0 ? null : InstanceFinder.NetworkManager;
        if (manager == null || ReferenceEquals(manager, _manager)) return;
        Unbind();
        CheckChannel<FoundationCommand>(manager.ServerManager); CheckChannel<FoundationReply>(manager.ClientManager); CheckChannel<FoundationSnapshot>(manager.ClientManager);
        _manager = manager;
        manager.ServerManager.RegisterBroadcast<FoundationCommand>(ReceiveCommand, requireAuthentication: false);
        manager.ClientManager.RegisterBroadcast<FoundationReply>(ReceiveReply);
        manager.ClientManager.RegisterBroadcast<FoundationSnapshot>(ReceiveSnapshot);
        manager.ServerManager.OnRemoteConnectionState += RemoteChanged;
    }
    private static void CheckChannel<T>(object manager)
    {
        var extensions = typeof(NetworkManager).Assembly.GetType("FishNet.Broadcast.Helping.BroadcastExtensions", true)!;
        var key = (ushort)Compatibility.Method(extensions, "GetKey").MakeGenericMethod(typeof(T)).Invoke(null, null)!;
        var handlers = (IDictionary)Compatibility.Field(manager.GetType(), "_broadcastHandlers").GetValue(manager)!;
        if (handlers[key] is object entry && (!entry.GetType().IsGenericType || entry.GetType().GetGenericArguments()[0] != typeof(T)))
            throw new InvalidOperationException("Foundations broadcast key conflicts with another mod.");
    }
    private static void RemoteChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
    { if (args.ConnectionState == RemoteConnectionState.Stopped) Sessions.Remove(connection); }
    private static void Unbind()
    {
        if (_manager != null)
        {
            _manager.ServerManager.UnregisterBroadcast<FoundationCommand>(ReceiveCommand);
            _manager.ClientManager.UnregisterBroadcast<FoundationReply>(ReceiveReply);
            _manager.ClientManager.UnregisterBroadcast<FoundationSnapshot>(ReceiveSnapshot);
            _manager.ServerManager.OnRemoteConnectionState -= RemoteChanged;
        }
        _manager = null; Sessions.Clear(); _sentHello = _clientReady = false; _chunks = null; ConnectionError = "";
    }
    internal static void WorldExit() => Unbind();
    internal static void Send(FoundationCommand request)
    {
        EnsureNetwork();
        if (_manager == null || !_manager.ClientManager.Started) return;
        request.Version = Policy.Protocol; request.Sequence = ++_sequence;
        _manager.ClientManager.Broadcast(request, Channel.Reliable);
    }
    private static void ReceiveCommand(NetworkConnection sender, FoundationCommand command, Channel channel)
    {
        if (_manager == null || !_manager.IsServerStarted || sender == null || !sender.IsActive) return;
        if (!Sessions.TryGetValue(sender, out var session)) Sessions[sender] = session = new Session();
        if (command.Version != Policy.Protocol) { sender.Disconnect(true); return; }
        if (command.Action == CommandAction.Hello)
        {
            if (session.Hello) return;
            session.Hello = true; session.Generation = ++_generation;
            SendSnapshot(sender, session.Generation); return;
        }
        if (command.Action == CommandAction.Ready)
        {
            if (!session.Hello || session.Generation != command.Revision) return;
            session.Ready = true;
            if (!sender.IsAuthenticated) _authenticate.Invoke(_manager.ServerManager, new object[] { sender });
            return;
        }
        if (!sender.IsAuthenticated || !session.Ready || !session.Requests.Accept(command.Sequence, Now, command.Action != CommandAction.Cancel)) return;
        try
        {
            var player = GameRefs.PlayersServer?.GetTrackedPlayerFromConnectionId(sender.ClientId);
            var state = ServerTrade.StateOf(player);
            if (player == null || state == null || !ReferenceEquals(state.Owner, sender)) { Reply(sender, command, false, "Your player is not ready."); return; }
            if (command.Action == CommandAction.Cancel) { session.Hold.Reset(); session.Selection = ""; session.TargetShip = 0; return; }
            if (FoundationSaving.Busy || Plugin.Failure.Length != 0) { session.Hold.Reset(); session.Selection = ""; session.TargetShip = 0; Reply(sender, command, false, "Wait for saving or resolve the Foundations error first."); return; }
            var message = Execute(player, session, command, out var success);
            Reply(sender, command, success, message);
        }
        catch (Exception error) { Plugin.Fault(error); Reply(sender, command, false, "Foundation action failed; check the loader log before continuing."); }
    }
    private static void Reply(NetworkConnection sender, FoundationCommand command, bool success, string message) =>
        _manager!.ServerManager.Broadcast(sender, new FoundationReply { Sequence = command.Sequence, Success = success, Message = message }, requireAuthenticated: false, channel: Channel.Reliable);
    private static void ReceiveReply(FoundationReply reply, Channel channel) => FoundationTool.Result(reply.Success, reply.Message);

    private static string Execute(TrackedPlayerServer player, Session session, FoundationCommand command, out bool success)
    {
        success = false;
        if (player.StateVal != Players.Interface.Model.PlayerStateType.Walking && player.StateVal != Players.Interface.Model.PlayerStateType.CheatFlying)
        { session.Hold.Reset(); return "Leave the seat or current interaction before building or recycling a foundation."; }
        if (command.Action == CommandAction.Remove)
        {
            var ship = ServiceLocator.GetService<ShipsServerTracker>().GetShip(command.Ship);
            var ground = FoundationRegistry.Get(command.Ship);
            if (ship == null || ground == null || ground.Revision != command.Revision || FoundationRegistry.Pier(ship.Id, command.Part) == null
                || !ship.TryGetStatefulPart(command.Part, out var part)) { session.Hold.Reset(); return "Foundation changed; aim again."; }
            if (!Geometry.InReach(player, ship.RelativeSpaceId, part.LocalPosition)) { session.Hold.Reset(); return "Foundation is out of recycler reach."; }
            session.TargetShip = ship.Id;
            if (ground.Piers.Count == 1 && !session.Hold.Observe("remove:" + ship.Id + ":" + part.Id, ground.Revision, Now, true, Policy.ReleaseHoldSeconds))
                return "LAST FOUNDATION: keep holding the recycler to release this structure. Active propulsion inputs will become effective.";
            var refund = player.IsUsingCheatFreeBuild ? null : part.Settings.BuildingCost;
            FoundationRegistry.Mutation = true;
            try { ship.RemoveStatefulPart(part.Id); }
            finally { FoundationRegistry.Mutation = false; }
            if (refund != null) player.AddItemsToInventory(refund);
            session.Hold.Reset(); success = true;
            return ground.Piers.Count == 0 ? "Foundation removed. Structure released." : "Foundation removed. Remaining supports keep the structure grounded.";
        }
        if (command.Action != CommandAction.New && command.Action != CommandAction.Attach) return "Unsupported foundation action.";
        if (!Geometry.Validate(command, player, out var placement, out var reason)) { session.Hold.Reset(); session.Selection = ""; return reason; }
        if (placement.Ship != null)
        {
            var ship = placement.Ship;
            if (FoundationRegistry.Revision(ship.Id) != command.Revision) { session.Hold.Reset(); return "Structure changed; confirm again."; }
            if (!FoundationRegistry.Grounded(ship.Id))
            {
                var body = ship.RigidbodyObject.GetComponent<Ships.Physics.ShipRigidbody>();
                var radius = ship.GetHullCoordBounds().Extents.magnitude;
                session.TargetShip = ship.Id; session.TargetRadius = radius;
                var eligible = Policy.Still(ship.PhysicsState.Velocity.magnitude, body.AngularVelocity.magnitude, radius);
                var selection = ship.Id + ":" + command.Position.X + ":" + command.Position.Y + ":" + command.Position.Z;
                if (!eligible || session.Selection != selection) { session.Selection = selection; session.StillSince = Now; session.Hold.Reset(); }
                if (!eligible || Now - session.StillSince < Policy.StillSeconds) return "Keep the structure still, including rotation.";
                if (!session.Hold.Observe(selection, command.Revision, Now, true, Policy.ConvertHoldSeconds)) return "Keep holding to ground this entire structure. Its position and tilt will be preserved.";
            }
        }
        PartRegistration.EnsureCost();
        var cost = PartRegistration.Settings.BuildingCost;
        if (!player.IsUsingCheatFreeBuild && !player.HasItemsInInventory(cost)) return "Not enough materials for the foundation.";
        FoundationTransactions.Place(placement);
        if (!player.IsUsingCheatFreeBuild) player.RemoveItemsFromInventory(cost);
        session.Hold.Reset(); session.Selection = ""; success = true;
        return "Foundation attached. Structure grounded.";
    }

    internal static void Publish()
    {
        RuntimeHooks.RefreshAppearance();
        if (_manager == null || !_manager.IsServerStarted) return;
        foreach (var entry in Sessions)
            if (entry.Value.Hello && entry.Key.IsActive)
            {
                var generation = ++_generation;
                if (!entry.Value.Ready) entry.Value.Generation = generation;
                SendSnapshot(entry.Key, generation);
            }
    }
    private static void SendSnapshot(NetworkConnection connection, uint generation)
    {
        var json = new GroundDocument { Structures = new List<GroundRecord>(FoundationRegistry.Records) }.Encode();
        var count = (ushort)((json.Length + ChunkCharacters - 1) / ChunkCharacters);
        for (ushort i = 0; i < count; i++)
            _manager!.ServerManager.Broadcast(connection, new FoundationSnapshot { Version = Policy.Protocol, Generation = generation, Index = i, Total = count,
                Chunk = json.Substring(i * ChunkCharacters, Math.Min(ChunkCharacters, json.Length - i * ChunkCharacters)) }, requireAuthenticated: false, channel: Channel.Reliable);
    }
    private static void ReceiveSnapshot(FoundationSnapshot snapshot, Channel channel)
    {
        if (snapshot.Version != Policy.Protocol || snapshot.Total == 0 || snapshot.Total > (Policy.MaximumMetadataCharacters / ChunkCharacters + 1)
            || snapshot.Index >= snapshot.Total || snapshot.Chunk == null || snapshot.Chunk.Length > ChunkCharacters) throw new InvalidOperationException("Invalid foundation snapshot.");
        if (_chunks == null || snapshot.Generation != _incomingGeneration)
        {
            if (_chunks != null && unchecked((int)(snapshot.Generation - _incomingGeneration)) <= 0) return;
            _chunks = new string[snapshot.Total]; _incomingGeneration = snapshot.Generation; _received = 0;
        }
        if (_chunks.Length != snapshot.Total) throw new InvalidOperationException("Inconsistent snapshot chunk count.");
        if (_chunks[snapshot.Index] != null) return;
        _chunks[snapshot.Index] = snapshot.Chunk; _received++;
        if (_received != _chunks.Length) return;
        var json = string.Concat(_chunks);
        var parsed = GroundDocument.Decode(json);
        if (!GameRefs.IsServer)
        {
            var old = new List<GroundRecord>(FoundationRegistry.Records);
            FoundationRegistry.Restore(parsed.Encode());
            foreach (var record in old) RuntimeHooks.Dirty(record.Ship, false);
            foreach (var record in parsed.Structures) RuntimeHooks.Dirty(record.Ship, false);
        }
        RuntimeHooks.RefreshAppearance();
        if (_manager?.ClientManager.Connection.IsAuthenticated != true) Send(new FoundationCommand { Action = CommandAction.Ready, Revision = snapshot.Generation });
        _clientReady = true;
    }

    internal static void WriteCommand(Writer writer, FoundationCommand value)
    {
        writer.WriteUInt16Unpacked(value.Version); writer.WriteUInt32Unpacked(value.Sequence); writer.WriteUInt8Unpacked((byte)value.Action);
        writer.WriteUInt32Unpacked(value.Ship); writer.WriteUInt32Unpacked(value.Space); writer.WriteUInt32Unpacked(value.Revision); writer.WriteUInt16Unpacked(value.Part);
        writer.WriteDouble(value.Position.X); writer.WriteDouble(value.Position.Y); writer.WriteDouble(value.Position.Z);
        writer.WriteSingle(value.Rotation.X); writer.WriteSingle(value.Rotation.Y); writer.WriteSingle(value.Rotation.Z); writer.WriteSingle(value.Rotation.W);
    }
    internal static FoundationCommand ReadCommand(Reader reader)
    {
        if (reader.Remaining < 61) throw new FormatException("Truncated foundation command.");
        var command = new FoundationCommand { Version = reader.ReadUInt16Unpacked(), Sequence = reader.ReadUInt32Unpacked(), Action = (CommandAction)reader.ReadUInt8Unpacked(),
            Ship = reader.ReadUInt32Unpacked(), Space = reader.ReadUInt32Unpacked(), Revision = reader.ReadUInt32Unpacked(), Part = reader.ReadUInt16Unpacked(),
            Position = new PositionD(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()), Rotation = new RotationF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) };
        if (command.Action > CommandAction.Cancel) throw new FormatException("Unknown foundation command.");
        return command;
    }
    private static void WriteReply(Writer writer, FoundationReply value) { writer.WriteUInt32Unpacked(value.Sequence); writer.WriteBoolean(value.Success); writer.WriteString(value.Message); }
    private static FoundationReply ReadReply(Reader reader) => new FoundationReply { Sequence = reader.ReadUInt32Unpacked(), Success = reader.ReadBoolean(), Message = reader.ReadStringAllocated() };
    private static void WriteSnapshot(Writer writer, FoundationSnapshot value) { writer.WriteUInt16Unpacked(value.Version); writer.WriteUInt16Unpacked(value.Index); writer.WriteUInt16Unpacked(value.Total); writer.WriteUInt32Unpacked(value.Generation); writer.WriteString(value.Chunk); }
    private static FoundationSnapshot ReadSnapshot(Reader reader) => new FoundationSnapshot { Version = reader.ReadUInt16Unpacked(), Index = reader.ReadUInt16Unpacked(), Total = reader.ReadUInt16Unpacked(), Generation = reader.ReadUInt32Unpacked(), Chunk = reader.ReadStringAllocated() };
}
