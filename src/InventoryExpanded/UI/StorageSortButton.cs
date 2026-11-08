using Backpack.Config;
using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppTMPro;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using TMPro;
#endif

namespace Backpack.UI;

public static class StorageSortButton
{
    private const float Gap = 8f;
    private const float CompactWidth = 50f;
    private const float CompactHeight = 34f;
    private static GameObject _root;
    private static RectTransform _rect;
    private static StorageMenu _menu;
    private static IItemSlotOwner _owner;
    private static int _createdForMenuId;
    private static int _openGeneration;
    private static int _geometryLoggedGeneration;
    private static int _visibleLoggedGeneration;
    private static string _lastDiagnosticSignature;

#if EXPERIMENTAL_UI_WORKSPACE
    internal static Transform ExperimentalDebugTransform => _root?.transform;
#endif

    public static void Show(StorageMenu menu, IItemSlotOwner owner)
    {
        if (!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures || menu == null || owner?.ItemSlots == null)
        {
            Hide();
            return;
        }

        _menu = menu;
        _owner = owner;
        _openGeneration++;
        _lastDiagnosticSignature = null;
        Logger.Diagnostic(
            $"[StorageSort] Show generation={_openGeneration}, menu=#{menu.GetInstanceID()}, " +
            $"title='{menu.TitleLabel?.text}', ownerSlots={owner.ItemSlots.Count}, enabled={Configuration.Instance.ShowSortButtons}.");
        RefreshVisibility();
    }

    public static void RefreshVisibility()
    {
        if (!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures)
        {
            Hide();
            return;
        }

        if (!Configuration.Instance.ShowSortButtons)
        {
            if (_root != null)
                _root.SetActive(false);
            LogDiagnostic("disabled by Show Sort Buttons");
            return;
        }

        var menu = _menu;
        if (menu == null || _owner?.ItemSlots == null)
        {
            LogDiagnostic($"missing context menu={(menu != null)} owner={(_owner != null)} slots={(_owner?.ItemSlots != null)}");
            return;
        }

        EnsureCreated(menu);
        if (_root == null)
        {
            LogDiagnostic("button creation unavailable");
            return;
        }

        if (!PositionRelativeToStorage(menu))
        {
            _root.SetActive(false);
            return;
        }
        _root.transform.SetAsLastSibling();
        _root.SetActive(true);
        LogVisible(menu);
    }

    public static void Update()
    {
        if (_root == null || _menu == null || !_menu.IsOpen || _owner?.ItemSlots == null
            || !Configuration.Instance.ShowSortButtons)
            return;

        // Native and modded storage grids can finish layout after StorageMenu.Open.
        // Following the live slot rectangles keeps the button below the rendered grid.
        if (PositionRelativeToStorage(_menu))
        {
            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
            LogVisible(_menu);
        }
    }

    public static bool TrySortActive()
    {
        if (!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures
            || _menu == null || !_menu.IsOpen || _owner?.ItemSlots == null)
            return false;

        return SortStorage();
    }

    public static bool TryGetOpenStorageOwner(out IItemSlotOwner owner, out StorageMenu menu)
    {
        menu = _menu;
        owner = _owner;
        return menu?.IsOpen == true && owner?.ItemSlots != null;
    }

    public static void Hide()
    {
        if (_root?.activeSelf == true)
            Logger.Diagnostic($"[StorageSort] Hide generation={_openGeneration}, title='{_menu?.TitleLabel?.text}'.");
        if (_root != null)
            _root.SetActive(false);

        _menu = null;
        _owner = null;
    }

    public static void ResetSession()
    {
        if (_root != null)
            UnityEngine.Object.Destroy(_root);

        _root = null;
        _rect = null;
        _menu = null;
        _owner = null;
        _createdForMenuId = 0;
        _openGeneration = 0;
        _geometryLoggedGeneration = 0;
        _visibleLoggedGeneration = 0;
        _lastDiagnosticSignature = null;
    }

    private static void EnsureCreated(StorageMenu menu)
    {
        var menuId = menu.GetInstanceID();
        if (_root != null && _createdForMenuId == menuId)
            return;

        if (_root != null)
            UnityEngine.Object.Destroy(_root);

        var canvasRect = menu.Canvas?.transform.GetComponent<RectTransform>();
        if (canvasRect == null || menu.SlotContainer == null || menu.TitleLabel == null)
        {
            LogDiagnostic(
                $"creation references canvas={(canvasRect != null)} slotContainer={(menu.SlotContainer != null)} title={(menu.TitleLabel != null)}");
            return;
        }

        _menu = menu;
        _createdForMenuId = menuId;
        _root = new GameObject("OGBackpack_StorageSortButton");
        _root.AddComponent<RectTransform>();
        _root.AddComponent<Image>();
        _root.AddComponent<Button>();
        var layout = _root.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        _rect = _root.GetComponent<RectTransform>();
        _rect.SetParent(canvasRect, false);

        var templateButton = menu.CloseButtonContainer?.GetComponentInChildren<Button>(true);
        var image = _root.GetComponent<Image>();
        var templateImage = templateButton?.GetComponent<Image>();
        if (templateImage != null)
        {
            image.sprite = templateImage.sprite;
            image.type = templateImage.type;
            image.material = templateImage.material;
        }
        image.color = new Color(0.12f, 0.14f, 0.16f, 0.96f);

        var button = _root.GetComponent<Button>();
        button.targetGraphic = image;
        if (templateButton != null)
            button.navigation = templateButton.navigation;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1.22f, 1.22f, 1.22f, 1f),
            pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f),
            selectedColor = new Color(1.22f, 1.22f, 1.22f, 1f),
            disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.55f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        Action onClick = () => SortStorage();
        button.onClick.AddListener(onClick);

        CreateLabel(menu, templateButton);
        _root.SetActive(false);
        Logger.Diagnostic(
            $"[StorageSort] Created button for menu=#{menuId}, parent='{canvasRect.name}', " +
            $"sibling={_root.transform.GetSiblingIndex()}.");
    }

    private static bool PositionRelativeToStorage(StorageMenu menu)
    {
        var parent = menu.SlotContainer;
        var slotUis = menu.SlotsUIs;
        if (parent == null || slotUis == null || _rect == null)
        {
            LogDiagnostic($"position references canvas={(parent != null)} slotArray={(slotUis != null)} rect={(_rect != null)}");
            return false;
        }

        if (!TryMeasureActiveStorageSlotBounds(
                menu,
                parent,
                null,
                out var bounds,
                out _,
                out var topRowCenterY,
                out _,
                out var metrics))
        {
            LogDiagnostic($"slot discovery pending {metrics}");
            return false;
        }

        _rect.SetParent(parent, false);
        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = new Vector2(CompactWidth, CompactHeight);
        var anchorOrigin = new Vector2(
            (0.5f - parent.pivot.x) * parent.rect.width,
            (0.5f - parent.pivot.y) * parent.rect.height);
        _rect.anchoredPosition = new Vector2(bounds.xMax + Gap + CompactWidth * 0.5f, topRowCenterY) - anchorOrigin;
        if (Logger.DiagnosticLoggingEnabled && _geometryLoggedGeneration != _openGeneration)
        {
            _geometryLoggedGeneration = _openGeneration;
            Logger.Diagnostic(
                $"[StorageSort] Positioned generation={_openGeneration}, {metrics}, " +
                $"bounds={bounds}, anchored=({_rect.anchoredPosition.x:F1},{_rect.anchoredPosition.y:F1}), " +
                $"size=({CompactWidth:F1},{CompactHeight:F1}), parent='{parent.name}', sibling={_root.transform.GetSiblingIndex()}.");
        }
        return true;
    }

    internal static bool TryMeasureActiveStorageSlotBounds(
        StorageMenu menu,
        RectTransform coordinateSpace,
        RectTransform secondaryCoordinateSpace,
        out Rect bounds,
        out Rect secondaryBounds,
        out float topRowCenterY,
        out int usableSlotCount,
        out string diagnostic)
    {
        bounds = default;
        secondaryBounds = default;
        topRowCenterY = float.NegativeInfinity;
        usableSlotCount = 0;

        var slotUis = menu?.SlotsUIs;
        var ownerSlots = _owner?.ItemSlots;
        var matchingOwner = menu != null && _menu == menu && ownerSlots != null;
        var rectCount = 0;
        var activeCount = 0;
        var assignedCount = 0;
        var ownerMatchedCount = 0;
        var readyRectCount = 0;
        var transformedReadyCount = 0;
        var hasBounds = false;
        var hasSecondaryBounds = false;

        if (matchingOwner && coordinateSpace != null && slotUis != null)
        {
            for (var i = 0; i < slotUis.Length; i++)
            {
                var slotUi = slotUis[i];
                var slotRect = slotUi?.Rect;
                if (slotRect == null)
                    continue;
                rectCount++;

                var isActive = slotUi.gameObject.activeInHierarchy;
                if (isActive)
                    activeCount++;
                var assignedSlot = slotUi.assignedSlot;
                if (assignedSlot != null)
                    assignedCount++;
                if (!isActive || assignedSlot == null || !OwnerContainsSlot(assignedSlot))
                    continue;
                ownerMatchedCount++;

                var localRect = slotRect.rect;
                if (localRect.width <= 1f || localRect.height <= 1f)
                    continue;
                readyRectCount++;

                if (!TryTransformRectBounds(slotRect, localRect, coordinateSpace, out var slotBounds))
                    continue;

                var slotSecondaryBounds = default(Rect);
                if (secondaryCoordinateSpace != null
                    && !TryTransformRectBounds(slotRect, localRect, secondaryCoordinateSpace, out slotSecondaryBounds))
                    continue;

                if (slotBounds.width <= 1f || slotBounds.height <= 1f)
                    continue;

                transformedReadyCount++;
                bounds = hasBounds ? Union(bounds, slotBounds) : slotBounds;
                hasBounds = true;
                if (secondaryCoordinateSpace != null)
                {
                    secondaryBounds = hasSecondaryBounds ? Union(secondaryBounds, slotSecondaryBounds) : slotSecondaryBounds;
                    hasSecondaryBounds = true;
                }

                topRowCenterY = Mathf.Max(topRowCenterY, slotBounds.center.y);
                usableSlotCount++;
            }
        }

        diagnostic =
            $"candidates={slotUis?.Length ?? 0}, rects={rectCount}, active={activeCount}, assigned={assignedCount}, " +
            $"ownerMatched={ownerMatchedCount}, readyRects={readyRectCount}, transformedReady={transformedReadyCount}, " +
            $"usable={usableSlotCount}, ownerReady={matchingOwner}, space='{coordinateSpace?.name}' rect={coordinateSpace?.rect}, " +
            $"secondarySpace='{secondaryCoordinateSpace?.name}' rect={secondaryCoordinateSpace?.rect}";
        return hasBounds && (secondaryCoordinateSpace == null || hasSecondaryBounds);
    }

    private static bool TryTransformRectBounds(
        RectTransform rectTransform,
        Rect localRect,
        RectTransform coordinateSpace,
        out Rect bounds)
    {
        bounds = default;
        if (rectTransform == null || coordinateSpace == null)
            return false;

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var corner = 0; corner < 4; corner++)
        {
            var localPoint = corner switch
            {
                0 => new Vector3(localRect.xMin, localRect.yMin, 0f),
                1 => new Vector3(localRect.xMin, localRect.yMax, 0f),
                2 => new Vector3(localRect.xMax, localRect.yMax, 0f),
                _ => new Vector3(localRect.xMax, localRect.yMin, 0f)
            };
            // This is the proven IL2CPP-safe path used for sort-button placement.
            var local = coordinateSpace.InverseTransformPoint(rectTransform.TransformPoint(localPoint));
            min = Vector2.Min(min, new Vector2(local.x, local.y));
            max = Vector2.Max(max, new Vector2(local.x, local.y));
        }

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return bounds.width > 1f && bounds.height > 1f;
    }

    private static Rect Union(Rect first, Rect second)
    {
        return Rect.MinMaxRect(
            Mathf.Min(first.xMin, second.xMin),
            Mathf.Min(first.yMin, second.yMin),
            Mathf.Max(first.xMax, second.xMax),
            Mathf.Max(first.yMax, second.yMax));
    }

    private static bool OwnerContainsSlot(ItemSlot candidate)
    {
        var slots = _owner?.ItemSlots;
        if (slots == null || candidate == null)
            return false;

        for (var i = 0; i < slots.Count; i++)
        {
#if IL2CPP
            var ownerSlot = slots[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var ownerSlot = slots[i];
#endif
            if (PlayerBackpack.SlotsMatch(ownerSlot, candidate))
                return true;
        }

        return false;
    }

    private static void LogDiagnostic(string detail)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        var signature = $"{_openGeneration}:{detail}";
        if (signature == _lastDiagnosticSignature)
            return;

        _lastDiagnosticSignature = signature;
        Logger.Diagnostic($"[StorageSort] generation={_openGeneration} inactive: {detail}.");
    }

    private static void LogVisible(StorageMenu menu)
    {
        if (!Logger.DiagnosticLoggingEnabled || _visibleLoggedGeneration == _openGeneration)
            return;

        _visibleLoggedGeneration = _openGeneration;
        Logger.Diagnostic(
            $"[StorageSort] Visible generation={_openGeneration}, activeSelf={_root?.activeSelf}, " +
            $"activeInHierarchy={_root?.activeInHierarchy}, menuOpen={menu?.IsOpen}, " +
            $"parent='{_rect?.parent?.name}', sibling={_root?.transform.GetSiblingIndex()}.");
    }

    private static bool SortStorage()
    {
        if (!GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures)
            return false;

        var itemUiManager = Singleton<ItemUIManager>.Instance;
        var slots = _owner?.ItemSlots;
        if (slots == null || itemUiManager?.IsCurrentlyDragging == true)
            return false;

        if (!InventorySorter.SortStorage(slots, _owner))
            return false;

        if (_menu?.SlotsUIs != null)
        {
            for (var i = 0; i < _menu.SlotsUIs.Length; i++)
                _menu.SlotsUIs[i]?.UpdateUI();
        }

        return true;
    }

    private static void CreateLabel(StorageMenu menu, Button templateButton)
    {
        var labelObject = new GameObject("Label");
        labelObject.AddComponent<RectTransform>();
        labelObject.AddComponent<TextMeshProUGUI>();
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(_rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var templateLabel = templateButton?.GetComponentInChildren<TextMeshProUGUI>(true);
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = templateLabel?.font ?? menu.TitleLabel.font;
        label.fontSize = 12f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = templateLabel?.color ?? Color.white;
        label.text = "SORT";
    }
}
