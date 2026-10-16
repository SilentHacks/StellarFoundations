using System;
using System.Reflection;
using Audio.Interface;
using Core.Services;
using HarmonyLib;
using SDModKit.Game;
using SDModKit.Content;
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
    private static FieldInfo _preview = null!, _hoverCollider = null!, _hoverFloor = null!, _hoverShip = null!;
    private static FieldInfo _recyclerHover = null!, _recyclerHolding = null!;
    private static bool _holding, _awaiting, _removing, _valid;
    private static float _height = 0.75f, _yaw;
    private static double _nextSend, _observed;
    private static FoundationCommand _candidate;
    private static FoundationCommand _removal;
    private static double _nextRemoval;
    private static RecyclerTool? _activeRecycler;
    private static GameObject? _ghost;
    private static readonly PierRecord PreviewRecord = new PierRecord { HasCollar = true, Kind = AttachmentKind.Contact, LocalFootNormal = new PositionD(0, 1, 0) };
    private static Renderer[] _ghostRenderers = Array.Empty<Renderer>();
    private static Material? _material;
    private static LineRenderer? _outline;
    private static uint _outlinedShip;
    private static readonly Vector3[] OutlineCorners = new Vector3[8], OutlineLine = new Vector3[16];
    private static readonly int[] OutlineOrder = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
    private static double _nextPreview;
    private static readonly FeedbackWindow Feedback = new FeedbackWindow();
    private static readonly HostProgress Progress = new HostProgress();
    private static FoundationPrompt _previewPrompt, _replyPrompt, _recyclerPrompt;
    private static double _toastUntil, _recyclerObserved;
    private static BuildToolLocalClient? _buildTool;
    private static bool _costVisible;
    private static Transform _floorMarker = null!, _contactMarker = null!, _obstructionMarker = null!;
    private static Vector3 _outlineCenter, _outlineExtents;
    private static GameObject? _markers;
    private static readonly System.Collections.Generic.List<Transform> MarkerPool = new System.Collections.Generic.List<Transform>();
    private static double _nextMarkers;
    private static IShipPartPlacements? _targetProxy;
    private static Material? _markerMaterial;
    private static FoundationHover? _recyclerTarget;
    private static bool _releaseRequired;
    private static uint _convertingShip;
    private static bool Selected => GameRefs.NetworkManagerCount > 0 && GameRefs.InWorld && ToolRefs.BuildActive
        && GameRefs.Service<ILocalSelectedPartProvider>()?.CurrentSelectedPartId == Policy.PartSettingsId;
    internal static bool IsSelected => Selected;
    internal static bool RecyclerContext => ToolRefs.RecyclerActive && (_removing || (_recyclerTarget != null && FoundationNetwork.Now - _recyclerObserved < 0.15));
    internal static bool OwnControls => Selected || RecyclerContext;
    internal static bool PlacingNew => Selected && _candidate.Action == CommandAction.New;
    private static bool Converting => _previewPrompt.Phase == FoundationPhase.Anchor || _holding && _convertingShip != 0;
    internal static string ActionHint => RecyclerContext ? (_releaseRequired ? "Hold to remove foundation" : "Recycle foundation")
        : Converting ? "Hold to anchor structure"
        : _previewPrompt.Phase == FoundationPhase.Inspect ? "" : PlacingNew ? "Place foundation" : "Add foundation";
    internal static string HoldReason => RecyclerContext ? (_releaseRequired ? "Last support. Releases the structure." : "")
        : Selected && Converting ? "Stops movement and propulsion." : "";
    internal static string HintMode => RecyclerContext ? "recycle:" + ActionHint + HoldReason
        : Selected ? (PlacingNew ? "new:" + Plugin.Mod.HeightModifier.Display : "attach:" + ActionHint + HoldReason) : "";

    internal static void Install(HarmonyRuntime harmony)
    {
        FoundationTargeting.Install(harmony);
        _preview = Compatibility.Field(typeof(BuildToolLocalClient), "_partPreview");
        _hoverCollider = Compatibility.Field(typeof(FloatingPartHover), "_partCollider");
        _hoverFloor = Compatibility.Field(typeof(HullHover), "_hullPartPosition");
        _hoverShip = Compatibility.Field(typeof(HullHover), "_currentShip");
        _recyclerHover = Compatibility.Field(typeof(RecyclerTool), "hoveredObject");
        _recyclerHolding = Compatibility.Field(typeof(RecyclerTool), "_isRecycling");
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "UpdateChecks"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Check)));
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "PrimaryActionBegin"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Begin)) { priority = Priority.First });
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "PrimaryActionEnd"), postfix: new HarmonyMethod(typeof(FoundationTool), nameof(End)));
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "DisableTool"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Disable)));
        harmony.Patch(Compatibility.Method(typeof(BuildToolLocalClient), "ExecuteScrollAction"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Scroll)));
        harmony.Patch(Compatibility.Method(typeof(FloatingPartHover), "RemoveObject"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(Recycle)));
        harmony.Patch(Compatibility.Method(typeof(HullHover), "RemoveObject"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(RecycleFloor)));
        harmony.Patch(Compatibility.Method(typeof(RecyclerTool), "PrimaryActionBegin"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(BeginRecycling)));
        harmony.Patch(Compatibility.Method(typeof(RecyclerTool), "PrimaryActionEnd"), postfix: new HarmonyMethod(typeof(FoundationTool), nameof(EndRecycling)));
        harmony.Patch(Compatibility.Method(typeof(RecyclerTool), "DisableTool"), postfix: new HarmonyMethod(typeof(FoundationTool), nameof(EndRecycling)));
        harmony.Patch(Compatibility.Method(typeof(RecyclerTool), "UpdateChecks"), postfix: new HarmonyMethod(typeof(FoundationTool), nameof(UpdateRecycler)));
        harmony.Patch(Compatibility.Method(typeof(RecyclerTool), "UpdateAutoRecycle"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(AutoRecycle)));
        harmony.Patch(Compatibility.Method(typeof(PartPreviewDetection), "CalculateSnapDetectionUpdate"), prefix: new HarmonyMethod(typeof(FoundationTool), nameof(FirstFloor)));
    }
    private static bool Check(BuildToolLocalClient __instance, CameraPhysicsData cameraPhysics)
    {
        if (!Selected) { Hide(); return true; }
        _buildTool = __instance;
        var preview = (PartPreview)_preview.GetValue(__instance)!;
        FoundationHud.CapturePreview(preview);
        preview.HidePreview();
        if (FoundationHud.MenuOpen) { if (_holding) Cancel(); Hide(); return false; }
        _observed = FoundationNetwork.Now;
        if (_observed >= _nextPreview)
        {
            _nextPreview = _observed + 0.05; Compute(cameraPhysics);
            if (_holding && !_valid && !_awaiting)
            {
                if (_convertingShip != 0 && FoundationRegistry.Grounded(_convertingShip)) _awaiting = true;
                else Cancel();
            }
        }
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
        if (!Policy.CanSnapFirstFloor(delta.x, delta.y, delta.z, local.Normal.y, pier.HasCollar)) return true;
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
        if (!_valid || FoundationHud.MenuOpen)
        {
            if (!FoundationHud.MenuOpen && (_previewPrompt.Phase == FoundationPhase.Blocked || _previewPrompt.Phase == FoundationPhase.TerrainLoading))
            { _replyPrompt = _previewPrompt; _toastUntil = FoundationNetwork.Now + 1.8; }
            GameRefs.Service<IAudioPlayer>()?.PlayAudioEvent("Event:/InvalidError"); return false;
        }
        Feedback.Close(); Progress.Reset(); _toastUntil = 0;
        _replyPrompt = _previewPrompt;
        _convertingShip = _candidate.Action == CommandAction.Attach && !FoundationRegistry.Grounded(_candidate.Ship) ? _candidate.Ship : 0;
        var immediate = _candidate.Action == CommandAction.New || _convertingShip == 0;
        _holding = !immediate; _awaiting = immediate; _nextSend = FoundationNetwork.Now + 0.2;
        Feedback.Observe(FoundationNetwork.Send(_candidate), FoundationNetwork.Now);
        return false;
    }
    private static void End()
    {
        if (_holding && (_awaiting || _convertingShip != 0 && FoundationRegistry.Grounded(_convertingShip)))
        { _holding = false; _awaiting = true; return; }
        if (_holding) Cancel(); _holding = false;
    }
    private static void Disable() { Cancel(); Hide(); }
    private static bool Scroll(float scroll)
    {
        if (!Selected) return true;
        if (!PlacingNew) return false;
        if (Plugin.Mod.HeightModifier.Held) _height = Mathf.Clamp(_height + Math.Sign(scroll) * 0.25f, 0.25f, Policy.MaximumLength);
        else _yaw = Mathf.Repeat(_yaw + Math.Sign(scroll) * 45f, 360f);
        Cancel(); return false;
    }
    private static void Cancel()
    {
        if (_holding || _removing) FoundationNetwork.Send(new FoundationCommand { Action = CommandAction.Cancel });
        if (_removing) StopRecycler();
        _holding = _awaiting = _removing = false;
        _removal = default;
        _convertingShip = 0;
        Feedback.Close(); Progress.Reset();
    }
    private static bool Recycle(FloatingPartHover __instance)
    {
        var collider = _hoverCollider.GetValue(__instance)! as Collider;
        if (collider == null) return true;
        var helper = collider.GetComponentInParent<IShipPartHelper>();
        var identity = collider.GetComponentInParent<IShipFloatingPartId>();
        if (helper == null || identity == null || FoundationRegistry.Pier(helper.ShipId, identity.Id) == null) return true;
        BeginRemoval(helper.ShipId, identity.Id);
        return false;
    }
    internal static void BeginRemoval(uint ship, ushort part)
    {
        _removing = true;
        Feedback.Close(); Progress.Reset(); _toastUntil = 0;
        _releaseRequired = NeedsRelease(ship);
        _replyPrompt = RemovalPrompt(ship, _releaseRequired, true);
        _removal = new FoundationCommand { Action = CommandAction.Remove, Ship = ship, Part = part, Revision = FoundationRegistry.Revision(ship) };
        _nextRemoval = FoundationNetwork.Now + 0.2;
        Feedback.Observe(FoundationNetwork.Send(_removal), FoundationNetwork.Now);
    }
    private static bool NeedsRelease(uint ship)
    {
        var count = FoundationRegistry.Get(ship)?.Piers.Count ?? 0;
        var tracker = GameRefs.Service<Ships.Services.ShipsClientTracker>();
        return tracker == null || !tracker.TryGetTrackedShip(ship, out var tracked) || RemovalPolicy.NeedsHold(count, tracked.PartCount);
    }
    private static FoundationPrompt RemovalPrompt(uint ship, bool release, bool holding = false)
    {
        var count = FoundationRegistry.Get(ship)?.Piers.Count ?? 0;
        return new FoundationPrompt(release && holding ? FoundationPhase.Releasing : FoundationPhase.Removing,
            release ? FoundationPrompt.ReleaseWarning : count > 1 ? FoundationPrompt.SupportLabel(count - 1) + " remaining" : "", release);
    }
    private static bool BeginRecycling(RecyclerTool __instance)
    {
        _activeRecycler = __instance;
        // Refuse a protected floor before native success animation/firing starts.
        if (_recyclerHover.GetValue(__instance) is HullHover floor && !RecycleFloor(floor)) return false;
        return true;
    }
    private static void EndRecycling()
    {
        if (_removing && _removal.Action != CommandAction.Remove) { Cancel(); return; }
        if (_removing && (!_releaseRequired || FoundationRegistry.Pier(_removal.Ship, _removal.Part) == null))
        { StopRecycler(); return; }
        if (_removing) Cancel(); else _activeRecycler = null;
    }
    private static bool AutoRecycle() => !_removing;
    private static void StopRecycler()
    {
        if (_activeRecycler != null) _recyclerHolding.SetValue(_activeRecycler, false);
        _activeRecycler = null;
    }
    private static void UpdateRecycler(RecyclerTool __instance)
    {
        _recyclerTarget = _recyclerHover.GetValue(__instance) as FoundationHover;
        if (_recyclerTarget != null && FoundationRegistry.Pier(_recyclerTarget.Ship, _recyclerTarget.Part) != null)
        {
            _releaseRequired = NeedsRelease(_recyclerTarget.Ship) || _removing && _releaseRequired;
            _recyclerObserved = FoundationNetwork.Now;
            _recyclerPrompt = RemovalPrompt(_recyclerTarget.Ship, _releaseRequired);
        }
        if (FoundationHud.MenuOpen) { if (_removing) Cancel(); return; }
        if (!_removing) return;
        if (!_releaseRequired && !(bool)_recyclerHolding.GetValue(__instance)!)
        {
            StopRecycler();
            return;
        }
        // The host snapshot/removal can arrive before its reply. Stop the native
        // recycler immediately, but keep that request's feedback window alive.
        if (_removal.Action == CommandAction.Remove && FoundationRegistry.Pier(_removal.Ship, _removal.Part) == null)
        {
            StopRecycler();
            return;
        }
        _activeRecycler = __instance;
        if (!(bool)_recyclerHolding.GetValue(__instance)! || Plugin.Failure.Length != 0) { Cancel(); return; }
        if (_removal.Action != CommandAction.Remove) return;
        if (_recyclerTarget == null || _recyclerTarget.Ship != _removal.Ship || _recyclerTarget.Part != _removal.Part
            || FoundationRegistry.Pier(_removal.Ship, _removal.Part) == null || FoundationRegistry.Revision(_removal.Ship) != _removal.Revision)
        { Cancel(); return; }
        // Native auto-recycling deliberately suppresses repeats of the same
        // hovered object. A foundation's warning hold needs a fresh observation.
        if (FoundationNetwork.Now < _nextRemoval) return;
        _nextRemoval = FoundationNetwork.Now + 0.2;
        Feedback.Observe(FoundationNetwork.Send(_removal), FoundationNetwork.Now);
    }
    private static bool RecycleFloor(HullHover __instance)
    {
        var ship = (IShipPartTargeter)_hoverShip.GetValue(__instance)!;
        var floor = (HullPartPosition)_hoverFloor.GetValue(__instance)!;
        if (!FoundationRegistry.HasFloorAttachment(ship.ShipId, floor)) return true;
        Cancel(); StopRecycler();
        _removal = default;
        _releaseRequired = false; _toastUntil = FoundationNetwork.Now + 1.8;
        Status = "Recycle the Foundation Anchor on this floor before removing the floor.";
        _replyPrompt = new FoundationPrompt(FoundationPhase.Blocked, "Remove the anchor on this floor first.");
        GameRefs.Service<IAudioPlayer>()?.PlayAudioEvent("Event:/InvalidError");
        return false;
    }

    private static void Compute(CameraPhysicsData camera)
    {
        _valid = false;
        if (!FoundationNetwork.ClientReady || Plugin.Failure.Length != 0)
        { _previewPrompt = new FoundationPrompt(FoundationPhase.Waiting, "Preparing foundations."); Hide(); return; }
        if (!ClientScene.PhysicsScene.Raycast(camera.CameraPos, camera.Direction, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore))
        { _targetProxy = null; _candidate = new FoundationCommand { Action = CommandAction.New }; Status = "Aim at terrain to start a base, or a floor to anchor its structure."; _previewPrompt = new FoundationPrompt(FoundationPhase.Place, Status); Hide(); return; }
        var environment = GameRefs.Service<IWorldEnvironmentClientProvider>();
        var transpose = GameRefs.Service<IWorldTransposeClient>();
        if (environment == null || transpose == null) { Hide(); return; }
        Vector3 cap; Quaternion rotation;
        var request = new FoundationCommand { Rotation = new RotationF(0, 0, 0, 1) };
        var proxy = hit.collider.GetComponentInParent<IShipPartPlacements>();
        _targetProxy = proxy;
        var identity = hit.collider.GetComponentInParent<IShipFloatingPartId>();
        if (proxy != null && identity != null && FoundationRegistry.Pier(proxy.ShipId, identity.Id) != null)
        {
            _candidate = new FoundationCommand { Action = CommandAction.Attach, Ship = proxy.ShipId };
            Inspect(); return;
        }
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
            var ground = FoundationRegistry.Get(proxy.ShipId);
            if (ground != null)
                foreach (var pier in ground.Piers)
                    if ((Geometry.Vector(pier.LocalCap) - capLocal).sqrMagnitude < 0.0001f)
                    { _candidate = request; Inspect(); return; }
            if (_holding && ground == null) Outline(proxy.ShipId, proxy);
            else if (_outline != null) _outline.enabled = false;
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
        else { Status = "Choose planetary terrain or an ordinary floor."; _previewPrompt = new FoundationPrompt(FoundationPhase.Blocked, Status); Hide(); return; }
        var localGravity = environment.GetGravityAtPosition(ClientOrigin.LocalPhysicsSpaceId, cap);
        var upVector = -localGravity.normalized;
        var valid = Geometry.CheckAttachment(ClientScene.PhysicsScene, cap, rotation, upVector,
            request.Action == CommandAction.Attach ? request.Ship : (uint?)null, out var contact, out var reason);
        var count = request.Ship == 0 ? 0 : FoundationRegistry.Get(request.Ship)?.Piers.Count ?? 0;
        var affordable = HasMaterials();
        _previewPrompt = !affordable && valid ? new FoundationPrompt(FoundationPhase.Materials) : valid
            ? new FoundationPrompt(request.Action == CommandAction.New ? FoundationPhase.Place : count > 0 ? FoundationPhase.AddSupport : FoundationPhase.Anchor)
            : new FoundationPrompt(contact.PendingTerrain ? FoundationPhase.TerrainLoading : FoundationPhase.Blocked, reason);
        UpdateCost(true);
        EnsureGhost();
        _ghost!.SetActive(true);
        var visual = transpose.ToTransformOperation(ClientOrigin.LocalPhysicsSpaceId, ClientOrigin.LocalVisualSpaceId);
        var visualCap = visual.MultiplyPoint(cap); var visualFoot = visual.MultiplyPoint(contact.Foot);
        var visualRotation = (Quaternion)visual.Rotation * rotation;
        _ghost.transform.SetPositionAndRotation(visualCap, visualRotation);
        PreviewRecord.Kind = valid ? contact.Kind : AttachmentKind.Contact;
        PreviewRecord.LocalFoot = Geometry.Position(Quaternion.Inverse(visualRotation) * (visualFoot - visualCap));
        PreviewRecord.LocalFootNormal = Geometry.Position(valid ? Quaternion.Inverse(rotation) * contact.Normal : Vector3.up);
        PartRegistration.Fit(_ghost.transform, PreviewRecord, false);
        foreach (var renderer in _ghostRenderers) renderer.sharedMaterial = _material;
        _floorMarker.gameObject.SetActive(request.Action == CommandAction.Attach);
        _floorMarker.position = visualCap + visualRotation * Vector3.up * 0.105f;
        _floorMarker.rotation = visualRotation; _floorMarker.localScale = new Vector3(0.98f, 1, 0.98f);
        _contactMarker.gameObject.SetActive(valid);
        _obstructionMarker.gameObject.SetActive(contact.HasBlocker);
        if (contact.HasBlocker)
        { _obstructionMarker.position = visual.MultiplyPoint(contact.Blocker); _obstructionMarker.localScale = Vector3.one * 0.14f; }
        if (valid)
        {
            _contactMarker.position = visualFoot + (Quaternion)visual.Rotation * contact.Normal * 0.015f;
            _contactMarker.rotation = Geometry.SurfaceRotation(visualRotation, (Quaternion)visual.Rotation * contact.Normal);
            _contactMarker.localScale = new Vector3(0.74f, 1, 0.74f);
        }
        if (valid)
        {
            var extended = contact.Kind == AttachmentKind.Pier;
            if (!_holding) Status = request.Action == CommandAction.New ? "Place level foundation. Ordinary floors snap above its mounting plate."
                : (extended ? "Extended pier. " : "Ground-contact anchor. ") + "Hold build to ground the outlined structure. Position and tilt stay unchanged.";
        }
        else Status = reason;
        FoundationHud.SetGhostColor(_material!, valid && affordable);
        _candidate = request; _valid = valid && affordable;
    }
    private static bool HasMaterials()
    {
        if (GameRefs.Service<IPlayersProviderClient>()?.IsLocalPlayerUsingFreeBuildCheat() == true) return true;
        var inventory = GameRefs.Service<IInventoryProvider>();
        if (inventory == null) return false;
        foreach (var cost in PartRegistration.Settings.BuildingCost)
            if (inventory.GetItemQuantity(cost.ItemId) < cost.Quantity) return false;
        return true;
    }

    internal static void Update()
    {
        var now = FoundationNetwork.Now;
        if (Feedback.Expired(now)) { Cancel(); NotifyBlocked("Host did not confirm the action."); }
        if (FoundationHud.MenuOpen)
        { if (_holding || _removing) Cancel(); Hide(); if (_markers != null) _markers.SetActive(false); FoundationHud.Show(default, 0); return; }
        if (now - _observed > 0.3 || !Selected) { if (_holding) Cancel(); Hide(); }
        else if (_holding && _valid && !_awaiting && now >= _nextSend && !(_convertingShip != 0 && FoundationRegistry.Grounded(_convertingShip)))
        {
            _nextSend = now + 0.2; _awaiting = _candidate.Action == CommandAction.New;
            Feedback.Observe(FoundationNetwork.Send(_candidate), now);
        }
        var prompt = now < _toastUntil ? _replyPrompt
            : _holding || _removing || _awaiting ? _replyPrompt
            : RecyclerContext ? _recyclerPrompt
            : Selected ? _previewPrompt : default;
        FoundationHud.Show(prompt, prompt.HasProgress ? Progress.At(now) : 0, now < _toastUntil, _holding || _removing);
        UpdateMarkers(now);
    }
    internal static void Result(FoundationReply reply)
    {
        if (!Feedback.Accept(reply.Sequence, FoundationNetwork.Now)) return;
        Status = reply.Message; _awaiting = false;
        if (_removing) _releaseRequired = reply.ReleaseRequired;
        _replyPrompt = new FoundationPrompt(reply.Phase, reply.Message, reply.ReleaseRequired);
        Progress.Set(reply.Progress, reply.Duration, FoundationNetwork.Now);
        if (reply.Success)
        {
            if (_removing) StopRecycler();
            else if (_buildTool != null)
            {
                _buildTool.GetComponentInChildren<BuildToolAnimator>()?.PlayPlacePartAnimation();
                _buildTool.GetComponentInChildren<BuildToolFiringEffect>()?.BeginFiring();
                GameRefs.Service<ILocalPlayerToolFiringProxy>()?.NotifyToolFiringBegin();
            }
            _holding = _removing = false; _removal = default; Feedback.Close(); _toastUntil = FoundationNetwork.Now + 0.8;
            _convertingShip = 0;
        }
        else if (reply.Phase == FoundationPhase.Blocked || reply.Phase == FoundationPhase.Materials)
        {
            if (_removing) StopRecycler();
            _holding = _removing = false; Feedback.Close(); _toastUntil = FoundationNetwork.Now + 1.8;
            GameRefs.Service<IAudioPlayer>()?.PlayAudioEvent("Event:/InvalidError");
        }
    }
    internal static void NotifyBlocked(string message)
    {
        Status = message; _replyPrompt = new FoundationPrompt(FoundationPhase.Blocked, message);
        _toastUntil = FoundationNetwork.Now + 2.2;
    }
    private static void Inspect()
    {
        EnsureGhost(); Hide();
        _previewPrompt = new FoundationPrompt(FoundationPhase.Inspect);
    }
    private static void UpdateCost(bool visible)
    {
        if (_costVisible == visible) return;
        _costVisible = visible;
        var tracker = GameRefs.Service<IBuildToolPartPreviewTracker>();
        if (visible) tracker?.OnPreviewShown?.Invoke(PartRegistration.Settings);
        else tracker?.OnPreviewHidden?.Invoke();
    }
    private static void UpdateMarkers(double now)
    {
        if (!Selected || FoundationHud.MenuOpen || _candidate.Ship == 0)
        { if (_markers != null) _markers.SetActive(false); return; }
        if (now < _nextMarkers || _material == null) return;
        _nextMarkers = now + 0.2;
        var ground = FoundationRegistry.Get(_candidate.Ship);
        var proxy = _targetProxy;
        if (ground == null || proxy == null || proxy.ShipId != _candidate.Ship)
        { if (_markers != null) _markers.SetActive(false); return; }
        var transpose = GameRefs.Service<IWorldTransposeClient>();
        if (transpose == null) return;
        var visual = transpose.ToTransformOperation(ClientOrigin.LocalPhysicsSpaceId, ClientOrigin.LocalVisualSpaceId);
        _markers ??= new GameObject("Nearby foundation markers"); _markers.SetActive(true);
        if (_markerMaterial == null) { _markerMaterial = new Material(_material); FoundationHud.SetGhostColor(_markerMaterial, true); }
        var used = 0;
        foreach (var pier in ground.Piers)
        {
            if (used == 32) break;
            var position = proxy.ToWorldPos(new FloatingPartPlacement(Geometry.Vector(pier.LocalCap) + Vector3.up * 0.24f, PartOrientation.Forward));
            var camera = Camera.main;
            var visualPos = visual.MultiplyPoint(position);
            if (camera != null && (camera.transform.position - visualPos).sqrMagnitude > 900) continue;
            if (MarkerPool.Count <= used)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder); marker.name = "Foundation marker";
                marker.layer = LayerMask.NameToLayer("Ignore Raycast");
                var collider = marker.GetComponent<Collider>(); collider.enabled = false; UnityEngine.Object.Destroy(collider);
                marker.transform.SetParent(_markers.transform, false); marker.GetComponent<Renderer>().sharedMaterial = _markerMaterial;
                marker.GetComponent<MeshFilter>().sharedMesh = AnchorModel.Border;
                MarkerPool.Add(marker.transform);
            }
            var item = MarkerPool[used++]; item.gameObject.SetActive(true); item.position = visualPos;
            item.rotation = (Quaternion)visual.Rotation * proxy.ToWorldRotation(Quaternion.identity);
            item.localScale = new Vector3(0.28f, 1, 0.28f);
        }
        for (var i = used; i < MarkerPool.Count; i++) MarkerPool[i].gameObject.SetActive(false);
    }
    private static void EnsureGhost()
    {
        if (_ghost != null) return;
        _ghost = new GameObject("Foundation placement preview");
        _material = new Material(FoundationHud.Hologram ?? throw new InvalidOperationException("Native build hologram is unavailable."));
        var mount = Primitive("FoundationMount");
        mount.localPosition = Vector3.down * 0.1f; mount.localScale = new Vector3(0.9f, 0.2f, 0.9f);
        AnchorModel.Mount(mount); PartRegistration.Fit(_ghost.transform, PreviewRecord, false);
        _floorMarker = Primitive("Selected floor"); _contactMarker = Primitive("Terrain contact");
        _obstructionMarker = Primitive("Obstruction");
        _floorMarker.GetComponent<MeshFilter>().sharedMesh = _contactMarker.GetComponent<MeshFilter>().sharedMesh = AnchorModel.Border;
        _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in _ghostRenderers) renderer.sharedMaterial = _material;
    }
    private static Transform Primitive(string name)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = name; item.layer = LayerMask.NameToLayer("Ignore Raycast");
        item.transform.SetParent(_ghost!.transform, false);
        var collider = item.GetComponent<Collider>(); collider.enabled = false; UnityEngine.Object.Destroy(collider);
        item.GetComponent<Renderer>().sharedMaterial = _material; return item.transform;
    }
    private static void Outline(uint ship, IShipPartPlacements proxy)
    {
        if (_outline == null)
        {
            var root = new GameObject("Foundation structure outline"); _outline = root.AddComponent<LineRenderer>();
            _outline.sharedMaterial = new Material(Shader.Find("Sprites/Default")); _outline.startColor = _outline.endColor = FoundationHud.ValidColor;
            _outline.startWidth = _outline.endWidth = 0.006f; _outline.useWorldSpace = true;
        }
        var client = GameRefs.Service<Ships.Services.ShipsClientTracker>();
        if (client == null || !client.TryGetTrackedShip(ship, out var tracked)) return;
        if (!_outline.enabled || _outlinedShip != ship)
        { var bounds = tracked.GetHullCoordBounds(); _outlineCenter = bounds.Center; _outlineExtents = bounds.Extents + Vector3.one * 0.1f; }
        _outline.enabled = true;
        var transform = ServiceLocator.GetService<IWorldTransposeClient>().ToTransformOperation(ClientOrigin.LocalPhysicsSpaceId, ClientOrigin.LocalVisualSpaceId);
        for (var i = 0; i < 8; i++)
        {
            var local = _outlineCenter + Vector3.Scale(_outlineExtents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            OutlineCorners[i] = transform.MultiplyPoint(proxy.ToWorldPos(new FloatingPartPlacement(local, PartOrientation.Forward)));
        }
        for (var i = 0; i < OutlineLine.Length; i++) OutlineLine[i] = OutlineCorners[OutlineOrder[i]];
        _outline.positionCount = OutlineLine.Length; _outline.SetPositions(OutlineLine); _outlinedShip = ship;
    }
    private static void Hide() { if (_ghost != null) _ghost.SetActive(false); if (_outline != null) _outline.enabled = false; UpdateCost(false); }
    internal static void Clear()
    {
        _holding = _awaiting = _removing = _valid = false; _outlinedShip = 0; _observed = _nextPreview = 0;
        _activeRecycler = null; _removal = default;
        _recyclerTarget = null; _releaseRequired = false;
        _convertingShip = 0;
        Feedback.Close(); Progress.Reset(); _previewPrompt = _replyPrompt = _recyclerPrompt = default;
        _toastUntil = _recyclerObserved = 0; _buildTool = null; _costVisible = false; FoundationHud.Clear();
        if (_markers != null) UnityEngine.Object.Destroy(_markers); _markers = null; MarkerPool.Clear(); _nextMarkers = 0;
        if (_markerMaterial != null) UnityEngine.Object.Destroy(_markerMaterial); _markerMaterial = null; _targetProxy = null;
        if (_ghost != null) UnityEngine.Object.Destroy(_ghost); if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_outline != null) { UnityEngine.Object.Destroy(_outline.sharedMaterial); UnityEngine.Object.Destroy(_outline.gameObject); }
        _ghost = null; _material = null; _outline = null; Status = "";
        _ghostRenderers = Array.Empty<Renderer>();
    }
    internal static void Draw()
    {
        if (!Plugin.Ready && Plugin.Failure.Length == 0) return;
        if (FoundationNetwork.ConnectionError.Length != 0) GUI.Box(new Rect(20, 120, 800, 90), FoundationNetwork.ConnectionError);
        if (Plugin.Failure.Length != 0)
        {
            GUI.Box(new Rect(20, 20, 800, 90), Plugin.DisplayName + " stopped: " + Plugin.Failure + "\nResolve the error before loading, saving or continuing a foundation world."); return;
        }
    }
}
