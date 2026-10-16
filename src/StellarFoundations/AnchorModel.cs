using System;
using System.Collections.Generic;
using SDModKit.Game;
using Ships.Interface.Services;
using UnityEngine;

namespace StellarFoundations;

// Generate the same wood panel and edge slices as the native hull preview.
// Retain its UVs, vertex colours, texture-array selection and materials instead
// of repainting a mechanical part or approximating the hull's surface treatment.
internal static class AnchorModel
{
    private static GameObject? _timber;
    private static Mesh? _border;
    internal static Mesh Border => _border ??= BuildBorder();
    private static void Ensure()
    {
        if (_timber != null) return;
        var floor = PartLookup.ByAssetName("WoodFloor") ?? throw new InvalidOperationException("Native wooden floor is unavailable.");
        var instancer = GameRefs.Service<IShipPartInstancer>() ?? throw new InvalidOperationException("Native hull artwork builder is unavailable.");
        var template = new GameObject("Foundation timber template");
        var unit = new GameObject("Native wood surfaces").transform; unit.SetParent(template.transform, false);
        var panel = instancer.InstantiatePartPreview(floor, unit);
        panel.transform.localRotation = floor.DefaultRotation;
        var filters = panel.GetComponentsInChildren<MeshFilter>();
        if (filters.Length == 0) throw new InvalidOperationException("Native wooden floor has no mesh slices.");
        var bounds = new Bounds(); var first = true;
        foreach (var filter in filters)
        {
            var mesh = filter.sharedMesh;
            for (var i = 0; i < 8; i++)
            {
                var corner = mesh.bounds.center + Vector3.Scale(mesh.bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var point = template.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
            }
        }
        if (bounds.size.x < 0.01f || bounds.size.y < 0.01f || bounds.size.z < 0.01f)
            throw new InvalidOperationException("Native wooden floor bounds differ from the inspected build.");
        var inverse = new Vector3(1 / bounds.size.x, 1 / bounds.size.y, 1 / bounds.size.z);
        unit.localScale = inverse; unit.localPosition = -Vector3.Scale(bounds.center, inverse);
        template.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(template); _timber = template;
    }
    internal static void Mount(Transform frame)
    { Ensure(); frame.name = "FoundationMount"; Piece(frame); }
    private static void Piece(Transform frame, bool upright = false)
    {
        var renderer = frame.GetComponent<Renderer>(); if (renderer != null) renderer.enabled = false;
        var child = frame.Find("Timber");
        if (child == null)
        {
            child = UnityEngine.Object.Instantiate(_timber!, frame, false).transform;
            child.name = "Timber";
            foreach (var item in child.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = frame.gameObject.layer;
            child.gameObject.SetActive(true);
        }
        // Put the native plank surface on a post's side rather than stretching
        // only the floor's dark end edging down its entire visible length.
        child.localRotation = upright ? Quaternion.AngleAxis(90, Vector3.forward) : Quaternion.identity;
    }
    internal static void Finish(Transform root, bool colliders)
    {
        if (colliders) return;
        Ensure();
        var collar = root.Find("FoundationCollar"); if (collar != null) Piece(collar);
        var foot = root.Find("FoundationFoot"); if (foot != null) Piece(foot);
        var column = root.Find("FoundationColumn");
        if (column != null)
        {
            Piece(column, true);
        }
        var neck = root.Find("Mounting neck");
        if (neck == null)
        {
            neck = new GameObject("Mounting neck").transform;
            neck.gameObject.layer = root.gameObject.layer; neck.SetParent(root, false);
            neck.localPosition = Vector3.up * 0.06f; neck.localScale = new Vector3(0.32f, 0.12f, 0.32f);
            Piece(neck, true);
        }
        neck.gameObject.SetActive(collar != null && collar.gameObject.activeSelf);
    }
    internal static bool Highlight(uint ship, ushort part, bool enabled)
    {
        var tracker = GameRefs.Service<Ships.Services.ShipsClientTracker>();
        if (tracker == null || !tracker.TryGetShipVisuals(ship, out var visuals) || !visuals.TryGetFloatingPartVisual(part, out var visual)) return false;
        var highlight = visual.GameObject.GetComponent<FoundationTimberHighlight>();
        if (highlight == null && !enabled) return true;
        highlight ??= visual.GameObject.AddComponent<FoundationTimberHighlight>();
        highlight.Set(enabled); return true;
    }
    private static Mesh BuildBorder()
    {
        var vertices = new Vector3[16]; var triangles = new int[24]; var uv = new Vector2[16];
        for (var edge = 0; edge < 4; edge++)
        {
            var rotation = Quaternion.AngleAxis(edge * 90, Vector3.up); var first = edge * 4;
            var points = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, -0.482f), new Vector3(0.5f, 0, -0.482f), new Vector3(0.5f, 0, -0.5f) };
            for (var i = 0; i < 4; i++) { vertices[first + i] = rotation * points[i]; uv[first + i] = new Vector2(i % 2, i / 2); }
            var t = edge * 6; triangles[t] = first; triangles[t + 1] = first + 1; triangles[t + 2] = first + 2;
            triangles[t + 3] = first; triangles[t + 4] = first + 2; triangles[t + 5] = first + 3;
        }
        var result = new Mesh { name = "Foundation placement border", vertices = vertices, triangles = triangles, uv = uv };
        result.RecalculateNormals(); result.RecalculateBounds(); return result;
    }
}

// Native hull recycling selects its red palette tile through UV channel 1.
// Clone only selected-instance meshes, leaving other anchors and thumbnails alone.
internal sealed class FoundationTimberHighlight : MonoBehaviour
{
    private readonly List<(Mesh Mesh, Vector2[] Original)> _slices = new List<(Mesh, Vector2[])>();
    private bool _initialized;
    internal void Set(bool enabled)
    {
        if (!_initialized)
        {
            _initialized = true;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled) continue;
                var uvs = new List<Vector2>(); filter.sharedMesh.GetUVs(1, uvs);
                if (uvs.Count == 0) continue;
                var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh); filter.sharedMesh = mesh;
                _slices.Add((mesh, uvs.ToArray()));
            }
        }
        foreach (var slice in _slices)
        {
            var uvs = (Vector2[])slice.Original.Clone();
            if (enabled) for (var i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(0.375f, 0.5f);
            slice.Mesh.uv2 = uvs;
        }
    }
    private void OnDestroy()
    { foreach (var slice in _slices) if (slice.Mesh != null) UnityEngine.Object.Destroy(slice.Mesh); }
}
