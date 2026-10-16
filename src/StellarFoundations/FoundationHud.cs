using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Services;
using HarmonyLib;
using SDModKit.Game;
using TMPro;
using Tools.Build;
using UI.Interface.Services;
using UI.ToolBar;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StellarFoundations;

// Uses the running game's canvas, font, input glyph prefab and hologram palette.
// Ordinary gameplay has no IMGUI overlay and adds no input-capturing surface.
internal static class FoundationHud
{
    private static ToolKeyTipsUpdater? _tips;
    private static GameObject? _root;
    private static TMP_Text? _notice;
    private static RectTransform? _fill, _track;
    private static Color _normal;
    private static ToolKeyTip _place = null!, _menu = null!;
    private static InputActionReference? _height;
    private static InputActionAsset? _bindingAsset;
    private static string _heightBinding = "";
    private static double _nextRetry;
    private static MethodInfo? _cancelDrag, _unlockPreview;
    private static Canvas? _canvas;
    private static readonly Color Warning = new Color(1, 0.8f, 0.34f, 1);
    private static FieldInfo TipLabel => Compatibility.Field(typeof(ToolKeyTipUi), "label");
    internal static Color ValidColor { get; private set; } = new Color(0.2f, 0.9f, 0.65f);
    internal static Color InvalidColor { get; private set; } = new Color(1, 0.25f, 0.2f);
    internal static Material? Hologram { get; private set; }
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    internal static bool MenuOpen => SDModKit.ModKit.Menu?.IsOpen == true
        || GameRefs.Service<IClosableMenuService>()?.HasClosableMenuOpen() == true;
    internal static void Install(HarmonyRuntime harmony)
    {
        harmony.Patch(Compatibility.Method(typeof(ToolKeyTipsUpdater), "GenerateTips"),
            prefix: new HarmonyMethod(typeof(FoundationHud), nameof(KeyTips)) { priority = Priority.Last });
        // Build Plus emits its own hints and handles Shift-drag after the native
        // list. Let this tool own its controls while retaining ordinary tools.
        // Unknown versions receive no guessed private-method patches.
        var buildPlus = AccessTools.TypeByName("BuildPlus.DragBuilder");
        if (buildPlus?.Assembly.ManifestModule.ModuleVersionId == new Guid("39fcd4e3-c9b9-42e3-9507-4ed9ef2512f5"))
        {
            _cancelDrag = Compatibility.Method(buildPlus, "Cancel");
            var nudge = buildPlus.Assembly.GetType("BuildPlus.Nudge", true)!;
            _unlockPreview = Compatibility.Method(nudge, "Unlock");
            harmony.Patch(Compatibility.Method(buildPlus, "Update"), prefix: new HarmonyMethod(typeof(FoundationHud), nameof(BuildPlusDrag)));
            harmony.Patch(Compatibility.Method(nudge, "Update"), prefix: new HarmonyMethod(typeof(FoundationHud), nameof(BuildPlusNudge)));
            harmony.Patch(Compatibility.Method(buildPlus, "A", new[] { typeof(bool), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(FoundationHud), nameof(BuildPlusHints)));
        }
    }
    private static bool BuildPlusHints() => !FoundationTool.OwnControls;
    private static bool BuildPlusDrag()
    {
        if (!FoundationTool.OwnControls) return true;
        _cancelDrag!.Invoke(null, null); return false;
    }
    private static bool BuildPlusNudge()
    {
        if (!FoundationTool.IsSelected) return true;
        _unlockPreview!.Invoke(null, null); return false;
    }
    internal static void CapturePreview(PartPreview preview)
    {
        if (Hologram != null) return;
        Hologram = (Material)Compatibility.Field(typeof(PartPreview), "hologramMat").GetValue(preview)!;
        ValidColor = (Color)Compatibility.Field(typeof(PartPreview), "validColor").GetValue(preview)!;
        InvalidColor = (Color)Compatibility.Field(typeof(PartPreview), "invalidColor").GetValue(preview)!;
    }
    private static bool Ensure()
    {
        if (_root != null) return true;
        if (FoundationNetwork.Now < _nextRetry) return false;
        _nextRetry = FoundationNetwork.Now + 0.5;
        _tips = GameRefs.Service<IKeyTipsDisplayer>() as ToolKeyTipsUpdater;
        if (_tips == null) return false;
        var wrapper = (Transform)Compatibility.Field(typeof(ToolKeyTipsUpdater), "tipWrapper").GetValue(_tips)!;
        var canvas = wrapper.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        var prefab = (ToolKeyTipUi)Compatibility.Field(typeof(ToolKeyTipsUpdater), "toolKeyTipPrefab").GetValue(_tips)!;
        var template = (TMP_Text)TipLabel.GetValue(prefab)!;
        _place = (ToolKeyTip)Compatibility.Field(typeof(ToolKeyTipsUpdater), "place").GetValue(_tips)!;
        _menu = (ToolKeyTip)Compatibility.Field(typeof(ToolKeyTipsUpdater), "openMenu").GetValue(_tips)!;
        _canvas = canvas.rootCanvas;
        // No card or dialogue surface. Ordinary feedback stays in the native
        // hologram, material counter and bottom tool hints.
        _root = new GameObject("Foundation interaction", typeof(RectTransform), typeof(CanvasGroup));
        var rect = (RectTransform)_root.transform;
        rect.SetParent(canvas.rootCanvas.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, 88); rect.sizeDelta = new Vector2(560, 80);
        var group = _root.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
        _notice = new GameObject("Context", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        _notice.transform.SetParent(rect, false);
        _notice.font = template.font; _notice.fontSharedMaterial = template.fontSharedMaterial;
        _notice.fontSize = template.fontSize * 0.85f; _notice.fontStyle = FontStyles.Normal;
        _notice.color = template.color; _normal = template.color;
        _notice.alignment = TextAlignmentOptions.Center; _notice.richText = false; _notice.raycastTarget = false;
        AlignTop((RectTransform)_notice.transform);
        _track = new GameObject("Hold progress", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _track.SetParent(rect, false); _track.sizeDelta = new Vector2(196, 4);
        AlignTop(_track);
        _track.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);
        _track.GetComponent<Image>().raycastTarget = false;
        _fill = new GameObject("Confirmed progress", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        _fill.SetParent(_track, false); _fill.anchorMin = Vector2.zero; _fill.anchorMax = Vector2.one;
        _fill.offsetMin = _fill.offsetMax = Vector2.zero; _fill.GetComponent<Image>().raycastTarget = false;
        return true;
    }
    private static void AlignTop(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1); rect.anchoredPosition = Vector2.zero;
    }
    internal static void Show(FoundationPrompt prompt, float progress, bool attempted = false, bool active = false)
    {
        if (prompt.Phase == FoundationPhase.Hidden || MenuOpen || !GameRefs.InWorld)
        { if (_root != null) _root.SetActive(false); RefreshHints(); return; }
        if (!Ensure()) return;
        var notice = prompt.Notice(attempted, active);
        var showProgress = active && prompt.HasProgress;
        _root!.SetActive(notice.Length != 0 || showProgress);
        RefreshHints();
        if (!_root.activeSelf) return;
        var rect = (RectTransform)_root.transform;
        var parentWidth = ((RectTransform)rect.parent).rect.width;
        var width = Mathf.Clamp(parentWidth - 40, 300, 560);
        _notice!.text = notice; _notice.gameObject.SetActive(notice.Length != 0);
        var detailHeight = notice.Length == 0 ? 0 : Mathf.Max(24, _notice.GetPreferredValues(notice, width, 0).y);
        var detailRect = (RectTransform)_notice.transform;
        detailRect.sizeDelta = new Vector2(width, detailHeight); detailRect.anchoredPosition = Vector2.zero;
        var barHeight = Mathf.Max(12, 6 / Mathf.Max(0.1f, _canvas!.scaleFactor));
        _track!.sizeDelta = new Vector2(196, barHeight);
        _track.anchoredPosition = new Vector2(0, -detailHeight - (detailHeight > 0 ? 12 : 0));
        rect.sizeDelta = new Vector2(width, detailHeight + barHeight);
        rect.anchoredPosition = new Vector2(0, 64 + detailHeight + barHeight);
        _track!.gameObject.SetActive(showProgress);
        _fill!.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
        _fill.GetComponent<Image>().color = prompt.IsWarning ? Warning : _normal;
    }
    private static void RefreshHints()
    {
        if (_tips == null) return;
        var key = FoundationTool.HintMode;
        if (key == _hintMode) return;
        _hintMode = key; Compatibility.Method(typeof(ToolKeyTipsUpdater), "UpdateKeyTips").Invoke(_tips, null);
    }
    private static string _hintMode = "";
    private static bool KeyTips()
    {
        if (!FoundationTool.OwnControls || MenuOpen || !Ensure()) return true;
        var result = new List<ToolKeyTip>();
        if (FoundationTool.ActionHint.Length != 0)
        {
            var text = FoundationPrompt.ActionLabel(FoundationTool.ActionHint, FoundationTool.HoldReason);
            result.Add(new ToolKeyTip { action = _place.action, actionKeyAlt = _place.actionKeyAlt, actionDesc = text });
        }
        if (FoundationTool.PlacingNew)
        {
            if (_bindingAsset == null)
            {
                _bindingAsset = ScriptableObject.CreateInstance<InputActionAsset>();
                var map = new InputActionMap("Foundation hints"); _bindingAsset.AddActionMap(map);
                _height = InputActionReference.Create(map.AddAction("Height", InputActionType.Button, "<Keyboard>/leftShift"));
            }
            var binding = Plugin.Mod.HeightModifier.Display;
            if (_height == null || _heightBinding != binding)
            {
                _heightBinding = binding;
                var modifier = Plugin.Mod.HeightModifier;
                var path = modifier.Mouse != SDModKit.Input.MouseBind.None ? "<Mouse>/" + modifier.Mouse.ToString().ToLowerInvariant() + "Button"
                    : modifier.Key == UnityEngine.InputSystem.Key.None ? "" : "<Keyboard>/" + modifier.Key;
                _height!.action.ApplyBindingOverride(0, path);
            }
            // The native glyph set has no scroll icon. A plain label avoids
            // squeezing "Scroll Wheel/Y" into its keyboard-key sized container.
            result.Add(new ToolKeyTip { actionDesc = "Scroll to rotate" });
            result.Add(new ToolKeyTip { action = _height, actionDesc = "+ scroll to adjust height" });
        }
        if (!FoundationTool.RecyclerContext) result.Add(_menu);
        var wrapper = (Transform)Compatibility.Field(typeof(ToolKeyTipsUpdater), "tipWrapper").GetValue(_tips)!;
        var prefab = (ToolKeyTipUi)Compatibility.Field(typeof(ToolKeyTipsUpdater), "toolKeyTipPrefab").GetValue(_tips)!;
        foreach (var tip in result)
        {
            var row = UnityEngine.Object.Instantiate(prefab, wrapper, false); row.Setup(tip);
            if (tip.action == null && tip.actionKeyAlt == null)
            {
                row.GetComponentInChildren<UI.Common.KeyTipIcon>(true).gameObject.SetActive(false);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)row.transform);
            }
        }
        return false;
    }
    internal static void SetGhostColor(Material material, bool valid)
    {
        var color = valid ? ValidColor : InvalidColor;
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
    }
    internal static void Clear()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null; _tips = null; _notice = null; _track = _fill = null;
        _hintMode = ""; Hologram = null; _nextRetry = 0;
        if (_height != null) { UnityEngine.Object.Destroy(_height); _height = null; }
        if (_bindingAsset != null) { _bindingAsset.Disable(); UnityEngine.Object.Destroy(_bindingAsset); _bindingAsset = null; }
        _heightBinding = "";
        _canvas = null;
    }
}
