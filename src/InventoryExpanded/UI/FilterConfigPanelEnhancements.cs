using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#if IL2CPP
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI.Items;
using Il2CppTMPro;
using StringList = Il2CppSystem.Collections.Generic.List<string>;
#elif MONO
using ScheduleOne.ItemFramework;
using ScheduleOne.UI.Items;
using TMPro;
using StringList = System.Collections.Generic.List<string>;
#endif

namespace Backpack.UI;

public static class FilterConfigPanelEnhancements
{
    private const float QuickActionsWidth = 196f;
    private const float QuickActionsHeight = 148f;
    private const float PanelGap = 12f;

    private static FilterConfigPanel _panel;
    private static GameObject _root;
    private static RectTransform _rootRect;
    private static Button _useCurrentItemButton;
    private static Button _copyFilterButton;
    private static Button _pasteFilterButton;

    public static void Update()
    {
        var panel = GetPanel();
        if (panel == null || !panel.IsOpen)
        {
            if (_root != null)
                _root.SetActive(false);
            return;
        }

        EnsureCreated(panel);
        if (_root == null)
            return;

        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        RefreshButtonStates(panel);
    }

    public static void ResetSession()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);

        _panel = null;
        _root = null;
        _rootRect = null;
        _useCurrentItemButton = null;
        _copyFilterButton = null;
        _pasteFilterButton = null;
    }

    private static FilterConfigPanel GetPanel()
    {
#if IL2CPP
        return Il2CppScheduleOne.DevUtilities.Singleton<ItemUIManager>.Instance?.FilterConfigPanel;
#elif MONO
        return ScheduleOne.DevUtilities.Singleton<ItemUIManager>.Instance?.FilterConfigPanel;
#endif
    }

    private static void EnsureCreated(FilterConfigPanel panel)
    {
        if (_root != null && _panel != null && _panel.GetInstanceID() == panel.GetInstanceID())
            return;

        if (_root != null)
            UnityEngine.Object.Destroy(_root);

        _panel = panel;
        var template = panel.TypeLabel ?? panel.ListLabel;
        if (panel.Rect == null || template == null)
        {
            Logger.Warning("Native filter panel has no usable Rect or text template; quick filter actions were not created.");
            return;
        }

        _root = new GameObject("OGBackpack_FilterQuickActions");
        _root.AddComponent<RectTransform>();
        _root.AddComponent<Image>();
        _rootRect = _root.GetComponent<RectTransform>();
        _rootRect.SetParent(panel.Rect, false);
        _rootRect.anchorMin = new Vector2(0f, 0.5f);
        _rootRect.anchorMax = new Vector2(0f, 0.5f);
        _rootRect.pivot = new Vector2(1f, 0.5f);
        _rootRect.sizeDelta = new Vector2(QuickActionsWidth, QuickActionsHeight);
        _rootRect.anchoredPosition = new Vector2(-PanelGap, 0f);
        _root.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.035f, 0.98f);

        _useCurrentItemButton = CreateButton(_rootRect, "Use Current Item", 48f, template, () => UseCurrentItem(panel));
        _copyFilterButton = CreateButton(_rootRect, "Copy Filter", 0f, template, () => CopyFilter(panel));
        _pasteFilterButton = CreateButton(_rootRect, "Paste Filter", -48f, template, () => PasteFilter(panel));
    }

    private static Button CreateButton(RectTransform parent, string text, float y, TextMeshProUGUI template, Action onClick)
    {
        var obj = new GameObject("Button_" + text.Replace(" ", string.Empty));
        obj.AddComponent<RectTransform>();
        obj.AddComponent<Image>();
        obj.AddComponent<Button>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(172f, 38f);
        rect.anchoredPosition = new Vector2(0f, y);
        obj.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.1f, 0.98f);

        var button = obj.GetComponent<Button>();
        button.onClick.AddListener((UnityAction) onClick);

        var labelObject = new GameObject("Label");
        labelObject.AddComponent<RectTransform>();
        labelObject.AddComponent<TextMeshProUGUI>();
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 2f);
        labelRect.offsetMax = new Vector2(-6f, -2f);
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = template.font;
        label.fontSize = 16f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.text = text;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return button;
    }

    private static void UseCurrentItem(FilterConfigPanel panel)
    {
        try
        {
            var slot = panel.OpenSlot;
            var definition = slot?.ItemInstance?.Definition;
            if (slot == null || definition == null || !definition.UsableInFilters || string.IsNullOrEmpty(definition.ID))
                return;

            var filter = slot.PlayerFilter?.Clone() ?? new SlotFilter();
            filter.Type = SlotFilter.EType.Whitelist;
            filter.ItemIDs ??= new StringList();
            filter.ItemIDs.Clear();
            filter.ItemIDs.Add(definition.ID);

            slot.SetPlayerFilter(filter, false);
            panel.RefreshDisplay();
            RefreshButtonStates(panel);
        }
        catch (Exception exception)
        {
            Logger.Error("Could not set the current item as the slot filter: " + exception);
        }
    }

    private static void CopyFilter(FilterConfigPanel panel)
    {
        panel.CopyClicked();
        RefreshButtonStates(panel);
    }

    private static void PasteFilter(FilterConfigPanel panel)
    {
        panel.PasteClicked();
        RefreshButtonStates(panel);
    }

    private static void RefreshButtonStates(FilterConfigPanel panel)
    {
        var definition = panel.OpenSlot?.ItemInstance?.Definition;
        if (_useCurrentItemButton != null)
            _useCurrentItemButton.interactable = definition != null && definition.UsableInFilters && !string.IsNullOrEmpty(definition.ID);
        if (_copyFilterButton != null)
            _copyFilterButton.interactable = panel.CopyButton == null || panel.CopyButton.interactable;
        if (_pasteFilterButton != null)
            _pasteFilterButton.interactable = panel.PasteButton != null
                ? panel.PasteButton.interactable
                : FilterConfigPanel.copiedFilter != null;
    }
}
