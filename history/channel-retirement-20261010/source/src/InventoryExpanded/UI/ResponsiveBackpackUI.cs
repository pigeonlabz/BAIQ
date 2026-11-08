using Backpack.Config;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppTMPro;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using TMPro;
#endif

namespace Backpack.UI;

public static class ResponsiveBackpackUI
{
    private const int DefaultMaxColumns = 16;
    private const int DefaultMaxRows = 8;
    private const float MaxScreenWidthUsage = 0.84f;
    private const float MaxScreenHeightUsage = 0.68f;
    private const float ElementGap = 12f;
    private const float TitleHeight = 34f;
    private const float SearchHeight = 36f;
    private const float ControlsHeight = 42f;

    private static readonly List<ItemSlotUI> SlotUis = new();
    private static StorageMenu _activeMenu;
    private static GameObject _root;
    private static RectTransform _rootRect;
    private static RectTransform _slotContainer;
    private static GridLayoutGroup _slotGrid;
    private static RectTransform _controlsRect;
    private static BackpackSearchBar _searchBar;
    private static TextMeshProUGUI _pageLabel;
    private static Button _previousButton;
    private static Button _nextButton;
    private static Button _sortButton;
    private static RectTransform _titleRect;
    private static RectTransform _doneRect;
    private static int _createdForMenuId;

    private static bool _nativeSlotContainerWasActive;
    private static bool _nativeTitleWasActive;
    private static bool _nativeSubtitleWasActive;
    private static Transform _doneOriginalParent;
    private static int _doneOriginalSiblingIndex;
    private static Vector2 _doneOriginalAnchorMin;
    private static Vector2 _doneOriginalAnchorMax;
    private static Vector2 _doneOriginalPivot;
    private static Vector2 _doneOriginalSize;
    private static Vector2 _doneOriginalPosition;

    private static int _currentPage;
    private static int _visibleColumns = 1;
    private static int _visibleRows = 1;
    private static int _visibleSlots = 1;
    private static int _totalPages = 1;
    private static Vector2 _lastLayoutParentSize;
    private static Vector2 _lastLayoutCellSize;
    private static Vector2 _lastLayoutSpacing;
    private static float _lastLayoutDoneHeight;
    private static bool _layoutInitialized;

    public static void Open(StorageMenu menu, double nativeOpenMs = 0d)
    {
        if (menu == null || PlayerBackpack.Instance == null)
            return;

        var menuId = menu.GetInstanceID();
        if (_createdForMenuId != 0 && _createdForMenuId != menuId)
            ResetSession($"StorageMenu changed {_createdForMenuId}->{menuId}");

        // StorageMenu overloads can delegate to one another; only capture native state once.
        if (_activeMenu == menu && _root != null && _root.activeSelf)
            return;

        if (_activeMenu != null && _activeMenu != menu)
            Close(_activeMenu);

        var timer = Stopwatch.StartNew();
        _activeMenu = menu;
        EnsureCreated(menu);
        var ensureMs = timer.Elapsed.TotalMilliseconds;
        if (_root == null)
            return;

        CaptureAndHideNativeLayout(menu);
        var captureMs = timer.Elapsed.TotalMilliseconds - ensureMs;
        RecalculateLayout(menu);
        var layoutMs = timer.Elapsed.TotalMilliseconds - ensureMs - captureMs;
        var slotsBefore = SlotUis.Count;
        EnsureSlotUis(menu, _visibleSlots);
        var wrappersMs = timer.Elapsed.TotalMilliseconds - ensureMs - captureMs - layoutMs;
        ApplyPage();
        var bindMs = timer.Elapsed.TotalMilliseconds - ensureMs - captureMs - layoutMs - wrappersMs;
        _root.SetActive(true);
        var activateMs = timer.Elapsed.TotalMilliseconds - ensureMs - captureMs - layoutMs - wrappersMs - bindMs;
        Logger.Diagnostic(
            $"[BackpackPerf] open total={timer.Elapsed.TotalMilliseconds + nativeOpenMs:F1}ms capacity={PlayerBackpack.Instance.BackpackSlotCount} " +
            $"rendered={_visibleSlots} created={SlotUis.Count - slotsBefore} native={nativeOpenMs:F1}ms ensure={ensureMs:F1}ms capture={captureMs:F1}ms " +
            $"activate={activateMs:F1}ms layout={layoutMs:F1}ms wrappers={wrappersMs:F1}ms bind={bindMs:F1}ms.");
    }

    public static void Close(StorageMenu menu)
    {
        if (_activeMenu != menu)
            return;

        var timer = Stopwatch.StartNew();
        if (_root != null)
            _root.SetActive(false);

        RestoreNativeLayout(menu);
        _activeMenu = null;
        _currentPage = 0;
        _searchBar?.Reset();
        Logger.Diagnostic($"[BackpackPerf] close total={timer.Elapsed.TotalMilliseconds:F1}ms capacity={PlayerBackpack.Instance?.BackpackSlotCount ?? 0}.");
    }

    public static void CloseActive()
    {
        if (_activeMenu != null)
            Close(_activeMenu);
    }

    public static void ResetSession(string reason)
    {
        var trackedSlots = SlotUis.Count;
        var liveSlots = CountLiveSlotUis();

        try
        {
            if (_activeMenu != null)
                RestoreNativeLayout(_activeMenu);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Standalone native layout cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }
        catch
        {
            // Scene teardown may already have destroyed the native canvas hierarchy.
        }

        SlotUis.Clear();
        _activeMenu = null;
        _root = null;
        _rootRect = null;
        _slotContainer = null;
        _slotGrid = null;
        _controlsRect = null;
        _searchBar = null;
        _pageLabel = null;
        _previousButton = null;
        _nextButton = null;
        _sortButton = null;
        _titleRect = null;
        _doneRect = null;
        _doneOriginalParent = null;
        _createdForMenuId = 0;
        _nativeSlotContainerWasActive = false;
        _nativeTitleWasActive = false;
        _nativeSubtitleWasActive = false;
        _currentPage = 0;
        _visibleColumns = 1;
        _visibleRows = 1;
        _visibleSlots = 1;
        _totalPages = 1;
        _lastLayoutParentSize = Vector2.zero;
        _lastLayoutCellSize = Vector2.zero;
        _lastLayoutSpacing = Vector2.zero;
        _lastLayoutDoneHeight = 0f;
        _layoutInitialized = false;

        if (trackedSlots > 0 || liveSlots > 0)
            Logger.Diagnostic($"[Lifecycle] Standalone Backpack UI reset ({reason}); trackedSlots={trackedSlots}, liveSlots={liveSlots}.");
    }

    public static void Update()
    {
        if (_activeMenu == null || PlayerBackpack.Instance == null)
            return;

        if (!PlayerBackpack.Instance.IsOpen)
        {
            Close(_activeMenu);
            return;
        }

        var itemUiManager = Singleton<ItemUIManager>.Instance;
        if (_searchBar?.IsFiltering == true && itemUiManager?.IsCurrentlyDragging != true && _searchBar.RefreshProjection(PlayerBackpack.Instance))
            ApplyPage();

        var scroll = Input.mouseScrollDelta.y;
        if (scroll > 0.01f)
            PreviousPage();
        else if (scroll < -0.01f)
            NextPage();
    }

    public static void Refresh()
    {
        if (_activeMenu == null)
            return;

        if (_sortButton != null)
            _sortButton.gameObject.SetActive(Configuration.Instance.ShowSortButtons);
        RecalculateLayout(_activeMenu);
        EnsureSlotUis(_activeMenu, _visibleSlots);
        ApplyPage();
    }

    public static void InvalidateLayout()
    {
        _layoutInitialized = false;
        Refresh();
    }

    public static bool ContainsSlotUi(ItemSlotUI candidate)
    {
        if (candidate == null)
            return false;

        for (var i = 0; i < SlotUis.Count; i++)
        {
            if (SlotUis[i] != null && SlotUis[i].GetInstanceID() == candidate.GetInstanceID())
                return true;
        }

        return false;
    }

    public static bool ContainsSlotRect(RectTransform candidate)
    {
        if (candidate == null)
            return false;

        for (var i = 0; i < SlotUis.Count; i++)
        {
            var rect = SlotUis[i]?.Rect;
            if (rect != null && rect.GetInstanceID() == candidate.GetInstanceID())
                return true;
        }

        return false;
    }

    private static void EnsureCreated(StorageMenu menu)
    {
        var menuId = menu.GetInstanceID();
        if (_root != null && _rootRect != null && _slotContainer != null && _createdForMenuId == menuId)
            return;

        if (_root != null || SlotUis.Count > 0 || _createdForMenuId != 0)
            ResetSession("standalone UI hierarchy was stale");

        var itemUiManager = Singleton<ItemUIManager>.Instance;
        if (itemUiManager?.ItemSlotUIPrefab == null)
        {
            Logger.Error("ItemUIManager has no slot prefab. Cannot create the backpack UI.");
            return;
        }

        _root = new GameObject("OGBackpack_MainLayout");
        _root.AddComponent<RectTransform>();
        _rootRect = _root.GetComponent<RectTransform>();
        _rootRect.SetParent(menu.Container, false);
        _rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        _rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        _rootRect.pivot = new Vector2(0.5f, 0.5f);
        _rootRect.anchoredPosition = Vector2.zero;
        _createdForMenuId = menuId;
        Logger.Diagnostic(
            $"[Lifecycle] Creating standalone Backpack UI for StorageMenu#{menuId}, " +
            $"ItemUIManager#{itemUiManager.GetInstanceID()}.");

        _titleRect = CreateLabel("Title", _rootRect, "Backpack", menu.TitleLabel, 320f, TitleHeight, 24f).rectTransform;
        _searchBar = BackpackSearchBar.Create("Search", _rootRect, menu.TitleLabel, SearchChanged);

        var slots = new GameObject("Slots");
        slots.AddComponent<RectTransform>();
        slots.AddComponent<GridLayoutGroup>();
        _slotContainer = slots.GetComponent<RectTransform>();
        _slotContainer.SetParent(_rootRect, false);
        _slotContainer.anchorMin = new Vector2(0.5f, 0.5f);
        _slotContainer.anchorMax = new Vector2(0.5f, 0.5f);
        _slotContainer.pivot = new Vector2(0.5f, 0.5f);

        _slotGrid = slots.GetComponent<GridLayoutGroup>();
        _slotGrid.cellSize = menu.SlotGridLayout.cellSize;
        _slotGrid.spacing = menu.SlotGridLayout.spacing;
        _slotGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _slotGrid.childAlignment = TextAnchor.UpperLeft;

        var controls = new GameObject("Controls");
        controls.AddComponent<RectTransform>();
        _controlsRect = controls.GetComponent<RectTransform>();
        _controlsRect.SetParent(_rootRect, false);
        _controlsRect.anchorMin = new Vector2(0.5f, 0.5f);
        _controlsRect.anchorMax = new Vector2(0.5f, 0.5f);
        _controlsRect.pivot = new Vector2(0.5f, 0.5f);
        _controlsRect.sizeDelta = new Vector2(520f, ControlsHeight);

        _previousButton = CreateButton("Previous", _controlsRect, new Vector2(-180f, 0f), PreviousPage, menu.TitleLabel, 112f);
        _pageLabel = CreateLabel("PageLabel", _controlsRect, "1 / 1", menu.TitleLabel, 90f, 30f, 20f);
        _pageLabel.rectTransform.anchoredPosition = new Vector2(-30f, 0f);
        _nextButton = CreateButton("Next", _controlsRect, new Vector2(110f, 0f), NextPage, menu.TitleLabel, 96f);
        _sortButton = CreateButton("Sort", _controlsRect, new Vector2(230f, 0f), () =>
        {
            PlayerBackpack.Instance?.SortContents();
        }, menu.TitleLabel, 90f);
        _sortButton.gameObject.SetActive(Configuration.Instance.ShowSortButtons);

        _root.SetActive(false);
    }

    private static void CaptureAndHideNativeLayout(StorageMenu menu)
    {
        _nativeSlotContainerWasActive = menu.SlotContainer.gameObject.activeSelf;
        _nativeTitleWasActive = menu.TitleLabel != null && menu.TitleLabel.gameObject.activeSelf;
        _nativeSubtitleWasActive = menu.SubtitleLabel != null && menu.SubtitleLabel.gameObject.activeSelf;

        menu.SlotContainer.gameObject.SetActive(false);
        menu.TitleLabel?.gameObject.SetActive(false);
        menu.SubtitleLabel?.gameObject.SetActive(false);

        _doneRect = menu.CloseButtonContainer;
        if (_doneRect == null)
            return;

        _doneOriginalParent = _doneRect.parent;
        _doneOriginalSiblingIndex = _doneRect.GetSiblingIndex();
        _doneOriginalAnchorMin = _doneRect.anchorMin;
        _doneOriginalAnchorMax = _doneRect.anchorMax;
        _doneOriginalPivot = _doneRect.pivot;
        _doneOriginalSize = _doneRect.sizeDelta;
        _doneOriginalPosition = _doneRect.anchoredPosition;
        _doneRect.SetParent(_rootRect, false);
        _doneRect.anchorMin = new Vector2(0.5f, 0.5f);
        _doneRect.anchorMax = new Vector2(0.5f, 0.5f);
        _doneRect.pivot = new Vector2(0.5f, 0.5f);
        _doneRect.SetAsLastSibling();
    }

    private static void RestoreNativeLayout(StorageMenu menu)
    {
        menu.SlotContainer.gameObject.SetActive(_nativeSlotContainerWasActive);
        if (menu.TitleLabel != null)
            menu.TitleLabel.gameObject.SetActive(_nativeTitleWasActive);
        if (menu.SubtitleLabel != null)
            menu.SubtitleLabel.gameObject.SetActive(_nativeSubtitleWasActive);

        if (_doneRect == null || _doneOriginalParent == null)
            return;

        _doneRect.SetParent(_doneOriginalParent, false);
        _doneRect.SetSiblingIndex(_doneOriginalSiblingIndex);
        _doneRect.anchorMin = _doneOriginalAnchorMin;
        _doneRect.anchorMax = _doneOriginalAnchorMax;
        _doneRect.pivot = _doneOriginalPivot;
        _doneRect.sizeDelta = _doneOriginalSize;
        _doneRect.anchoredPosition = _doneOriginalPosition;
        _doneRect = null;
        _doneOriginalParent = null;
    }

    private static void RecalculateLayout(StorageMenu menu)
    {
        var cell = _slotGrid.cellSize;
        var spacing = _slotGrid.spacing;
        var parentSize = menu.Container.rect.size;
        var availableWidth = parentSize.x > 1f ? parentSize.x : Screen.width;
        var availableHeight = parentSize.y > 1f ? parentSize.y : Screen.height;
        var maxColumns = Mathf.Max(1, Mathf.FloorToInt((availableWidth * MaxScreenWidthUsage + spacing.x) / (cell.x + spacing.x)));
        var maxRows = Mathf.Max(1, Mathf.FloorToInt((availableHeight * MaxScreenHeightUsage + spacing.y) / (cell.y + spacing.y)));
        var preferredColumns = Configuration.Instance.PreferredColumns;
        var configuredSlots = PlayerBackpack.Instance.BackpackSlotCount;

        _visibleColumns = preferredColumns > 0
            ? Mathf.Clamp(preferredColumns, 1, maxColumns)
            : Mathf.Clamp(Mathf.Min(DefaultMaxColumns, configuredSlots), 1, maxColumns);
        _visibleRows = Mathf.Clamp(DefaultMaxRows, 1, maxRows);
        _visibleSlots = Mathf.Max(1, _visibleColumns * _visibleRows);
        var gridSize = new Vector2(
            _visibleColumns * cell.x + Mathf.Max(0, _visibleColumns - 1) * spacing.x,
            _visibleRows * cell.y + Mathf.Max(0, _visibleRows - 1) * spacing.y);
        var doneHeight = _doneRect != null && _doneRect.sizeDelta.y > 1f ? _doneRect.sizeDelta.y : 48f;
        var totalHeight = TitleHeight + ElementGap + SearchHeight + ElementGap + gridSize.y + ElementGap + ControlsHeight + ElementGap + doneHeight;
        var geometryChanged = !_layoutInitialized
                              || _lastLayoutParentSize != parentSize
                              || _lastLayoutCellSize != cell
                              || _lastLayoutSpacing != spacing
                              || !Mathf.Approximately(_lastLayoutDoneHeight, doneHeight)
                              || _slotGrid.constraintCount != _visibleColumns
                              || _slotContainer.sizeDelta != gridSize;

        if (!geometryChanged)
        {
            if (_doneRect != null)
                _doneRect.anchoredPosition = new Vector2(0f, -totalHeight * 0.5f + doneHeight * 0.5f);
            return;
        }

        _slotGrid.constraintCount = _visibleColumns;
        _slotContainer.sizeDelta = gridSize;
        _rootRect.sizeDelta = new Vector2(Mathf.Max(gridSize.x, 520f), totalHeight);

        var nextY = totalHeight * 0.5f;
        _titleRect.anchoredPosition = new Vector2(0f, nextY - TitleHeight * 0.5f);
        nextY -= TitleHeight + ElementGap;
        _searchBar.Rect.sizeDelta = new Vector2(Mathf.Min(Mathf.Max(gridSize.x, 520f), 680f), SearchHeight);
        _searchBar.Rect.anchoredPosition = new Vector2(0f, nextY - SearchHeight * 0.5f);
        nextY -= SearchHeight + ElementGap;
        _slotContainer.anchoredPosition = new Vector2(0f, nextY - gridSize.y * 0.5f);
        nextY -= gridSize.y + ElementGap;
        _controlsRect.anchoredPosition = new Vector2(0f, nextY - ControlsHeight * 0.5f);
        nextY -= ControlsHeight + ElementGap;
        if (_doneRect != null)
            _doneRect.anchoredPosition = new Vector2(0f, nextY - doneHeight * 0.5f);

        _lastLayoutParentSize = parentSize;
        _lastLayoutCellSize = cell;
        _lastLayoutSpacing = spacing;
        _lastLayoutDoneHeight = doneHeight;
        _layoutInitialized = true;
    }

    private static void EnsureSlotUis(StorageMenu menu, int count)
    {
        if (_slotContainer == null)
            return;

        if (SlotUis.Count >= count && _slotContainer.childCount >= SlotUis.Count)
            return;

        var staleSlots = false;
        for (var i = 0; i < SlotUis.Count; i++)
        {
            if (SlotUis[i] == null)
            {
                staleSlots = true;
                break;
            }
        }

        var liveSlots = CountLiveSlotUis();
        if (staleSlots || _slotContainer.childCount < liveSlots)
        {
            Logger.Warning(
                $"[Lifecycle] Rebuilding stale standalone slot UI list; tracked={SlotUis.Count}, " +
                $"live={liveSlots}, children={_slotContainer.childCount}.");
            for (var i = 0; i < SlotUis.Count; i++)
            {
                if (SlotUis[i] != null)
                    UnityEngine.Object.Destroy(SlotUis[i].gameObject);
            }

            SlotUis.Clear();
        }

        var itemUiManager = Singleton<ItemUIManager>.Instance;
        if (itemUiManager?.ItemSlotUIPrefab == null)
            return;

        var prefab = itemUiManager.ItemSlotUIPrefab.gameObject;
        while (SlotUis.Count < count)
        {
            var slot = UnityEngine.Object.Instantiate(prefab, _slotContainer);
            slot.name = "MainBackpackSlot_" + SlotUis.Count;
            slot.SetActive(true);
            var slotUi = slot.GetComponent<ItemSlotUI>();
            slotUi.ClearSlot();
            SlotUis.Add(slotUi);
        }
    }

    private static int CountLiveSlotUis()
    {
        var live = 0;
        for (var i = 0; i < SlotUis.Count; i++)
        {
            if (SlotUis[i] != null)
                live++;
        }

        return live;
    }

    private static void ApplyPage()
    {
        if (_activeMenu == null || PlayerBackpack.Instance == null)
            return;

        UpdateProjectionAndPagination();
        var start = _currentPage * _visibleSlots;
        for (var i = 0; i < SlotUis.Count; i++)
        {
            var slotUI = SlotUis[i];
            if (slotUI == null)
                continue;

            var projectedIndex = start + i;
            var slotIndex = _searchBar.GetInventorySlotIndex(projectedIndex);
            var shouldShow = i < _visibleSlots && slotIndex >= 0;
            if (!shouldShow)
            {
                if (slotUI.assignedSlot != null)
                    slotUI.ClearSlot();
                slotUI.gameObject.SetActive(false);
                continue;
            }

            var targetSlot = PlayerBackpack.Instance.GetBackpackSlot(slotIndex);
            slotUI.gameObject.SetActive(true);
            if (!PlayerBackpack.SlotsMatch(slotUI.assignedSlot, targetSlot))
            {
                if (slotUI.assignedSlot != null)
                    slotUI.ClearSlot();
                slotUI.AssignSlot(targetSlot);
            }
            slotUI.SetVisible(true);
        }

        _pageLabel.text = _searchBar.IsFiltering && _searchBar.ProjectedCount == 0
            ? "No results"
            : $"{_currentPage + 1} / {_totalPages}";
        _previousButton.interactable = _currentPage > 0;
        _nextButton.interactable = _currentPage < _totalPages - 1;

    }

    private static void ClearSlotBindings()
    {
        for (var i = 0; i < SlotUis.Count; i++)
            SlotUis[i]?.ClearSlot();
    }

    private static void SearchChanged()
    {
        _currentPage = 0;
        Refresh();
    }

    private static void UpdateProjectionAndPagination()
    {
        _searchBar.RefreshProjection(PlayerBackpack.Instance);
        _totalPages = Mathf.Max(1, Mathf.CeilToInt(_searchBar.ProjectedCount / (float) _visibleSlots));
        _currentPage = Mathf.Clamp(_currentPage, 0, _totalPages - 1);
    }

    private static void PreviousPage()
    {
        if (_currentPage <= 0)
            return;

        _currentPage--;
        ApplyPage();
    }

    private static void NextPage()
    {
        if (_currentPage >= _totalPages - 1)
            return;

        _currentPage++;
        ApplyPage();
    }

    private static Button CreateButton(string text, RectTransform parent, Vector2 position, Action onClick, TextMeshProUGUI template, float width)
    {
        var obj = new GameObject("Button_" + text);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<Image>();
        obj.AddComponent<Button>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, 34f);
        rect.anchoredPosition = position;

        obj.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.1f, 0.92f);
        var button = obj.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        CreateLabel("Label", rect, text, template, width - 10f, 30f, 18f);
        return button;
    }

    private static TextMeshProUGUI CreateLabel(string name, RectTransform parent, string text, TextMeshProUGUI template, float width, float height, float fontSize)
    {
        var obj = new GameObject(name);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<TextMeshProUGUI>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);

        var label = obj.GetComponent<TextMeshProUGUI>();
        label.font = template.font;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.text = text;
        return label;
    }
}
