using System;
using System.Collections.Generic;
using HarmonyLib;
using Items.Model;
using SDModKit.Content;
using SDModKit.Game;
using Ships.Interface.Model.Parts;
using Ships.Interface.Settings;
using Ships.Interface.Services;
using UnityEngine;

namespace StellarFoundations;

internal static class PartRegistration
{
    internal const string Asset = "SilentHacks.FoundationPier";
    internal const string Definition = @"{
          'id':'silenthacks.foundations','name':'" + Plugin.DisplayName + @"','version':'" + Plugin.Version + @"',
          'parts':[{'id':61240,'asset':'SilentHacks.FoundationPier','label':'Foundation',
          'description':'Anchors a structure to the ground.',
          'donor':'SignalSplitter','state':'SignalSplitterState','snapping':'UnderFloor','buildMenuAfter':'WoodFloor','mass':20,
          'bounds':{'center':[0,-0.1,0],'size':[0.9,0.2,0.9]},'sockets':[],
          'visual':{'clearDonor':true,'meshes':[{'shape':'cube','position':[0,-0.1,0],'scale':[0.9,0.2,0.9],'color':'#59636e'}]}}]
        }";
    private static PartSettings? _basicFloor;
    internal static PartSettings Settings => CustomParts.Get(Policy.PartSettingsId) ?? throw new InvalidOperationException("Foundation is not registered.");
    internal static void Register()
    {
        if (ContentRegistry.PartDef(Policy.PartSettingsId) != null) throw new InvalidOperationException("Foundation part ID is already registered by another pack.");
        ContentRegistry.LoadJson(typeof(Plugin).Assembly, Definition, "foundations");
        new HarmonyRuntime(Plugin.Owner).Patch(Compatibility.Method(typeof(ContentBuilder), "Configure"),
            postfix: new HarmonyMethod(typeof(PartRegistration), nameof(Configured)));
        new HarmonyRuntime(Plugin.Owner).Patch(Compatibility.Method(typeof(Research.ResearchClientProxy), "IsPartUnlocked"),
            prefix: new HarmonyMethod(typeof(PartRegistration), nameof(FoundationUnlock)));
    }

    private static void Configured(ContentPartDef def, PartSettings settings, GameObject prefab)
    {
        if (def.Asset != Asset) return;
        ApplyCost(settings);
        var visual = prefab.GetComponentInChildren<Ships.Parts.Common.Model.Reactions.IShipPartVisual>()?.GameObject.transform;
        if (visual != null)
        {
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>()) AnchorModel.Mount(filter.transform);
            Fit(visual, new PierRecord { Kind = AttachmentKind.Pier, HasCollar = true,
                LocalCap = new PositionD(0, 0, 0), LocalFoot = new PositionD(0, -0.8, 0), LocalFootNormal = new PositionD(0, 1, 0) }, false);
        }
    }
    private static void FoundationUnlock(ref PartSettings __0)
    {
        if (__0 != null && __0.id == Policy.PartSettingsId) __0 = BasicFloor();
    }

    internal static bool IsFoundation(StatefulPart part) => part.Settings.id == Policy.PartSettingsId;

    internal static void EnsureCost()
    { ApplyCost(Settings); }

    private static void ApplyCost(PartSettings settings)
    {
        var field = Compatibility.Field(typeof(PartSettings), "buildingCost");
        var floor = BasicFloor();
        var cost = new List<ItemInstance>();
        foreach (var item in floor.BuildingCost)
        {
            var itemSettings = ItemCatalog.ById(item.ItemId) ?? throw new InvalidOperationException("Foundation recipe contains an unknown item.");
            cost.Add(new ItemInstance(itemSettings, checked(item.Quantity * 2)));
        }
        field.SetValue(settings, cost);
    }
    private static PartSettings BasicFloor()
    {
        if (_basicFloor != null) return _basicFloor;
        var floor = PartLookup.ByAssetName("WoodFloor");
        if (floor == null)
            foreach (var candidate in new PartSettingsProvider().GetAllPartSettings())
                if (candidate is HullPartSettings hull && hull.hullMaterialType == HullMaterialType.Wood && candidate.snappingStyle == SnappingStyle.Floor && !candidate.IsStateful)
                { floor = candidate; break; }
        if (floor == null) throw new InvalidOperationException("Basic wooden floor recipe could not be resolved.");
        return _basicFloor = floor;
    }

    // Keep adaptive geometry out of prefab bounds. Every generated collider stays
    // beneath the native part identity, so the exposed collar uses normal recycling.
    internal static void Fit(Transform root, PierRecord record, bool colliders)
    {
        var collar = Shape(root, "FoundationCollar", colliders);
        collar.gameObject.SetActive(record.HasCollar);
        collar.localPosition = Vector3.up * Policy.CollarHeight;
        collar.localRotation = Quaternion.identity;
        collar.localScale = new Vector3(0.7f, Policy.CollarThickness, 0.7f);
        var column = Shape(root, "FoundationColumn", colliders);
        var extended = record.Kind == AttachmentKind.Pier;
        column.gameObject.SetActive(extended);
        var down = Geometry.Vector(record.LocalFoot) - Geometry.Vector(record.LocalCap);
        // The instanced part root already carries the cap's placement transform.
        if (extended)
        {
            column.localPosition = down * 0.5f;
            column.localRotation = Geometry.SurfaceRotation(Quaternion.identity, -down.normalized);
            column.localScale = new Vector3(0.18f, down.magnitude, 0.18f);
        }
        var foot = Shape(root, "FoundationFoot", colliders);
        foot.gameObject.SetActive(extended);
        if (extended)
        {
            foot.localPosition = down; foot.localRotation = Geometry.SurfaceRotation(Quaternion.identity, Geometry.Vector(record.LocalFootNormal));
            foot.localScale = new Vector3(0.65f, 0.12f, 0.65f);
        }
        AnchorModel.Finish(root, colliders);
    }
    private static Transform Shape(Transform root, string name, bool colliders)
    {
        var existing = root.Find(name);
        if (existing != null) return existing;
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = name; item.layer = root.gameObject.layer;
        item.transform.SetParent(root, false);
        // Primitive meshes only define the collider/frame. Visible timber is
        // attached by AnchorModel.Finish; no temporary metal material is needed.
        item.GetComponent<Renderer>().enabled = false;
        if (colliders) item.tag = "ShipFloatingPart";
        else { var collider = item.GetComponent<Collider>(); collider.enabled = false; UnityEngine.Object.Destroy(collider); }
        return item.transform;
    }
}
