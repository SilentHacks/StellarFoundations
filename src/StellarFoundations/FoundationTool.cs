using System;
using System.Reflection;
using Core.Services;
using HarmonyLib;
using SDModKit.Game;
using Players.Interface.Services;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Tools.Build;
using Tools.Build.Model;
using Tools.Build.SnapDetectors.Model;
using Tools.Common;
using Tools.Common.Hover;
using Tools.Interface.Model;
using Tools.Interface.Services;
using Tools.Recycler;
using UnityEngine;
using WorldProvision.Interface.Services;
using WorldTracking.Interface.Services;
using WorldTracking.Interface.Values;

namespace StellarFoundations;

internal static class FoundationTool
{
    internal static string Status = "";
    private static FieldInfo _preview = null!, _hoverCollider = null!;
    private static bool _holding, _awaiting, _removing, _valid;
    private static float _height = 0.75f, _yaw;
    private static double _nextSend, _observed;
    private static FoundationCommand _candidate;
    private static GameObject? _ghost;
    private static Transform _cap = null!, _column = null!, _foot = null!;
    private static Material? _material;
    private static LineRenderer? _outline;
    private static uint _outlinedShip;
    private static readonly Vector3[] OutlineCorners = new Vector3[8], OutlineLine = new Vector3[16];
    private static readonly int[] OutlineOrder = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
    private static double _nextPreview;
    private static bool Selected => GameRefs.NetworkManagerCount > 0 && GameRefs.InWorld && GameRefs.Service<ILocalSelectedPartProvider>()?.CurrentSelectedPartId == Policy.PartSettingsId;

    internal static void Install(HarmonyRuntime harmony)
    {
        _preview = Compatibility.Field(typeof(BuildToolLocalClient), "_partPreview");
        _hoverCollider = Compatibility.Field(typeof(FloatingPartHover), "_partCollider");
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "UpdateChecks"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Check)));
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "PrimaryActionBegin"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Begin)));
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "PrimaryActionEnd"), postfix: new HarmonyMethod(typeof(FoundationTool), nameof(End)));
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "DisableTool"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Disable)));
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "ExecuteScrollAction"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Scroll)));
        harmony.Patch(Compatibility.Method(typeof(FloatingPartHover), "RemoveObject"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Recycle)));
        harmony.Patch(Compatibility.Method(typeof(RecyclerTool), "PrimaryActionEnd"), postfix: new HarmonyMethod(typeof(FoundationTool), nameof(EndRecycling)));
        harmony.Patch(Compatibility.Method(typeof(PartPreviewDetection), "CalculateSnapDetectionUpdate"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(FirstFloor)));
    }
    private static bool Check(BuildToolLocalClient __instance, CameraPhysicsData cameraPhysics)
    {
        if (!Selected) { Hide(); return true; }
        ((PartPreview)_preview.GetValue(__instance)!).HidePreview();
        _observed = FoundationNetwork.Now;
        if (_observed >= _nextPreview) { _nextPreview = _observed + 0.05; Compute(cameraPhysics); }
        return false;
    }
    private static bool FirstFloor(SnapDetectionState state, CameraPhysicsData cameraPhysics, ref SnapDetectionResults __result)
    {
        if (FoundationRegistry.Count == 0 || !(state.partSettings is HullPartSettings floor) || floor.IsStateful || floor.snappingStyle != SnappingStyle.Floor) return true;
        if (!ClientScene.PhysicsScene.Raycast(cameraPhysics.CameraPos, cameraPhysics.Direction, out var hit, 5f, RaycastMasks.BuildDetectionLayer, QueryTriggerInteraction.Ignore)) return true;
        var proxy = hit.collider.GetComponentInParent<IShipPartPlacements>();
        var identity = hit.collider.GetComponentInParent<IShipFloatingPartId>();
        if (proxy == null || identity == null || FoundationRegistry.Pier(proxy.ShipId, identity.Id) is not PierRecord pier) return true;
        var local = proxy.ConvertHitToLocalHit(hit);
        var delta = local.Pos - Geometry.Vector(pier.LocalCap);
        if (local.Normal.y < 0.7f || Math.Abs(delta.x) > 0.46f || Math.Abs(delta.z) > 0.46f || Math.Abs(delta.y) > 0.05f) return true;
        var coord = Vector3Int.RoundToInt(Geometry.Vector(pier.LocalCap) + Vector3.up * 0.55f);
        if (!proxy.IsHullPartAvailable(coord, VoxelPartType.Floor)) return true;
        // Native hull meshes face their forward axis; floors face up, with their up axis along the grid.
        var localRotation = proxy.ToLocalRotation(state.partRotation);
        var forward = PartOrientationHelper.SnapVerticalOrientation(localRotation * Vector3.forward) ?? PartOrientation.Up;
        var up = PartOrientationHelper.SnapFloorOrientation(localRotation * Vector3.up) ?? PartOrientation.Forward;
        var placement = new HullPartPlacement(coord, VoxelPartType.Floor, forward, up);
        __result = new SnapDetectionResults { IsSnapped = true, shipPartsProxy = proxy, placement = placement, pos = proxy.ToWorldPos(placement) };
        return false;
    }
    private static bool Begin()
    {
        if (!Selected) return true;
        _holding = true; _awaiting = false; _nextSend = 0;
        return false;
    }
    private static void End() { if (_holding) Cancel(); _holding = false; }
    private static void Disable() { Cancel(); Hide(); }
    private static bool Scroll(float scroll)
    {
        if (!Selected) return true;
        if (Plugin.Mod.HeightModifier.Held) _height = Mathf.Clamp(_height + Math.Sign(scroll) * 0.25f, 0.25f, Policy.MaximumLength);
        else _yaw = Mathf.Repeat(_yaw + Math.Sign(scroll) * 45f, 360f);
        Cancel(); return false;
    }
    private static void Cancel()
    {
        if (_holding || _removing) FoundationNetwork.Send(new FoundationCommand { Action = CommandAction.Cancel });
        _holding = _awaiting = _removing = false;
    }
    private static bool Recycle(FloatingPartHover __instance)
    {
        var collider = _hoverCollider.GetValue(__instance)! as Collider;
        if (collider == null) return true;
        var helper = collider.GetComponentInParent<IShipPartHelper>();
        var identity = collider.GetComponentInParent<IShipFloatingPartId>();
        if (helper == null || identity == null || FoundationRegistry.Pier(helper.ShipId, identity.Id) == null) return true;
        _removing = true;
        Status = "Hold recycler to remove this foundation. Removing the final support releases the structure.";
        FoundationNetwork.Send(new FoundationCommand { Action = CommandAction.Remove, Ship = helper.ShipId, Part = identity.Id, Revision = FoundationRegistry.Revision(helper.ShipId) });
        return false;
    }
    private static void EndRecycling() { if (_removing) Cancel(); }

    private static void Compute(CameraPhysicsData camera)
    {
        _valid = false;
        if (!FoundationNetwork.ClientReady || Plugin.Failure.Length != 0) { Hide(); return; }
        if (!ClientScene.PhysicsScene.Raycast(camera.CameraPos, camera.Direction, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore))
        { Status = "Aim at planetary terrain or an existing floor."; Hide(); return; }
        var environment = GameRefs.Service<IWorldEnvironmentClientProvider>();
        var transpose = GameRefs.Service<IWorldTransposeClient>();
        if (environment == null || transpose == null) { Hide(); return; }
        Vector3 cap; Quaternion rotation;
        var request = new FoundationCommand { Rotation = new RotationF(0, 0, 0, 1) };
        var proxy = hit.collider.GetComponentInParent<IShipPartPlacements>();
        if (proxy != null && hit.collider.CompareTag("HullFloor"))
        {
            var local = proxy.ConvertHitToLocalHit(hit);
            var orientation = ShipPartsTagDetection.GuessPartOrientationFromTag(hit.collider);
            var coord = orientation.HasValue ? RelativeDirectionsMap.Map[orientation.Value].GetCoord(local.Pos) : Vector3Int.RoundToInt(local.Pos);
            var capLocal = PartPositionHelper.GetHullPartInteriorPos(new HullPartPosition(coord, VoxelPartType.Floor)) - Vector3.up * 0.05f;
            cap = proxy.ToWorldPos(new FloatingPartPlacement(capLocal, PartOrientation.Forward));
            rotation = proxy.ToWorldRotation(Quaternion.identity);
            request.Action = CommandAction.Attach; request.Ship = proxy.ShipId; request.Revision = FoundationRegistry.Revision(proxy.ShipId);
            request.Position = Geometry.Position(capLocal);
            Outline(proxy.ShipId, proxy);
        }
        else if (hit.collider.CompareTag("Planet"))
        {
            var gravity = environment.GetGravityAtPosition(ClientOrigin.LocalPhysicsSpaceId, hit.point);
            if (gravity.sqrMagnitude < 0.0001f) { Status = "Planetary gravity is required."; Hide(); return; }
            var up = -gravity.normalized;
            cap = hit.point + up * _height;
            rotation = Geometry.Level(gravity, camera.Direction) * Quaternion.AngleAxis(_yaw, Vector3.up);
            request.Action = CommandAction.New; request.Space = ClientOrigin.LocalPhysicsSpaceId;
            request.Position = Geometry.Position(cap); request.Rotation = Geometry.Rotation(rotation);
            if (_outline != null) _outline.enabled = false;
        }
        else { Status = "Foundations attach to planetary terrain beneath ordinary floors."; Hide(); return; }
        var localGravity = environment.GetGravityAtPosition(ClientOrigin.LocalPhysicsSpaceId, cap);
        var upVector = -localGravity.normalized;
        var valid = Geometry.Footprint(ClientScene.PhysicsScene, cap, upVector, out var foot, out var normal, out var reason);
        EnsureGhost();
        _ghost!.SetActive(true);
        var visual = transpose.ToTransformOperation(ClientOrigin.LocalPhysicsSpaceId, ClientOrigin.LocalVisualSpaceId);
        var visualCap = visual.MultiplyPoint(cap); var visualFoot = visual.MultiplyPoint(foot);
        var visualRotation = (Quaternion)visual.Rotation * rotation;
        _cap.position = visualCap - visualRotation * Vector3.up * 0.1f; _cap.rotation = visualRotation; _cap.localScale = new Vector3(0.9f, 0.2f, 0.9f);
        if (valid)
        {
            var direction = visualFoot - visualCap;
            _column.gameObject.SetActive(true); _foot.gameObject.SetActive(true);
            _column.position = (visualCap + visualFoot) * 0.5f; _column.rotation = Quaternion.FromToRotation(Vector3.up, direction); _column.localScale = new Vector3(0.18f, direction.magnitude, 0.18f);
            _foot.position = visualFoot; _foot.rotation = Quaternion.FromToRotation(Vector3.up, (Quaternion)visual.Rotation * normal); _foot.localScale = new Vector3(0.65f, 0.12f, 0.65f);
            if (!_holding) Status = request.Action == CommandAction.New ? "Place level foundation. Ordinary floors snap above its mounting plate." : "Hold build to ground the outlined structure. Position and tilt stay unchanged.";
        }
        else { _column.gameObject.SetActive(false); _foot.gameObject.SetActive(false); Status = reason; }
        _material!.color = valid ? new Color(0.2f, 0.9f, 0.65f, 0.45f) : new Color(1, 0.25f, 0.2f, 0.45f);
        _candidate = request; _valid = valid;
    }

    internal static void Update()
    {
        if (FoundationNetwork.Now - _observed > 0.3 || !Selected) { if (_holding) Cancel(); Hide(); return; }
        if (!_holding || !_valid || _awaiting || FoundationNetwork.Now < _nextSend) return;
        _nextSend = FoundationNetwork.Now + 0.2;
        _awaiting = _candidate.Action == CommandAction.New;
        FoundationNetwork.Send(_candidate);
    }
    internal static void Result(bool success, string message)
    {
        Status = message; _awaiting = false;
        if (success) { _holding = false; _removing = false; }
    }
    private static void EnsureGhost()
    {
        if (_ghost != null) return;
        _ghost = new GameObject("Foundation placement preview");
        _material = new Material(Shader.Find("Sprites/Default"));
        _cap = Primitive("Mounting plate"); _column = Primitive("Telescoping column"); _foot = Primitive("Terrain foot");
    }
    private static Transform Primitive(string name)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = name; item.layer = LayerMask.NameToLayer("Ignore Raycast");
        item.transform.SetParent(_ghost!.transform, false); UnityEngine.Object.Destroy(item.GetComponent<Collider>());
        item.GetComponent<Renderer>().sharedMaterial = _material; return item.transform;
    }
    private static void Outline(uint ship, IShipPartPlacements proxy)
    {
        if (_outline == null)
        {
            var root = new GameObject("Foundation structure outline"); _outline = root.AddComponent<LineRenderer>();
            _outline.sharedMaterial = new Material(Shader.Find("Sprites/Default")); _outline.startColor = _outline.endColor = new Color(1, 0.75f, 0.2f);
            _outline.startWidth = _outline.endWidth = 0.035f; _outline.useWorldSpace = true;
        }
        _outline.enabled = true;
        var client = GameRefs.Service<Ships.Services.ShipsClientTracker>();
        if (client == null || !client.TryGetTrackedShip(ship, out var tracked)) return;
        var bounds = tracked.GetHullCoordBounds();
        var center = bounds.Center; var extents = bounds.Extents + Vector3.one * 0.1f;
        var transform = ServiceLocator.GetService<IWorldTransposeClient>().ToTransformOperation(ClientOrigin.LocalPhysicsSpaceId, ClientOrigin.LocalVisualSpaceId);
        for (var i = 0; i < 8; i++)
        {
            var local = center + Vector3.Scale(extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            OutlineCorners[i] = transform.MultiplyPoint(proxy.ToWorldPos(new FloatingPartPlacement(local, PartOrientation.Forward)));
        }
        for (var i = 0; i < OutlineLine.Length; i++) OutlineLine[i] = OutlineCorners[OutlineOrder[i]];
        _outline.positionCount = OutlineLine.Length; _outline.SetPositions(OutlineLine); _outlinedShip = ship;
    }
    private static void Hide() { if (_ghost != null) _ghost.SetActive(false); if (_outline != null) _outline.enabled = false; }
    internal static void Clear()
    {
        _holding = _awaiting = _removing = _valid = false; _outlinedShip = 0; _observed = _nextPreview = 0;
        if (_ghost != null) UnityEngine.Object.Destroy(_ghost); if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_outline != null) { UnityEngine.Object.Destroy(_outline.sharedMaterial); UnityEngine.Object.Destroy(_outline.gameObject); }
        _ghost = null; _material = null; _outline = null; Status = "";
    }
    internal static void Draw()
    {
        if (!Plugin.Ready && Plugin.Failure.Length == 0) return;
        if (FoundationNetwork.ConnectionError.Length != 0) GUI.Box(new Rect(20, 120, 800, 90), FoundationNetwork.ConnectionError);
        if (Plugin.Failure.Length != 0)
        {
            GUI.Box(new Rect(20, 20, 800, 90), "Stellar Foundations stopped: " + Plugin.Failure + "\nResolve the error before loading, saving or continuing a foundation world."); return;
        }
        if (!Selected && !_removing) return;
        GUILayout.BeginArea(new Rect(Screen.width / 2f - 380, Mathf.Max(20, Screen.height - 270), 760, 130), GUI.skin.box);
        GUILayout.Label("Foundation Pier   •   " + Plugin.Mod.HeightModifier.Display + " + scroll: height   •   scroll: heading");
        GUILayout.Label(Status);
        if (_outlinedShip != 0 && _outline != null && _outline.enabled) GUILayout.Label("Target structure: " + _outlinedShip + " — entire outlined entity");
        GUILayout.EndArea();
    }
}
