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
    private static Material? _columnMaterial, _footMaterial;
    private static PartSettings? _basicFloor;
    internal static PartSettings Settings => CustomParts.Get(Policy.PartSettingsId) ?? throw new InvalidOperationException("Foundation Pier is not registered.");
    internal static void Register()
    {
        if (ContentRegistry.PartDef(Policy.PartSettingsId) != null) throw new InvalidOperationException("Foundation part ID is already registered by another pack.");
        ContentRegistry.LoadJson(typeof(Plugin).Assembly, @"{
          'id':'silenthacks.foundations','name':'Stellar Foundations','version':'0.1.0',
          'parts':[{'id':61240,'asset':'SilentHacks.FoundationPier','label':'Foundation Pier',
          'description':'Stationary planetary support for ordinary floors. Scroll rotates; Shift + scroll adjusts height. Hold build to attach an existing structure without moving it.',
          'donor':'SignalSplitter','state':'SignalSplitterState','snapping':'UnderFloor','buildMenuAfter':'WoodFloor','mass':20,
          'bounds':{'center':[0,-0.1,0],'size':[0.9,0.2,0.9]},'sockets':[],
          'visual':{'clearDonor':true,'meshes':[{'shape':'cube','position':[0,-0.1,0],'scale':[0.9,0.2,0.9],'color':'#59636e'}]}}]
        }", "foundations");
        new HarmonyRuntime(Plugin.Owner).Patch(Compatibility.Method(typeof(ContentBuilder), "Configure"),
            postfix: new HarmonyMethod(typeof(PartRegistration), nameof(Configured)));
        new HarmonyRuntime(Plugin.Owner).Patch(Compatibility.Method(typeof(Research.ResearchClientProxy), "IsPartUnlocked"),
            prefix: new HarmonyMethod(typeof(PartRegistration), nameof(FoundationUnlock)));
    }

    private static void Configured(ContentPartDef def, PartSettings settings)
    {
        if (def.Asset != Asset) return;
        ApplyCost(settings);
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

    // Keep the leg out of the prefab's bounds: its dimensions belong to this placed pier.
    internal static void Fit(Transform root, PierRecord record, bool colliders)
    {
        var column = root.Find("FoundationColumn");
        if (column == null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = "FoundationColumn"; item.layer = root.gameObject.layer;
            if (colliders) item.tag = "ShipFloatingPart";
            item.transform.SetParent(root, false); column = item.transform;
            if (!colliders) UnityEngine.Object.Destroy(item.GetComponent<Collider>());
            var renderer = item.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = !colliders;
                if (!colliders) renderer.sharedMaterial = SharedMaterial(ref _columnMaterial, new Color(0.28f, 0.31f, 0.35f));
            }
        }
        var down = Geometry.Vector(record.LocalFoot) - Geometry.Vector(record.LocalCap);
        // The instanced part root already carries the cap's placement transform.
        column.localPosition = down * 0.5f;
        column.localRotation = Quaternion.FromToRotation(Vector3.up, down.normalized);
        column.localScale = new Vector3(0.18f, down.magnitude, 0.18f);
        var foot = root.Find("FoundationFoot");
        if (foot == null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = "FoundationFoot"; item.layer = root.gameObject.layer;
            if (colliders) item.tag = "ShipFloatingPart";
            item.transform.SetParent(root, false); foot = item.transform;
            if (!colliders) UnityEngine.Object.Destroy(item.GetComponent<Collider>());
            var renderer = item.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = !colliders;
                if (!colliders) renderer.sharedMaterial = SharedMaterial(ref _footMaterial, new Color(0.38f, 0.4f, 0.42f));
            }
        }
        foot.localPosition = down; foot.localRotation = Quaternion.FromToRotation(Vector3.up, Geometry.Vector(record.LocalFootNormal));
        foot.localScale = new Vector3(0.65f, 0.12f, 0.65f);
    }
    private static Material SharedMaterial(ref Material? cached, Color color)
    {
        if (cached == null)
        {
            var source = ContentAssets.Material("PartMetal") ?? throw new InvalidOperationException("Native foundation material is unavailable.");
            cached = new Material(source) { color = color, name = "Foundation shared material" };
            if (cached.HasProperty("_BaseColor")) cached.SetColor("_BaseColor", color);
            if (cached.HasProperty("_Color")) cached.SetColor("_Color", color);
        }
        return cached;
    }
}
