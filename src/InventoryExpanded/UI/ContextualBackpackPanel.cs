using Backpack.Config;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppScheduleOne.UI.Phone;
using Il2CppTMPro;
using SlotList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using ScheduleOne.UI.Phone;
using TMPro;
using SlotList = System.Collections.Generic.List<ScheduleOne.ItemFramework.ItemSlot>;
#endif

namespace Backpack.UI;

public static class ContextualBackpackPanel
{
    private const float HeaderHeight = 30f;
    private const float SearchHeight = 34f;
    private const float SearchGap = 2f;
    private const float FooterHeight = 34f;
    private const float HorizontalPadding = 6f;
    private const float GridVerticalPadding = 4f;
    private const float GridSpacing = 4f;
    private const float CanvasSafetyMargin = 12f;
    private const float ResizeHandleSize = 24f;
    private const float MinWidth = 380f;
    private const float MinHeight = 300f;

    private enum ResizeCorner
    {
        None,
        BottomRight,
        TopLeft
    }

    private enum ContextMode
    {
        StorageOrMachine,
        Phone
    }

    private sealed class ActiveInteraction
    {
        public object Context { get; init; }
        public object ExternalEndpoint { get; init; }
        public int ContextId { get; init; }
        public int EndpointId { get; init; }
        public ContextMode Mode { get; init; }
    }

    private static GameObject _root;
    private static RectTransform _rect;
    private static RectTransform _parentRect;
    private static Canvas _panelCanvas;
    private static GraphicRaycaster _panelRaycaster;
    private static ItemUIManager _registeredItemUiManager;
    private static int _createdForItemUiManagerId;
    private static RectTransform _headerRect;
    private static RectTransform _resizeHandleRect;
    private static RectTransform _topLeftResizeHandleRect;
    private static BackpackSearchBar _searchBar;
    private static GridLayoutGroup _grid;
    private static RectTransform _slotContainer;
    private static TextMeshProUGUI _pageLabel;
    private static Button _previousButton;
    private static Button _nextButton;
    private static Button _sortButton;
    private static readonly List<ItemSlotUI> SlotUis = new();
    private static ActiveInteraction _activeInteraction;
    private static bool _isOpen;
    private static bool _isDragging;
    private static bool _isResizing;
    private static ResizeCorner _resizeCorner;
    private static Vector2 _dragGrabOffset;
    private static Vector2 _resizeGrabOffset;
    private static Vector2 _resizeOppositeCorner;
    private static Vector2 _lastCanvasSize;
    private static int _currentPage;
    private static int _visibleColumns = 1;
    private static int _visibleRows = 1;
    private static int _visibleSlots = 1;
    private static int _totalPages = 1;
    private static Canvas _tooltipCanvas;
    private static bool _tooltipCanvasWasAdded;
    private static bool _tooltipOriginalOverrideSorting;
    private static int _tooltipOriginalSortingLayerId;
    private static int _tooltipOriginalSortingOrder;
    private static int _tooltipTargetId;
    private static Canvas _dragCanvas;
    private static bool _dragCanvasWasAdded;
    private static bool _dragOriginalOverrideSorting;
    private static int _dragOriginalSortingLayerId;
    private static int _dragOriginalSortingOrder;
    private static int _dragTargetId;
    private static bool _phoneDeferredLogged;

    public static bool IsGeometryInteractionActive => _isDragging || _isResizing;
    public static bool IsOpen => _isOpen;
    public static bool PhonePresentationAvailable => false;

    internal static bool IsActiveCharacterContext(ItemUIManager itemUiManager)
    {
        if (!_isOpen || itemUiManager == null || _activeInteraction?.Mode != ContextMode.StorageOrMachine
            || _activeInteraction.ContextId != GetObjectId(itemUiManager))
            return false;

        try
        {
            var gameplayMenu = Singleton<GameplayMenu>.Instance;
            return gameplayMenu?.IsOpen == true
                   && string.Equals(gameplayMenu.CurrentScreen.ToString(), "Character", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static bool Show(object context, object externalEndpoint)
    {
        if (!CanOpen())
            return false;

        return ShowInternal(context, externalEndpoint, ContextMode.StorageOrMachine);
    }

    public static bool ShowPhone(Phone phone)
    {
        if (!PhonePresentationAvailable)
        {
            if (!_phoneDeferredLogged && Configuration.Instance.ShowBackpackWithPhone)
            {
                _phoneDeferredLogged = true;
                Logger.Diagnostic("[PhoneBackpack] Presentation is deferred in this 1.2.0 playtest build; saved preference and geometry are preserved.");
            }
            return false;
        }

        if (!CanOpenPhone(phone))
            return false;

        return ShowInternal(phone, null, ContextMode.Phone);
    }

    private static bool ShowInternal(object context, object externalEndpoint, ContextMode mode)
    {
        var timer = Stopwatch.StartNew();
        var rootExisted = _root != null;

        EnsureCreated();
        if (_root == null)
            return false;

        var contextId = GetObjectId(context);
        var endpointId = GetObjectId(externalEndpoint);
        var contextChanged = !_isOpen || endpointId != _activeInteraction?.EndpointId || mode != _activeInteraction?.Mode;
        if (contextChanged)
        {
            ClearSlotBindings();
            _currentPage = 0;
            _searchBar?.Reset();
        }

        _activeInteraction = new ActiveInteraction
        {
            Context = context,
            ExternalEndpoint = externalEndpoint,
            ContextId = contextId,
            EndpointId = endpointId,
            Mode = mode
        };
        if (contextChanged)
            ApplySavedGeometry(mode);
        _isOpen = true;
        _root.SetActive(true);
        if (contextChanged)
            PlayerBackpack.Instance.SetContextualAccessor(true);
        ClampToScreen();
        Refresh();
        UpdateNativeOverlayLayers(Singleton<ItemUIManager>.Instance);

        if (contextChanged)
        {
            Logger.Diagnostic(
                $"[BackpackPerf] contextual open total={timer.Elapsed.TotalMilliseconds:F1}ms mode={mode} " +
                $"capacity={PlayerBackpack.Instance.BackpackSlotCount} rendered={_visibleSlots} first={!rootExisted}.");
            var menu = Singleton<GameplayMenu>.Instance;
            var parent = _root.transform.parent;
            Logger.Diagnostic(
                $"[ContextualLifecycle] opened mode={mode} contextId={contextId} endpointId={endpointId} " +
                $"gameplayMenuOpen={menu?.IsOpen == true} gameplayScreen={menu?.CurrentScreen.ToString() ?? "<missing>"} " +
                $"quickMoveEnabled={IsQuickMoveActive(Singleton<ItemUIManager>.Instance)} " +
                $"rootId={_root.GetInstanceID()} rootActive={_root.activeSelf} " +
                $"parent={(parent == null ? "<none>" : parent.name)} parentId={(parent == null ? 0 : parent.GetInstanceID())}.");
        }

        return true;
    }

    public static void Hide(string reason = "explicit close")
    {
        HideInternal(reason);
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

    public static bool ContainsScreenPoint(Vector2 screenPoint)
    {
        return _isOpen && _rect != null && RectTransformUtility.RectangleContainsScreenPoint(_rect, screenPoint);
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

    public static void Hide(object context)
    {
        if (!_isOpen || GetObjectId(context) != _activeInteraction?.ContextId)
            return;

        HideInternal($"ItemUIManager.DisableQuickMove contextId={GetObjectId(context)}");
    }

    public static void HidePhone(object context = null)
    {
        if (!_isOpen || _activeInteraction?.Mode != ContextMode.Phone)
            return;
        if (context != null && GetObjectId(context) != _activeInteraction.ContextId)
            return;

        HideInternal("CharacterInterface.Close for Phone contextual panel");
    }

    public static void ResetSession(string reason)
    {
        var trackedSlots = SlotUis.Count;
        var liveSlots = CountLiveSlotUis();

        try
        {
            RestoreNativeOverlayLayers();
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual overlay cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            UnregisterContextualRaycaster();
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual raycaster cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            if (_isOpen)
                PlayerBackpack.Instance?.SetContextualAccessor(false);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual accessor cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }
        catch
        {
            // Scene teardown may already have destroyed this hierarchy.
        }

        SlotUis.Clear();
        _root = null;
        _rect = null;
        _parentRect = null;
        _panelCanvas = null;
        _panelRaycaster = null;
        _registeredItemUiManager = null;
        _createdForItemUiManagerId = 0;
        _headerRect = null;
        _resizeHandleRect = null;
        _topLeftResizeHandleRect = null;
        _searchBar = null;
        _grid = null;
        _slotContainer = null;
        _pageLabel = null;
        _previousButton = null;
        _nextButton = null;
        _sortButton = null;
        _activeInteraction = null;
        _isOpen = false;
        _isDragging = false;
        _isResizing = false;
        _resizeCorner = ResizeCorner.None;
        _lastCanvasSize = Vector2.zero;
        _currentPage = 0;
        _visibleColumns = 1;
        _visibleRows = 1;
        _visibleSlots = 1;
        _totalPages = 1;

        if (trackedSlots > 0 || liveSlots > 0)
            Logger.Diagnostic($"[Lifecycle] Contextual Backpack UI reset ({reason}); trackedSlots={trackedSlots}, liveSlots={liveSlots}.");
    }

    private static void HideInternal(string reason)
    {
        if (!_isOpen)
            return;

        var timer = Stopwatch.StartNew();
        var interaction = _activeInteraction;
        var mode = interaction?.Mode;
        var menu = Singleton<GameplayMenu>.Instance;
        var itemUiManager = Singleton<ItemUIManager>.Instance;
        var root = _root;
        var parent = root == null ? null : root.transform.parent;
        Logger.Diagnostic(
            $"[ContextualLifecycle] closing reason={reason} mode={mode} " +
            $"contextId={interaction?.ContextId ?? 0} endpointId={interaction?.EndpointId ?? 0} " +
            $"gameplayMenuOpen={menu?.IsOpen == true} gameplayScreen={menu?.CurrentScreen.ToString() ?? "<missing>"} " +
            $"quickMoveEnabled={IsQuickMoveActive(itemUiManager)} rootId={(root == null ? 0 : root.GetInstanceID())} " +
            $"rootActive={root?.activeSelf == true} parent={(parent == null ? "<none>" : parent.name)} " +
            $"parentId={(parent == null ? 0 : parent.GetInstanceID())}.");

        _isOpen = false;
        _isDragging = false;
        _isResizing = false;
        try
        {
            if (_root != null)
                _root.SetActive(false);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual Backpack visibility cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            RestoreNativeOverlayLayers();
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual Backpack native overlay cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            PlayerBackpack.Instance?.SetContextualAccessor(false);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual Backpack accessor cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            ClearSlotBindings();
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] Contextual Backpack slot cleanup skipped safely: {exception.GetType().Name}: {exception.Message}");
        }

        _searchBar?.Reset();
        _currentPage = 0;
        _activeInteraction = null;
        Logger.Diagnostic($"[BackpackPerf] contextual close total={timer.Elapsed.TotalMilliseconds:F1}ms mode={mode} reason={reason} capacity={PlayerBackpack.Instance?.BackpackSlotCount ?? 0}.");
    }

    public static void Refresh()
    {
        if (!_isOpen || _root == null || PlayerBackpack.Instance == null)
            return;

        if (_sortButton != null)
            _sortButton.gameObject.SetActive(Configuration.Instance.ShowSortButtons);
        RecalculateLayout();
        EnsureSlotUis(_visibleSlots);
        ApplyPage();
    }

    public static void InvalidateLayout()
    {
        _visibleColumns = 0;
        _visibleRows = 0;
        _visibleSlots = 0;
        Refresh();
    }

    public static bool TryResolveQuickMoveDestination(ItemSlot sourceSlot, SlotList nativeDestinationSlots, out SlotList destinationSlots)
    {
        destinationSlots = null;
        if (!_isOpen || sourceSlot == null || _activeInteraction?.ExternalEndpoint is not SlotList externalSlots || PlayerBackpack.Instance == null)
            return false;

        var sourceIsBackpack = PlayerBackpack.Instance.OwnsSlot(sourceSlot);
        var sourceIsPlayer = IsPlayerInventorySlot(sourceSlot);
        var sourceIsExternal = ContainsSlot(externalSlots, sourceSlot);
        if (!sourceIsBackpack && !sourceIsPlayer && !sourceIsExternal)
            return false;

        destinationSlots = new SlotList();
        if (sourceIsExternal)
        {
            destinationSlots = ApplyPlayerDestinationPriority(nativeDestinationSlots);
            return destinationSlots.Count > 0;
        }

        AddUniqueSlots(destinationSlots, externalSlots);
        AddUniqueSlots(destinationSlots, nativeDestinationSlots);
        return destinationSlots.Count > 0;
    }

    internal static bool IsExternalQuickMoveSource(ItemSlot sourceSlot)
    {
        return _isOpen
               && sourceSlot != null
               && _activeInteraction?.ExternalEndpoint is SlotList externalSlots
               && ContainsSlot(externalSlots, sourceSlot);
    }

    internal static SlotList ApplyPlayerDestinationPriority(SlotList nativeDestinationSlots)
    {
        var destinations = new SlotList();
        if (Configuration.Instance.ShiftClickDestinationPriority == ShiftClickDestinationPriorityOption.InventoryFirst)
        {
            AddUniqueSlots(destinations, nativeDestinationSlots);
            AddUniqueSlots(destinations, PlayerBackpack.Instance?.GetBackpackSlots());
        }
        else
        {
            AddUniqueSlots(destinations, PlayerBackpack.Instance?.GetBackpackSlots());
            AddUniqueSlots(destinations, nativeDestinationSlots);
        }

        return destinations;
    }

    public static void Update()
    {
        if (!_isOpen || _root == null)
            return;

        var isPhone = _activeInteraction?.Mode == ContextMode.Phone;
        if ((!isPhone && !Configuration.Instance.ContextualBackpackEnabled)
            || (isPhone && !Configuration.Instance.ShowBackpackWithPhone))
        {
            Hide(isPhone ? "Update: Phone contextual preference disabled" : "Update: contextual preference disabled");
            return;
        }

        var itemUiManager = Singleton<ItemUIManager>.Instance;
        if (isPhone)
        {
            if (Phone.Instance?.IsOpen != true)
            {
                Hide("Update: Phone closed");
                return;
            }
        }
        else if (!IsQuickMoveActive(itemUiManager))
        {
            HideInternal("ContextualBackpackPanel.Update: ItemUIManager.QuickMoveEnabled=false");
            return;
        }

        HandleDragResize();
        if (IsGeometryInteractionActive)
            return;

        var canvasSize = GetCanvasSize();
        if (canvasSize != _lastCanvasSize)
        {
            _lastCanvasSize = canvasSize;
            Refresh();
        }

        if (_searchBar?.IsFiltering == true && itemUiManager?.IsCurrentlyDragging != true && _searchBar.RefreshProjection(PlayerBackpack.Instance))
            ApplyPage();

        HandlePagingInput();
    }

    public static void UpdateNativeOverlayLayers(ItemUIManager itemUiManager)
    {
        if (!_isOpen || _panelCanvas == null || itemUiManager == null)
            return;

        EnsureTooltipLayer(itemUiManager);
        EnsureDragLayer(itemUiManager);
    }

    private static bool CanOpen()
    {
        if (!Configuration.Instance.ContextualBackpackEnabled)
        {
            Logger.Diagnostic("[Contextual] Open rejected: Show Contextual Backpack is disabled.");
            return false;
        }
        if (!Configuration.Instance.AutoOpenContextualBackpack)
        {
            Logger.Diagnostic("[Contextual] Open rejected: automatic contextual opening is disabled.");
            return false;
        }
        if (PlayerBackpack.Instance == null)
        {
            Logger.Diagnostic("[Contextual] Open rejected: no current PlayerBackpack instance.");
            return false;
        }
        if (!PlayerBackpack.Instance.IsUnlocked)
        {
            Logger.Diagnostic(
                $"[Contextual] Open rejected: unlock requirement " +
                $"{Configuration.Instance.UnlockLevel.Rank}:{Configuration.Instance.UnlockLevel.Tier} is not met.");
            return false;
        }
        if (PlayerBackpack.Instance.IsOpen)
        {
            Logger.Diagnostic("[Contextual] Open rejected: standalone Backpack is already open.");
            return false;
        }

        return true;
    }

    private static bool CanOpenPhone(Phone phone)
    {
        return Configuration.Instance.ShowBackpackWithPhone
               && phone?.IsOpen == true
               && PlayerBackpack.Instance != null
               && PlayerBackpack.Instance.IsUnlocked
               && !PlayerBackpack.Instance.IsOpen;
    }

    private static bool IsQuickMoveActive(ItemUIManager itemUiManager)
    {
        try
        {
            return itemUiManager?.QuickMoveEnabled == true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureCreated()
    {
        var storageMenu = Singleton<StorageMenu>.Instance;
        var itemUiManager = Singleton<ItemUIManager>.Instance;
        if (storageMenu?.TitleLabel == null || itemUiManager?.ItemSlotUIPrefab == null || itemUiManager.Canvas == null)
            return;

        var itemUiManagerId = itemUiManager.GetInstanceID();
        if (_root != null && _rect != null && _slotContainer != null && _createdForItemUiManagerId == itemUiManagerId)
            return;

        if (_root != null || SlotUis.Count > 0 || _createdForItemUiManagerId != 0)
            ResetSession("ItemUIManager or contextual hierarchy changed");

        var labelTemplate = storageMenu.TitleLabel;
        _root = new GameObject("OGBackpack_ContextualPanel");
        _root.AddComponent<RectTransform>();
        _root.AddComponent<Canvas>();
        _root.AddComponent<GraphicRaycaster>();
        _root.AddComponent<Image>();
        _rect = _root.GetComponent<RectTransform>();
        _panelCanvas = _root.GetComponent<Canvas>();
        _panelRaycaster = _root.GetComponent<GraphicRaycaster>();
        _registeredItemUiManager = itemUiManager;
        _createdForItemUiManagerId = itemUiManagerId;
        Logger.Diagnostic(
            $"[Lifecycle] Creating contextual Backpack UI for ItemUIManager#{itemUiManagerId}, " +
            $"StorageMenu#{storageMenu.GetInstanceID()}.");
        _parentRect = itemUiManager.Canvas.transform.TryCast<RectTransform>();
        _rect.SetParent(_parentRect, false);
        _panelCanvas.overrideSorting = true;
        _panelCanvas.sortingLayerID = itemUiManager.Canvas.sortingLayerID;
        _panelCanvas.sortingOrder = itemUiManager.Canvas.sortingOrder + 1;
        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = Configuration.Instance.ContextualPanelSize;
        _rect.anchoredPosition = Configuration.Instance.ContextualPanelPosition;
        _lastCanvasSize = GetCanvasSize();

        var image = _root.GetComponent<Image>();
        image.color = new Color(0.025f, 0.03f, 0.035f, 1f);

        RegisterContextualRaycasters(itemUiManager);

        CreateHeader(labelTemplate);
        CreateSearchBar(labelTemplate);
        CreateGrid(itemUiManager.ItemSlotUIPrefab.gameObject);
        CreateFooter(labelTemplate);
        CreateResizeHandles();
        _root.SetActive(false);
    }

    private static void RegisterContextualRaycasters(ItemUIManager itemUiManager)
    {
        RegisterRaycaster(itemUiManager, itemUiManager.Canvas.GetComponent<GraphicRaycaster>());
        RegisterRaycaster(itemUiManager, _panelRaycaster);
    }

    private static void RegisterRaycaster(ItemUIManager itemUiManager, GraphicRaycaster raycaster)
    {
        if (raycaster == null)
            return;

        for (var i = 0; i < itemUiManager._raycasters.Count; i++)
        {
#if IL2CPP
            var registered = itemUiManager._raycasters[new Index(i)];
#elif MONO
            var registered = itemUiManager._raycasters[i];
#endif
            if (registered != null && registered.GetInstanceID() == raycaster.GetInstanceID())
                return;
        }

        itemUiManager.AddRaycaster(raycaster);
    }

    private static void UnregisterContextualRaycaster()
    {
        if (_registeredItemUiManager == null || _panelRaycaster == null)
            return;

        var panelRaycasterId = _panelRaycaster.GetInstanceID();
        for (var i = _registeredItemUiManager._raycasters.Count - 1; i >= 0; i--)
        {
#if IL2CPP
            var registered = _registeredItemUiManager._raycasters[new Index(i)];
#elif MONO
            var registered = _registeredItemUiManager._raycasters[i];
#endif
            if (registered == null || registered.GetInstanceID() != panelRaycasterId)
                continue;

            _registeredItemUiManager._raycasters.RemoveAt(i);
        }
    }

    private static void EnsureTooltipLayer(ItemUIManager itemUiManager)
    {
        var infoPanel = itemUiManager.InfoPanel;
        var target = infoPanel?.Container;
        if (target == null)
            return;

        var targetId = target.GetInstanceID();
        if (_tooltipCanvas != null && _tooltipTargetId == targetId)
        {
            ApplyOverlayOrder(_tooltipCanvas, _panelCanvas.sortingOrder + 1);
            return;
        }

        RestoreTooltipLayer();

        var nativeCanvas = infoPanel.Canvas;
        if (nativeCanvas != null && nativeCanvas.GetInstanceID() != itemUiManager.Canvas.GetInstanceID())
        {
            _tooltipCanvas = nativeCanvas;
            _tooltipCanvasWasAdded = false;
        }
        else
        {
            _tooltipCanvas = target.GetComponent<Canvas>();
            _tooltipCanvasWasAdded = _tooltipCanvas == null;
            if (_tooltipCanvasWasAdded)
                _tooltipCanvas = target.gameObject.AddComponent<Canvas>();
        }

        _tooltipTargetId = targetId;
        CaptureCanvasState(
            _tooltipCanvas,
            out _tooltipOriginalOverrideSorting,
            out _tooltipOriginalSortingLayerId,
            out _tooltipOriginalSortingOrder);
        ApplyOverlayOrder(_tooltipCanvas, _panelCanvas.sortingOrder + 1);
    }

    private static void EnsureDragLayer(ItemUIManager itemUiManager)
    {
        var target = itemUiManager.tempIcon;
        if (!itemUiManager.IsCurrentlyDragging || target == null)
        {
            RestoreDragLayer();
            return;
        }

        var targetId = target.GetInstanceID();
        if (_dragCanvas != null && _dragTargetId == targetId)
            return;

        RestoreDragLayer();
        _dragCanvas = target.GetComponent<Canvas>();
        _dragCanvasWasAdded = _dragCanvas == null;
        if (_dragCanvasWasAdded)
            _dragCanvas = target.gameObject.AddComponent<Canvas>();

        _dragTargetId = targetId;
        CaptureCanvasState(
            _dragCanvas,
            out _dragOriginalOverrideSorting,
            out _dragOriginalSortingLayerId,
            out _dragOriginalSortingOrder);
        var tooltipOrder = _tooltipCanvas != null ? _tooltipCanvas.sortingOrder : _panelCanvas.sortingOrder + 1;
        ApplyOverlayOrder(_dragCanvas, Math.Max(_panelCanvas.sortingOrder + 2, tooltipOrder + 1));
    }

    private static void ApplyOverlayOrder(Canvas canvas, int sortingOrder)
    {
        if (canvas == null)
            return;

        canvas.overrideSorting = true;
        canvas.sortingLayerID = _panelCanvas.sortingLayerID;
        canvas.sortingOrder = sortingOrder;
    }

    private static void CaptureCanvasState(Canvas canvas, out bool overrideSorting, out int sortingLayerId, out int sortingOrder)
    {
        overrideSorting = canvas != null && canvas.overrideSorting;
        sortingLayerId = canvas?.sortingLayerID ?? 0;
        sortingOrder = canvas?.sortingOrder ?? 0;
    }

    private static void RestoreNativeOverlayLayers()
    {
        RestoreDragLayer();
        RestoreTooltipLayer();
    }

    private static void RestoreTooltipLayer()
    {
        RestoreCanvas(
            _tooltipCanvas,
            _tooltipCanvasWasAdded,
            _tooltipOriginalOverrideSorting,
            _tooltipOriginalSortingLayerId,
            _tooltipOriginalSortingOrder);
        _tooltipCanvas = null;
        _tooltipCanvasWasAdded = false;
        _tooltipTargetId = 0;
    }

    private static void RestoreDragLayer()
    {
        RestoreCanvas(
            _dragCanvas,
            _dragCanvasWasAdded,
            _dragOriginalOverrideSorting,
            _dragOriginalSortingLayerId,
            _dragOriginalSortingOrder);
        _dragCanvas = null;
        _dragCanvasWasAdded = false;
        _dragTargetId = 0;
        _phoneDeferredLogged = false;
    }

    private static void RestoreCanvas(Canvas canvas, bool wasAdded, bool overrideSorting, int sortingLayerId, int sortingOrder)
    {
        if (canvas == null)
            return;

        if (wasAdded)
        {
            UnityEngine.Object.Destroy(canvas);
            return;
        }

        canvas.overrideSorting = overrideSorting;
        canvas.sortingLayerID = sortingLayerId;
        canvas.sortingOrder = sortingOrder;
    }

    private static void CreateHeader(TextMeshProUGUI labelTemplate)
    {
        var header = new GameObject("Header");
        header.AddComponent<RectTransform>();
        header.AddComponent<Image>();
        _headerRect = header.GetComponent<RectTransform>();
        _headerRect.SetParent(_rect, false);
        _headerRect.anchorMin = new Vector2(0f, 1f);
        _headerRect.anchorMax = new Vector2(1f, 1f);
        _headerRect.pivot = new Vector2(0.5f, 1f);
        _headerRect.sizeDelta = new Vector2(0f, HeaderHeight);
        _headerRect.anchoredPosition = Vector2.zero;
        header.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.1f, 0.98f);

        var title = CreateLabel("Title", _headerRect, Vector2.zero, "Backpack", labelTemplate, 220f, 20f);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.offsetMin = new Vector2(ResizeHandleSize + 4f, 0f);
        titleRect.offsetMax = new Vector2(-(ResizeHandleSize + 4f), 0f);
        title.enableWordWrapping = false;
    }

    private static void CreateGrid(GameObject slotPrefab)
    {
        var container = new GameObject("Slots");
        container.AddComponent<RectTransform>();
        container.AddComponent<GridLayoutGroup>();
        _slotContainer = container.GetComponent<RectTransform>();
        _slotContainer.SetParent(_rect, false);
        _slotContainer.anchorMin = new Vector2(0f, 0f);
        _slotContainer.anchorMax = new Vector2(1f, 1f);
        _slotContainer.pivot = new Vector2(0.5f, 0.5f);
        _slotContainer.offsetMin = new Vector2(HorizontalPadding, FooterHeight + GridVerticalPadding);
        _slotContainer.offsetMax = new Vector2(-HorizontalPadding, -(HeaderHeight + SearchGap + SearchHeight + GridVerticalPadding));

        _grid = container.GetComponent<GridLayoutGroup>();
        var templateSlot = slotPrefab.GetComponent<ItemSlotUI>();
        _grid.cellSize = templateSlot != null ? templateSlot.Rect.sizeDelta : new Vector2(64f, 64f);
        if (_grid.cellSize.x <= 1f || _grid.cellSize.y <= 1f)
            _grid.cellSize = new Vector2(64f, 64f);
        _grid.spacing = new Vector2(GridSpacing, GridSpacing);
        _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _grid.childAlignment = TextAnchor.UpperLeft;
    }

    private static void CreateSearchBar(TextMeshProUGUI labelTemplate)
    {
        _searchBar = BackpackSearchBar.Create("Search", _rect, labelTemplate, SearchChanged, showClearButton: false);
        _searchBar.Rect.anchorMin = new Vector2(0f, 1f);
        _searchBar.Rect.anchorMax = new Vector2(1f, 1f);
        _searchBar.Rect.pivot = new Vector2(0.5f, 1f);
        _searchBar.Rect.sizeDelta = new Vector2(-HorizontalPadding * 2f, SearchHeight);
        _searchBar.Rect.anchoredPosition = new Vector2(0f, -(HeaderHeight + SearchGap));
    }

    private static void CreateFooter(TextMeshProUGUI labelTemplate)
    {
        var footer = new GameObject("Footer");
        footer.AddComponent<RectTransform>();
        footer.AddComponent<HorizontalLayoutGroup>();
        var footerRect = footer.GetComponent<RectTransform>();
        footerRect.SetParent(_rect, false);
        footerRect.anchorMin = new Vector2(0f, 0f);
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 0f);
        footerRect.sizeDelta = new Vector2(0f, FooterHeight);
        footerRect.anchoredPosition = Vector2.zero;

        var layout = footer.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 28, 2, 2);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        _previousButton = CreateButton("Previous", footerRect, Vector2.zero, PreviousPage, labelTemplate, 86f);
        _pageLabel = CreateLabel("Page", footerRect, Vector2.zero, "1 / 1", labelTemplate, 74f, 17f);
        var pageLayout = _pageLabel.gameObject.AddComponent<LayoutElement>();
        pageLayout.minWidth = 74f;
        pageLayout.preferredWidth = 74f;
        pageLayout.minHeight = 30f;
        _nextButton = CreateButton("Next", footerRect, Vector2.zero, NextPage, labelTemplate, 66f);
        _sortButton = CreateButton("Sort", footerRect, Vector2.zero, () =>
        {
            PlayerBackpack.Instance?.SortContents();
        }, labelTemplate, 66f);
        _sortButton.gameObject.SetActive(Configuration.Instance.ShowSortButtons);
    }

    private static void CreateResizeHandles()
    {
        _resizeHandleRect = CreateResizeHandle(
            "ResizeHandle_BottomRight",
            new Vector2(1f, 0f),
            new Vector2(1f, 0f));
        _topLeftResizeHandleRect = CreateResizeHandle(
            "ResizeHandle_TopLeft",
            new Vector2(0f, 1f),
            new Vector2(0f, 1f));
    }

    private static RectTransform CreateResizeHandle(string name, Vector2 anchor, Vector2 pivot)
    {
        var handle = new GameObject(name);
        handle.AddComponent<RectTransform>();
        handle.AddComponent<Image>();
        var rect = handle.GetComponent<RectTransform>();
        rect.SetParent(_rect, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = new Vector2(ResizeHandleSize, ResizeHandleSize);
        rect.anchoredPosition = Vector2.zero;
        handle.GetComponent<Image>().color = new Color(0.45f, 0.48f, 0.52f, 0.85f);
        return rect;
    }

    private static void EnsureSlotUis(int count)
    {
        var itemUiManager = Singleton<ItemUIManager>.Instance;
        if (itemUiManager?.ItemSlotUIPrefab == null || _slotContainer == null)
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
                $"[Lifecycle] Rebuilding stale contextual slot UI list; tracked={SlotUis.Count}, " +
                $"live={liveSlots}, children={_slotContainer.childCount}.");
            for (var i = 0; i < SlotUis.Count; i++)
            {
                if (SlotUis[i] != null)
                    UnityEngine.Object.Destroy(SlotUis[i].gameObject);
            }

            SlotUis.Clear();
        }

        var prefab = itemUiManager.ItemSlotUIPrefab.gameObject;
        while (SlotUis.Count < count)
        {
            var slot = UnityEngine.Object.Instantiate(prefab, _slotContainer);
            slot.name = "ContextualBackpackSlot_" + SlotUis.Count;
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

    private static bool RecalculateLayout()
    {
        ClampToScreen();
        return RecalculateGridDimensions();
    }

    private static bool RecalculateGridDimensions()
    {
        var size = _rect.sizeDelta;
        var availableWidth = Mathf.Max(1f, size.x - HorizontalPadding * 2f);
        var availableHeight = Mathf.Max(
            1f,
            size.y - HeaderHeight - SearchGap - SearchHeight - FooterHeight - GridVerticalPadding * 2f);
        var pitchX = _grid.cellSize.x + _grid.spacing.x;
        var pitchY = _grid.cellSize.y + _grid.spacing.y;
        var preferredColumns = Configuration.Instance.PreferredColumns;

        var visibleColumns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + _grid.spacing.x) / pitchX));
        if (preferredColumns > 0)
            visibleColumns = Mathf.Min(visibleColumns, preferredColumns);
        var visibleRows = Mathf.Max(1, Mathf.FloorToInt((availableHeight + _grid.spacing.y) / pitchY));
        var backpackCapacity = Mathf.Max(1, PlayerBackpack.Instance?.BackpackSlotCount ?? 1);
        var visibleSlots = Mathf.Min(backpackCapacity, Mathf.Max(1, visibleColumns * visibleRows));
        var gridChanged = visibleColumns != _visibleColumns
                          || visibleRows != _visibleRows
                          || visibleSlots != _visibleSlots;
        _visibleColumns = visibleColumns;
        _visibleRows = visibleRows;
        _visibleSlots = visibleSlots;
        if (gridChanged)
            _grid.constraintCount = _visibleColumns;
        return gridChanged;
    }

    private static void RefreshResizeLayout()
    {
        if (!RecalculateGridDimensions())
            return;

        EnsureSlotUis(_visibleSlots);
        ApplyPage();
    }

    private static void ApplyPage()
    {
        _searchBar.RefreshProjection(PlayerBackpack.Instance);
        UpdateProjectionAndPagination();
        var start = _currentPage * _visibleSlots;
        for (var i = 0; i < SlotUis.Count; i++)
        {
            var slotUI = SlotUis[i];
            if (slotUI == null)
                continue;

            // Cloned ItemSlotUIs may inherit their template's current binding.
            slotUI.ClearSlot();
            var projectedIndex = start + i;
            var slotIndex = _searchBar.GetInventorySlotIndex(projectedIndex);
            var shouldShow = i < _visibleSlots && slotIndex >= 0;
            slotUI.gameObject.SetActive(shouldShow);
            if (shouldShow)
            {
                slotUI.AssignSlot(PlayerBackpack.Instance.GetBackpackSlot(slotIndex));
                slotUI.SetVisible(true);
            }
            else
            {
                slotUI.ClearSlot();
                slotUI.SetVisible(false);
            }
        }

        if (_pageLabel != null)
            _pageLabel.text = _searchBar.IsFiltering && _searchBar.ProjectedCount == 0
                ? "No results"
                : $"{_currentPage + 1} / {_totalPages}";
        if (_previousButton != null)
            _previousButton.interactable = _currentPage > 0;
        if (_nextButton != null)
            _nextButton.interactable = _currentPage < _totalPages - 1;
    }

    private static void HandlePagingInput()
    {
        var scroll = Input.mouseScrollDelta.y;
        if (scroll > 0.01f)
            PreviousPage();
        else if (scroll < -0.01f)
            NextPage();
    }

    private static void HandleDragResize()
    {
        var mouse = (Vector2) Input.mousePosition;
        if (Input.GetMouseButtonDown(0) && !AnyPanelSlotBeingDragged())
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_topLeftResizeHandleRect, mouse))
            {
                BeginResize(ResizeCorner.TopLeft, mouse);
            }
            else if (RectTransformUtility.RectangleContainsScreenPoint(_resizeHandleRect, mouse))
            {
                BeginResize(ResizeCorner.BottomRight, mouse);
            }
            else if (RectTransformUtility.RectangleContainsScreenPoint(_headerRect, mouse))
            {
                _isDragging = true;
                _dragGrabOffset = GetLocalPoint(mouse) - _rect.anchoredPosition;
            }
        }

        if (Input.GetMouseButton(0))
        {
            if (_isDragging)
            {
                _rect.anchoredPosition = GetLocalPoint(mouse) - _dragGrabOffset;
                ClampPositionToScreen();
            }
            else if (_isResizing)
            {
                ResizeFromCorner(mouse);
                RefreshResizeLayout();
            }
        }

        if (Input.GetMouseButtonUp(0) && (_isDragging || _isResizing))
        {
            var completedResize = _isResizing;
            _isDragging = false;
            _isResizing = false;
            _resizeCorner = ResizeCorner.None;
            if (completedResize)
                Refresh();
            if (_activeInteraction?.Mode == ContextMode.Phone)
                Configuration.Instance.SetPhonePanelGeometry(_rect.anchoredPosition, _rect.sizeDelta);
            else
                Configuration.Instance.SetContextualPanelGeometry(_rect.anchoredPosition, _rect.sizeDelta);
        }
    }

    private static void ApplySavedGeometry(ContextMode mode)
    {
        if (_rect == null)
            return;

        if (mode == ContextMode.Phone)
        {
            _rect.sizeDelta = Configuration.Instance.PhonePanelSize;
            _rect.anchoredPosition = Configuration.Instance.PhonePanelPosition;
        }
        else
        {
            _rect.sizeDelta = Configuration.Instance.ContextualPanelSize;
            _rect.anchoredPosition = Configuration.Instance.ContextualPanelPosition;
        }
    }

    private static void BeginResize(ResizeCorner corner, Vector2 screenMouse)
    {
        var halfSize = _rect.sizeDelta * 0.5f;
        var position = _rect.anchoredPosition;
        Vector2 activeCorner;
        if (corner == ResizeCorner.TopLeft)
        {
            activeCorner = position + new Vector2(-halfSize.x, halfSize.y);
            _resizeOppositeCorner = position + new Vector2(halfSize.x, -halfSize.y);
        }
        else
        {
            activeCorner = position + new Vector2(halfSize.x, -halfSize.y);
            _resizeOppositeCorner = position + new Vector2(-halfSize.x, halfSize.y);
        }

        _isResizing = true;
        _resizeCorner = corner;
        _resizeGrabOffset = GetLocalPoint(screenMouse) - activeCorner;
    }

    private static void ResizeFromCorner(Vector2 screenMouse)
    {
        var activeCorner = GetLocalPoint(screenMouse) - _resizeGrabOffset;
        var maximumSize = GetMaximumPanelSize(GetCanvasSize());
        float width;
        float height;

        if (_resizeCorner == ResizeCorner.TopLeft)
        {
            width = ClampDimension(_resizeOppositeCorner.x - activeCorner.x, MinWidth, maximumSize.x);
            height = ClampDimension(activeCorner.y - _resizeOppositeCorner.y, MinHeight, maximumSize.y);
            _rect.sizeDelta = new Vector2(width, height);
            _rect.anchoredPosition = _resizeOppositeCorner + new Vector2(-width * 0.5f, height * 0.5f);
        }
        else
        {
            width = ClampDimension(activeCorner.x - _resizeOppositeCorner.x, MinWidth, maximumSize.x);
            height = ClampDimension(_resizeOppositeCorner.y - activeCorner.y, MinHeight, maximumSize.y);
            _rect.sizeDelta = new Vector2(width, height);
            _rect.anchoredPosition = _resizeOppositeCorner + new Vector2(width * 0.5f, -height * 0.5f);
        }

        ClampPositionToScreen();
    }

    private static Vector2 GetLocalPoint(Vector2 screenPosition)
    {
        if (_parentRect == null)
            return screenPosition;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, screenPosition, null, out var localPoint);
        return localPoint;
    }

    private static void ClampToScreen()
    {
        var canvasSize = GetCanvasSize();
        var size = _rect.sizeDelta;
        var maximumSize = GetMaximumPanelSize(canvasSize);
        size.x = ClampDimension(size.x, MinWidth, maximumSize.x);
        size.y = ClampDimension(size.y, MinHeight, maximumSize.y);
        _rect.sizeDelta = size;
        ClampPositionToScreen(canvasSize, size);
    }

    private static void ClampPositionToScreen()
    {
        ClampPositionToScreen(GetCanvasSize(), _rect.sizeDelta);
    }

    private static void ClampPositionToScreen(Vector2 canvasSize, Vector2 size)
    {
        var xLimit = Mathf.Max(0f, (canvasSize.x - size.x) * 0.5f - CanvasSafetyMargin);
        var yLimit = Mathf.Max(0f, (canvasSize.y - size.y) * 0.5f - CanvasSafetyMargin);
        _rect.anchoredPosition = new Vector2(
            Mathf.Clamp(_rect.anchoredPosition.x, -xLimit, xLimit),
            Mathf.Clamp(_rect.anchoredPosition.y, -yLimit, yLimit));
    }

    private static Vector2 GetMaximumPanelSize(Vector2 canvasSize)
    {
        return new Vector2(
            Mathf.Max(1f, canvasSize.x - CanvasSafetyMargin * 2f),
            Mathf.Max(1f, canvasSize.y - CanvasSafetyMargin * 2f));
    }

    private static float ClampDimension(float value, float minimum, float maximum)
    {
        var effectiveMinimum = Mathf.Min(minimum, maximum);
        return Mathf.Clamp(value, effectiveMinimum, maximum);
    }

    private static Vector2 GetCanvasSize()
    {
        if (_parentRect != null && _parentRect.rect.width > 1f && _parentRect.rect.height > 1f)
            return _parentRect.rect.size;

        return new Vector2(Screen.width, Screen.height);
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

    private static bool ContainsSlot(SlotList slots, ItemSlot candidate)
    {
        for (var i = 0; i < slots.Count; i++)
        {
#if IL2CPP
            var slot = slots[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var slot = slots[i];
#endif
            if (PlayerBackpack.SlotsMatch(slot, candidate))
                return true;
        }

        return false;
    }

    private static bool IsPlayerInventorySlot(ItemSlot candidate)
    {
        var playerInventory = PlayerInventory.Instance;
        var playerSlots = playerInventory?.hotbarSlots;
        if (playerInventory == null || playerSlots == null)
            return false;

        if (PlayerBackpack.SlotsMatch(playerInventory.cashSlot, candidate))
            return true;

        for (var i = 0; i < playerSlots.Count; i++)
        {
#if IL2CPP
            var playerSlot = playerSlots[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var playerSlot = playerSlots[i];
#endif
            if (PlayerBackpack.SlotsMatch(playerSlot, candidate))
                return true;
        }

        return false;
    }

    private static void AddUniqueSlots(SlotList destination, SlotList source)
    {
        if (source == null)
            return;

        for (var i = 0; i < source.Count; i++)
        {
#if IL2CPP
            var candidate = source[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var candidate = source[i];
#endif
            if (candidate == null || ContainsSlot(destination, candidate))
                continue;

            destination.Add(candidate);
        }
    }

    private static int GetObjectId(object value)
    {
        if (value is UnityEngine.Object unityObject)
            return unityObject.GetInstanceID();

        return value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
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

    private static bool AnyPanelSlotBeingDragged()
    {
        for (var i = 0; i < SlotUis.Count; i++)
        {
            if (SlotUis[i] != null && SlotUis[i].IsBeingDragged)
                return true;
        }

        return false;
    }

    private static Button CreateButton(string text, RectTransform parent, Vector2 anchoredPosition, Action onClick, TextMeshProUGUI template, float width)
    {
        var obj = new GameObject("Button_" + text);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<Image>();
        obj.AddComponent<Button>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, 32f);
        rect.anchoredPosition = anchoredPosition;
        var layout = obj.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = 30f;
        layout.preferredHeight = 30f;

        obj.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.1f, 0.95f);
        var button = obj.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        CreateLabel("Label", rect, Vector2.zero, text, template, width - 8f, 17f);
        return button;
    }

    private static TextMeshProUGUI CreateLabel(string name, RectTransform parent, Vector2 anchoredPosition, string text, TextMeshProUGUI template, float width, float fontSize)
    {
        var obj = new GameObject(name);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<TextMeshProUGUI>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, 30f);
        rect.anchoredPosition = anchoredPosition;

        var label = obj.GetComponent<TextMeshProUGUI>();
        label.font = template.font;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.text = text;
        return label;
    }
}
