using System.Collections;
using Backpack.Config;
using Backpack.Patches;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.ContactsApp;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppScheduleOne.UI.Phone.Map;
using Il2CppScheduleOne.UI.Phone.Messages;
using Il2CppScheduleOne.UI.Phone.ProductManagerApp;
using Il2CppTMPro;
using GameCanvasScaler = Il2CppScheduleOne.UI.CanvasScaler;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using ScheduleOne.UI.Phone;
using ScheduleOne.UI.Phone.ContactsApp;
using ScheduleOne.UI.Phone.Delivery;
using ScheduleOne.UI.Phone.Map;
using ScheduleOne.UI.Phone.Messages;
using ScheduleOne.UI.Phone.ProductManagerApp;
using TMPro;
using GameCanvasScaler = ScheduleOne.UI.CanvasScaler;
#endif

namespace Backpack.UI;

/// <summary>
/// Dedicated, non-contextual Backpack presentation for the normal phone screen.
/// Panels are fitted into app-specific safe regions around the projected Phone
/// content, so allowed Phone apps remain unobstructed and interactive.
/// </summary>
public static class PhoneBackpackUI
{
    private const int MaxColumnsPerSide = 6;
    private const int MaxLandscapeRightColumns = 4;
    private const int MaxLandscapeBottomColumns = 12;
    private const int MaxLandscapeBottomRows = 3;
    private const int MaxRows = 8;
    private const int MaxPortraitRightRows = 9;
    private const float CanvasMargin = 14f;
    private const float PhoneGap = 28f;
    private const float NativeInventoryGap = 14f;
    private const float PanelPadding = 8f;
    private const float HeaderHeight = 32f;
    private const float ToolbarHeight = 36f;
    private const float SectionGap = 5f;
    private const float GridSpacing = 4f;
    private const float MinimumPanelWidth = 250f;
    private const float BoundsEpsilon = 1.5f;
    private const float LayoutRetryIntervalSeconds = 0.05f;
    private const float DefaultPhoneFacingSetbackFraction = 0.28f;
    // The user-validated maximum presentation scale. Current scale is always
    // resolved from Schedule I's live CanvasScaler contribution, not preferences.
    private const float ReferenceGlobalUiScale = 0.8f;

    private static readonly List<ItemSlotUI> LeftSlotUis = new();
    private static readonly List<ItemSlotUI> RightSlotUis = new();

    private static Phone _phone;
    private static GameObject _root;
    private static RectTransform _rootRect;
    private static RectTransform _parentRect;
    private static Canvas _panelCanvas;
    private static GraphicRaycaster _panelRaycaster;
    private static ItemUIManager _registeredItemUiManager;
    private static int _createdForItemUiManagerId;

    private static RectTransform _leftPanel;
    private static RectTransform _rightPanel;
    private static RectTransform _leftGridRect;
    private static RectTransform _rightGridRect;
    private static GridLayoutGroup _leftGrid;
    private static GridLayoutGroup _rightGrid;
    private static RectTransform _controlsRect;
    private static TextMeshProUGUI _pageLabel;
    private static Button _previousButton;
    private static Button _nextButton;
    private static Button _sortButton;

    private static bool _requested;
    private static int _showRequestGeneration;
    private static bool _isOpen;
    private static bool _layoutValid;
    private static int _currentPage;
    private static int _leftColumns;
    private static int _rightColumns;
    private static int _leftRows;
    private static int _rightRows;
    private static int _visibleSlots = 1;
    private static int _totalPages = 1;
    private static Rect _lastPhoneScreenBounds;
    private static Rect _lastCanvasRect;
    private static float _lastLandscapeSafeBottom = float.NaN;
    private static float _lastPresentationScale = 1f;
    private static PhoneLayoutMode _lastLayoutMode;
    private static string _lastRejectReason;
    private static string _lastDecisionKey;
    private static float _nextLayoutLogTime;
    private static float _nextLayoutRetryAt;
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
    private static bool _suspendedForApp;
    private static bool _panelInteractionEnabled;
    private static bool _phoneAccessorActive;
    private static bool _characterSuppressed;
    private static PhoneAppState _activeAppState = PhoneAppState.Unknown;
    private static Type _modSettingsType;
    private static System.Reflection.PropertyInfo _modSettingsInstanceProperty;
    private static System.Reflection.FieldInfo _modSettingsPanelField;
    private static bool _modSettingsReflectionResolved;

    private enum PhoneLayoutMode
    {
        Portrait,
        Landscape
    }

    private enum PanelHorizontalAlignment
    {
        Center,
        NearLeft,
        NearRight
    }

    private sealed class PhoneAppState
    {
        public static readonly PhoneAppState Unknown = new("Unknown", false, PhoneLayoutMode.Portrait, "none", null);

        public PhoneAppState(string name, bool allowed, PhoneLayoutMode layout, string source, RectTransform boundsRect)
        {
            Name = name;
            Allowed = allowed;
            Layout = layout;
            Source = source;
            BoundsRect = boundsRect;
        }

        public string Name { get; }
        public bool Allowed { get; }
        public PhoneLayoutMode Layout { get; }
        public string Source { get; }
        public RectTransform BoundsRect { get; }
        public string Key => $"{Name}|{Allowed}|{Layout}|{Source}";
    }

    private readonly struct PanelMetrics
    {
        public PanelMetrics(Rect bounds, int columns, int rows, float gridWidth, float gridHeight)
        {
            Bounds = bounds;
            Columns = columns;
            Rows = rows;
            GridWidth = gridWidth;
            GridHeight = gridHeight;
        }

        public Rect Bounds { get; }
        public int Columns { get; }
        public int Rows { get; }
        public float GridWidth { get; }
        public float GridHeight { get; }
        public int Capacity => Columns * Rows;
        public bool IsUsable => Capacity > 0;
    }

    public static bool IsOpen => _isOpen;

    public static bool Show(Phone phone, bool? allowInteractions = null)
    {
        _phone = phone;
        _requested = true;

        var appState = ResolveAppState();
        UpdateAppState(appState);

        if (!CanOpen(phone, appState, out var rejectReason))
        {
            Reject(rejectReason);
            return false;
        }

        EnsureCreated();
        if (_root == null)
        {
            _nextLayoutRetryAt = Time.unscaledTime + LayoutRetryIntervalSeconds;
            Reject("native ItemUIManager/StorageMenu UI templates are not ready");
            return false;
        }

        if (!TryRecalculateLayout("phone opened", true))
            return false;

        var wasOpen = _isOpen;
        var resuming = _suspendedForApp;
        var gameplayPolicyReady = allowInteractions ?? GameplaySettingsSyncManager.CanUseBaiqInventoryFeatures;
        _isOpen = true;
        _suspendedForApp = false;
        _root.SetActive(true);
        _rootRect.SetAsFirstSibling();
        SetPanelInteractionEnabled(gameplayPolicyReady);
        if (!wasOpen)
        {
            if (gameplayPolicyReady)
                InventoryInteractionPatch.BeginPhoneQuickMove(_registeredItemUiManager);
            if (!resuming)
                _currentPage = 0;
        }

        Refresh();
        _lastRejectReason = null;
        LogPresentationDecision(appState, "shown", null);
        if (!wasOpen)
        {
            Logger.Diagnostic(
                $"[PhoneBackpack] shown app={appState.Name} layout={appState.Layout.ToString().ToLowerInvariant()} " +
                $"rendered={_visibleSlots} pages={_totalPages}.");
        }

        LogLifecycleState("presenter.shown");

        return true;
    }

    internal static void RequestShowAfterLayout(Phone phone)
    {
        _phone = phone;
        _requested = true;
        _nextLayoutRetryAt = Time.unscaledTime + LayoutRetryIntervalSeconds;
        var requestGeneration = unchecked(++_showRequestGeneration);
        MelonCoroutines.Start(ShowAfterCanvasLayout(phone, requestGeneration));
        Logger.Diagnostic("[PhoneBackpack] initial phone presentation deferred until end-of-frame layout.");
    }

    private static IEnumerator ShowAfterCanvasLayout(Phone phone, int requestGeneration)
    {
        yield return new WaitForEndOfFrame();
        if (requestGeneration != _showRequestGeneration || phone == null || !phone.IsOpen)
            yield break;

        try
        {
            // Phone.SetIsOpen can run before the Phone/Home RectTransforms have
            // completed their first layout pass. Force one canvas pass at the
            // deferred boundary, then measure once; the existing bounded
            // Update retry remains responsible for any still-transient geometry.
            Canvas.ForceUpdateCanvases();
            _nextLayoutRetryAt = Time.unscaledTime;
            if (!Show(phone))
                Logger.Diagnostic("[PhoneBackpack] deferred first-open measurement is still pending; bounded layout retries remain active.");
        }
        catch (Exception exception)
        {
            _nextLayoutRetryAt = Time.unscaledTime + LayoutRetryIntervalSeconds;
            Logger.Warning($"[PhoneBackpack] deferred first-open layout evaluation failed safely; bounded retries remain active ({exception.GetType().Name}).");
        }
    }

    public static void Hide(string reason = "phone closed")
    {
        unchecked { _showRequestGeneration++; }
        _requested = false;
        _suspendedForApp = false;
        SetPanelInteractionEnabled(false);
        if (!_isOpen)
        {
            if (_root != null)
                _root.SetActive(false);
            LogLifecycleState($"presenter.hidden reason={reason}");
            return;
        }

        _isOpen = false;
        try
        {
            if (_root != null)
                _root.SetActive(false);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[PhoneBackpack] visibility cleanup skipped safely: {exception.Message}");
        }

        try
        {
            RestoreNativeOverlayLayers();
            InventoryInteractionPatch.EndPhoneQuickMove(_registeredItemUiManager);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[PhoneBackpack] quick-move cleanup skipped safely: {exception.Message}");
        }

        try
        {
            SetPhoneContextualAccessor(false);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[PhoneBackpack] accessor cleanup skipped safely: {exception.Message}");
        }

        ClearSlotBindings();
        _currentPage = 0;
        Logger.Diagnostic($"[PhoneBackpack] hidden reason={reason} app={_activeAppState.Name}.");
        LogLifecycleState($"presenter.hidden reason={reason}");
    }

    public static void Update(bool gameplayPolicyReady)
    {
        if (!_requested && !_isOpen)
            return;

        SetPanelInteractionEnabled(gameplayPolicyReady);

        if (!Configuration.Instance.ShowBackpackWithPhone)
        {
            Hide("preference disabled");
            return;
        }

        if (_phone?.IsOpen != true)
        {
            Hide("phone no longer open");
            return;
        }

        var appState = ResolveAppState();
        var appChanged = UpdateAppState(appState);
        if (!appState.Allowed)
        {
            SuspendForApp($"app-not-allowed app={appState.Name}");
            return;
        }

        if (appChanged)
        {
            _layoutValid = false;
            _nextLayoutRetryAt = 0f;
            if (_root != null)
                _root.SetActive(false);
        }

        if (!_isOpen)
        {
            if (Time.unscaledTime >= _nextLayoutRetryAt)
                Show(_phone, gameplayPolicyReady);
            return;
        }

        var manager = Singleton<ItemUIManager>.Instance;
        if (manager == null || manager.GetInstanceID() != _createdForItemUiManagerId)
        {
            var phone = _phone;
            ResetSession("ItemUIManager hierarchy changed");
            Show(phone, gameplayPolicyReady);
            return;
        }

        var layoutWasValid = _layoutValid;
        var hadLayoutRejection = _lastRejectReason != null;
        if (!layoutWasValid && Time.unscaledTime < _nextLayoutRetryAt)
            return;

        // While policy is pending, app/phone visibility checks still run above,
        // but a valid read-only panel does not need a full geometry walk every
        // frame. App changes and invalid layouts continue through the retry path.
        if (!gameplayPolicyReady && layoutWasValid)
            return;

        if (TryRecalculateLayout(appChanged ? "allowed app changed" : "phone/canvas geometry changed", appChanged) &&
            (appChanged || !layoutWasValid || hadLayoutRejection))
            LogPresentationDecision(appState, "shown", null);

        if (!_layoutValid)
            return;

        SetPanelInteractionEnabled(gameplayPolicyReady);
        if (gameplayPolicyReady && !InventoryInteractionPatch.PhoneQuickMoveActive)
            InventoryInteractionPatch.BeginPhoneQuickMove(_registeredItemUiManager);
        if (!gameplayPolicyReady)
            return;

        if (!ContainsScreenPoint(Input.mousePosition))
            return;

        var scroll = Input.mouseScrollDelta.y;
        if (scroll > 0.01f)
            PreviousPage();
        else if (scroll < -0.01f)
            NextPage();
    }

    public static void Refresh()
    {
        if (!_isOpen || !_layoutValid || PlayerBackpack.Instance == null)
            return;

        if (_sortButton != null)
            _sortButton.gameObject.SetActive(Configuration.Instance.ShowSortButtons);
        EnsureSlotUis(_leftColumns, _leftRows, _rightColumns, _rightRows);
        ApplyPage();
    }

    public static void InvalidateLayout(string reason = "configuration changed")
    {
        _layoutValid = false;
        if (!Configuration.Instance.ShowBackpackWithPhone)
        {
            Hide("preference disabled");
            return;
        }

        if (Phone.Instance?.IsOpen == true)
            Show(Phone.Instance);
        else if (_isOpen)
            Hide(reason);
    }

    internal static void SetPolicyReadiness(bool ready, string reason)
    {
        if (ready)
            return;

        SetPanelInteractionEnabled(false);
        if (_requested || _isOpen)
            Logger.Diagnostic($"[PhoneBackpack] policy became unavailable; current presentation is read-only ({reason}).");
    }

    public static void ResetSession(string reason)
    {
        unchecked { _showRequestGeneration++; }
        var trackedSlots = LeftSlotUis.Count + RightSlotUis.Count;
        var wasOpen = _isOpen;
        SetPanelInteractionEnabled(false);

        if (wasOpen)
        {
            try
            {
                RestoreNativeOverlayLayers();
                InventoryInteractionPatch.EndPhoneQuickMove(_registeredItemUiManager);
                SetPhoneContextualAccessor(false);
            }
            catch (Exception exception)
            {
                Logger.Warning($"[PhoneBackpack] session cleanup skipped safely: {exception.Message}");
            }
        }

        try
        {
            UnregisterRaycaster();
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
        }
        catch
        {
            // The owning native canvas may already have been destroyed.
        }

        LeftSlotUis.Clear();
        RightSlotUis.Clear();
        _phone = null;
        _root = null;
        _rootRect = null;
        _parentRect = null;
        _panelCanvas = null;
        _panelRaycaster = null;
        _registeredItemUiManager = null;
        _createdForItemUiManagerId = 0;
        _leftPanel = null;
        _rightPanel = null;
        _leftGridRect = null;
        _rightGridRect = null;
        _leftGrid = null;
        _rightGrid = null;
        _controlsRect = null;
        _pageLabel = null;
        _previousButton = null;
        _nextButton = null;
        _sortButton = null;
        _requested = false;
        _isOpen = false;
        _layoutValid = false;
        _currentPage = 0;
        _leftColumns = 0;
        _rightColumns = 0;
        _leftRows = 0;
        _rightRows = 0;
        _visibleSlots = 1;
        _totalPages = 1;
        _lastRejectReason = null;
        _lastDecisionKey = null;
        _nextLayoutRetryAt = 0f;
        _suspendedForApp = false;
        _panelInteractionEnabled = false;
        _phoneAccessorActive = false;
        _characterSuppressed = false;
        _activeAppState = PhoneAppState.Unknown;
        _lastLandscapeSafeBottom = float.NaN;
        _lastPresentationScale = 1f;

        if (trackedSlots > 0 || wasOpen)
            Logger.Diagnostic($"[Lifecycle] Phone Backpack UI reset ({reason}); trackedSlots={trackedSlots}.");
    }

    public static bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!_isOpen)
            return false;

        return (_leftPanel != null && RectTransformUtility.RectangleContainsScreenPoint(_leftPanel, screenPoint))
               || (_rightPanel != null && RectTransformUtility.RectangleContainsScreenPoint(_rightPanel, screenPoint));
    }

    public static bool ContainsSlotRect(RectTransform candidate)
    {
        if (candidate == null)
            return false;

        return ContainsSlotRect(LeftSlotUis, candidate) || ContainsSlotRect(RightSlotUis, candidate);
    }

    public static void UpdateNativeOverlayLayers(ItemUIManager manager)
    {
        if (!_isOpen || _panelCanvas == null || manager == null)
            return;

        EnsureTooltipLayer(manager);
        EnsureDragLayer(manager);
    }

    internal static void LogLifecycleState(string boundary)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        try
        {
            var phone = Phone.Instance;
            var menu = Singleton<GameplayMenu>.Instance;
            var characterInterface = Singleton<GameplayMenuInterface>.Instance?.CharacterInterface;
            var gameplayScreen = menu == null ? "<missing>" : menu.CurrentScreen.ToString();
            var root = _root;
            var parent = root == null ? null : root.transform.parent;
            var appState = ResolveAppState();
            var desiredVisible = _requested && phone?.IsOpen == true && appState.Allowed &&
                                 Configuration.Instance.ShowBackpackWithPhone &&
                                 PlayerBackpack.Instance?.IsUnlocked == true &&
                                 PlayerBackpack.Instance.IsOpen != true;

            Logger.Diagnostic(
                $"[PhoneBackpackLifecycle] boundary={boundary} phoneOpen={phone?.IsOpen == true} " +
                $"gameplayMenuOpen={menu?.IsOpen == true} gameplayScreen={gameplayScreen} " +
                $"characterOpen={characterInterface?.IsOpen == true} owner={appState.Name} " +
                $"characterSuppressed={_characterSuppressed} requested={_requested} " +
                $"desiredVisible={desiredVisible} presenterOpen={_isOpen} " +
                $"rootId={(root == null ? 0 : root.GetInstanceID())} " +
                $"rootActiveSelf={root?.activeSelf == true} rootActiveInHierarchy={root?.activeInHierarchy == true} " +
                $"parent={(parent == null ? "<none>" : parent.name)} " +
                $"parentId={(parent == null ? 0 : parent.GetInstanceID())}.");
        }
        catch (Exception exception)
        {
            Logger.Diagnostic($"[PhoneBackpackLifecycle] boundary={boundary} stateSampleFailed={exception.GetType().Name}.");
        }
    }

    internal static void OnGameplayScreenChanged(GameplayMenu menu)
    {
        if (menu == null)
            return;

        var selectedScreen = menu.CurrentScreen.ToString();
        if (string.Equals(selectedScreen, "Character", StringComparison.Ordinal))
        {
            _characterSuppressed = true;
            var character = new PhoneAppState("Character", false, PhoneLayoutMode.Portrait, "GameplayMenu.CurrentScreen", null);
            UpdateAppState(character);
            SuspendForApp("GameplayMenu.CurrentScreen=Character");
            LogLifecycleState("GameplayMenu.SetScreen.postfix");
            return;
        }

        if (string.Equals(selectedScreen, "Phone", StringComparison.Ordinal))
        {
            // GameplayMenu.CurrentScreen is the authoritative owner. A stale
            // CharacterInterface.IsOpen observation or suppression latch must
            // not keep the Phone presenter rejected after this transition.
            _characterSuppressed = false;
            var phoneState = ResolveAppState();
            UpdateAppState(phoneState);

            if (phoneState.Allowed && Configuration.Instance.ShowBackpackWithPhone && Phone.Instance?.IsOpen == true)
            {
                // GameplayMenu switches screen ownership independently of Phone.SetIsOpen.
                // Re-request the existing Phone presenter when Phone remained logically open.
                RequestShowAfterLayout(Phone.Instance);
            }

            LogLifecycleState("GameplayMenu.SetScreen.postfix");
            return;
        }

        var appState = ResolveAppState();
        UpdateAppState(appState);
        LogLifecycleState("GameplayMenu.SetScreen.postfix");
    }

    public static void SetCharacterSuppressed(bool suppressed)
    {
        _characterSuppressed = suppressed;
        if (suppressed)
        {
            var character = new PhoneAppState("Character", false, PhoneLayoutMode.Portrait, "CharacterInterface.Open", null);
            UpdateAppState(character);
            SuspendForApp("app-not-allowed app=Character");
            return;
        }

        if (_requested && _phone?.IsOpen == true)
            UpdateAppState(ResolveAppState());
    }

    private static bool UpdateAppState(PhoneAppState next)
    {
        var changed = !string.Equals(_activeAppState.Key, next.Key, StringComparison.Ordinal);
        if (!changed)
            return false;

        Logger.Diagnostic(
            $"[PhoneBackpack] app changed old={_activeAppState.Name} new={next.Name} " +
            $"allowed={next.Allowed} layout={next.Layout.ToString().ToLowerInvariant()} source={next.Source}.");
        _activeAppState = next;
        return true;
    }

    private static PhoneAppState ResolveAppState()
    {
        var gameplayMenu = Singleton<GameplayMenu>.Instance;
        if (gameplayMenu != null)
        {
            var selectedScreen = gameplayMenu.CurrentScreen.ToString();
            if (string.Equals(selectedScreen, "Character", StringComparison.Ordinal))
                return new PhoneAppState("Character", false, PhoneLayoutMode.Portrait, "GameplayMenu.CurrentScreen", null);

            if (!string.Equals(selectedScreen, "Phone", StringComparison.Ordinal))
                return new PhoneAppState("Unknown", false, PhoneLayoutMode.Portrait, $"GameplayMenu.CurrentScreen={selectedScreen}", null);
        }
        else
        {
            // Before GameplayMenu exists, retain the legacy fallback. Once the
            // native owner is available, its selected screen outranks stale
            // Character visibility and suppression observations.
            var characterInterface = Singleton<GameplayMenuInterface>.Instance?.CharacterInterface;
            if (characterInterface?.IsOpen == true || _characterSuppressed)
                return new PhoneAppState("Character", false, PhoneLayoutMode.Portrait, "CharacterInterface lifecycle fallback", null);
        }

        var activeApp = Phone.ActiveApp;
        if (IsModSettingsActive(activeApp, out var modSettingsSource))
            return new PhoneAppState("ModSettings", false, PhoneLayoutMode.Landscape, modSettingsSource, null);

        if (activeApp == null)
        {
            var home = HomeScreen.Instance;
            if (home?.isOpen == true)
                return new PhoneAppState("Home", true, PhoneLayoutMode.Portrait, "HomeScreen.isOpen", null);

            return new PhoneAppState("Unknown", false, PhoneLayoutMode.Portrait, "Phone.ActiveApp=null; home closed", null);
        }

        var messages = FindAppComponent<MessagesApp>(activeApp);
        if (messages != null)
            return new PhoneAppState("Messages", true, PhoneLayoutMode.Portrait, "Phone.ActiveApp/MessagesApp", messages.appContainer);

        var products = FindAppComponent<ProductManagerApp>(activeApp);
        if (products != null)
            return new PhoneAppState("Products", true, PhoneLayoutMode.Landscape, "Phone.ActiveApp/ProductManagerApp", products.appContainer);

        var deliveries = FindAppComponent<DeliveryApp>(activeApp);
        if (deliveries != null)
            return new PhoneAppState("Deliveries", true, PhoneLayoutMode.Landscape, "Phone.ActiveApp/DeliveryApp", deliveries.appContainer);

        if (FindAppComponent<JournalApp>(activeApp) != null)
            return new PhoneAppState("Journal", false, PhoneLayoutMode.Portrait, "Phone.ActiveApp/JournalApp", null);
        if (FindAppComponent<ContactsApp>(activeApp) != null)
            return new PhoneAppState("Contacts", false, PhoneLayoutMode.Portrait, "Phone.ActiveApp/ContactsApp", null);
        if (FindAppComponent<MapApp>(activeApp) != null)
            return new PhoneAppState("Map", false, PhoneLayoutMode.Landscape, "Phone.ActiveApp/MapApp", null);
        var dealers = FindAppComponent<DealerManagementApp>(activeApp);
        if (dealers != null)
        {
            var dealerLayout = dealers.Orientation == App<DealerManagementApp>.EOrientation.Horizontal
                ? PhoneLayoutMode.Landscape
                : PhoneLayoutMode.Portrait;
            return new PhoneAppState(
                "Dealers",
                true,
                dealerLayout,
                "Phone.ActiveApp/DealerManagementApp.Orientation",
                dealerLayout == PhoneLayoutMode.Landscape ? dealers.appContainer : null);
        }

        var typeName = GetFirstComponentTypeName(activeApp);
        var name = string.IsNullOrWhiteSpace(activeApp.name) ? "Unknown" : activeApp.name;
        return new PhoneAppState(name, false, PhoneLayoutMode.Portrait, $"Phone.ActiveApp/{typeName}", null);
    }

    private static T FindAppComponent<T>(GameObject activeApp) where T : Component
    {
        if (activeApp == null)
            return null;

        return activeApp.GetComponent<T>()
               ?? activeApp.GetComponentInParent<T>()
               ?? activeApp.GetComponentInChildren<T>(true);
    }

    private static string GetFirstComponentTypeName(GameObject activeApp)
    {
        var components = activeApp?.GetComponentsInParent<Component>(true);
        if (components == null)
            return "no-component";

        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null || component is Transform)
                continue;
            return component.GetType().FullName ?? component.GetType().Name;
        }

        return "no-identifying-component";
    }

    private static bool IsModSettingsActive(GameObject activeApp, out string source)
    {
        source = "ModManagerPhoneApp unavailable";
        if (activeApp == null)
            return false;

        try
        {
            ResolveModSettingsReflection();
            if (_modSettingsType == null || _modSettingsInstanceProperty == null)
                return false;

            var instance = _modSettingsInstanceProperty.GetValue(null);
            if (instance == null)
                return false;

            var panel = _modSettingsPanelField?.GetValue(instance) as GameObject;
            if (panel != null && panel.GetInstanceID() == activeApp.GetInstanceID())
            {
                source = "Phone.ActiveApp=ModSettingsAppCreator.ModManagerPanel";
                return true;
            }
        }
        catch (Exception exception)
        {
            source = $"Mod Settings reflection failed: {exception.GetType().Name}";
        }

        return false;
    }

    private static void ResolveModSettingsReflection()
    {
        if (_modSettingsReflectionResolved)
            return;

        _modSettingsReflectionResolved = true;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            var type = assemblies[i].GetType("ModManagerPhoneApp.ModSettingsAppCreator", false);
            if (type == null)
                continue;

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            _modSettingsType = type;
            _modSettingsInstanceProperty = type.GetProperty("Instance", flags);
            _modSettingsPanelField = type.GetField("ModManagerPanel", flags);
            return;
        }
    }

    private static void SetPanelInteractionEnabled(bool enabled)
    {
        enabled = enabled && _isOpen && _layoutValid;
        if (_panelRaycaster != null)
            _panelRaycaster.enabled = enabled;
        if (_previousButton != null)
            _previousButton.interactable = enabled;
        if (_nextButton != null)
            _nextButton.interactable = enabled;
        if (_sortButton != null)
            _sortButton.interactable = enabled;

        if (_phoneAccessorActive != enabled)
        {
            try
            {
                SetPhoneContextualAccessor(enabled);
            }
            catch (Exception exception)
            {
                Logger.Warning($"[PhoneBackpack] contextual accessor update skipped safely: {exception.Message}");
            }
        }

        if (!enabled && InventoryInteractionPatch.PhoneQuickMoveActive)
            InventoryInteractionPatch.EndPhoneQuickMove(_registeredItemUiManager);

        if (_panelInteractionEnabled == enabled)
            return;
        _panelInteractionEnabled = enabled;
        Logger.Diagnostic($"[PhoneBackpack] interaction {(enabled ? "enabled" : "disabled")}; panelOpen={_isOpen}, layoutValid={_layoutValid}.");
    }

    private static void SetPhoneContextualAccessor(bool enabled)
    {
        if (!enabled && ContextualBackpackPanel.IsActiveCharacterContext(_registeredItemUiManager))
        {
            // The Phone and Character projections share the Backpack's native
            // accessor. Character's active contextual panel now owns that lease.
            _phoneAccessorActive = false;
            Logger.Diagnostic("[PhoneBackpack] contextual accessor release retained for active Character context.");
            return;
        }

        PlayerBackpack.Instance?.SetContextualAccessor(enabled);
        _phoneAccessorActive = enabled;
    }

    private static void SuspendForApp(string reason)
    {
        SetPanelInteractionEnabled(false);
        if (!_isOpen)
        {
            var wasVisible = _root?.activeSelf == true;
            _suspendedForApp = true;
            if (_root != null)
                _root.SetActive(false);
            if (wasVisible)
                LogLifecycleState($"presenter.suspended reason={reason}");
            return;
        }

        LogPresentationDecision(_activeAppState, "hidden", reason);
        _isOpen = false;
        _suspendedForApp = true;
        if (_root != null)
            _root.SetActive(false);

        try
        {
            RestoreNativeOverlayLayers();
            InventoryInteractionPatch.EndPhoneQuickMove(_registeredItemUiManager);
            SetPhoneContextualAccessor(false);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[PhoneBackpack] app suspension cleanup skipped safely: {exception.Message}");
        }

        Logger.Diagnostic($"[PhoneBackpack] hidden reason={reason}.");
        LogLifecycleState($"presenter.suspended reason={reason}");
    }

    private static bool CanOpen(Phone phone, PhoneAppState appState, out string reason)
    {
        if (!Configuration.Instance.ShowBackpackWithPhone)
            reason = "Show Backpack with Phone is disabled";
        else if (phone?.IsOpen != true)
            reason = "normal phone is not open";
        else if (!appState.Allowed)
            reason = $"app-not-allowed app={appState.Name} source={appState.Source}";
        else if (PlayerBackpack.Instance == null)
            reason = "no current PlayerBackpack instance";
        else if (!PlayerBackpack.Instance.IsUnlocked)
            reason = "Backpack unlock requirement is not met";
        else if (PlayerBackpack.Instance.IsOpen)
            reason = "standalone Backpack is already open";
        else
        {
            reason = null;
            return true;
        }

        return false;
    }

    private static void Reject(string reason)
    {
        if (string.Equals(_lastRejectReason, reason, StringComparison.Ordinal))
            return;

        _lastRejectReason = reason;
        LogPresentationDecision(_activeAppState, "hidden", reason);
        Logger.Diagnostic($"[PhoneBackpack] presentation rejected reason={reason}.");
    }

    private static void LogPresentationDecision(PhoneAppState appState, string result, string reason)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        var activeApp = Phone.ActiveApp;
        var activeName = activeApp == null || string.IsNullOrWhiteSpace(activeApp.name)
            ? "<null>"
            : activeApp.name;
        var activeType = activeApp == null ? "<null>" : GetFirstComponentTypeName(activeApp);
        var geometryValid = string.Equals(result, "shown", StringComparison.Ordinal) && appState.Allowed && _layoutValid;
        var layoutCacheValid = _layoutValid;
        var decisionKey =
            $"{_phone?.IsOpen == true}|{activeName}|{activeType}|{appState.Key}|{_characterSuppressed}|" +
            $"{geometryValid}|{layoutCacheValid}|{result}|{reason}";
        if (string.Equals(_lastDecisionKey, decisionKey, StringComparison.Ordinal))
            return;

        _lastDecisionKey = decisionKey;
        Logger.Diagnostic(
            $"[PhoneBackpack] evaluate phoneOpen={_phone?.IsOpen == true} activeApp={activeName} " +
            $"activeType={activeType} identified={appState.Name} source={appState.Source} allowed={appState.Allowed} " +
            $"characterSuppressed={_characterSuppressed} modSettingsSuppressed={appState.Name == "ModSettings"} " +
            $"geometryValid={geometryValid} layoutCacheValid={layoutCacheValid} requestedLayout={appState.Layout.ToString().ToLowerInvariant()} " +
            $"result={result} reason={reason ?? "none"}.");
    }

    private static void EnsureCreated()
    {
        var manager = Singleton<ItemUIManager>.Instance;
        var storageMenu = Singleton<StorageMenu>.Instance;
        if (manager?.Canvas == null || manager.ItemSlotUIPrefab == null || storageMenu?.TitleLabel == null)
            return;

        var managerId = manager.GetInstanceID();
        if (_root != null && _createdForItemUiManagerId == managerId)
            return;

        if (_root != null || _createdForItemUiManagerId != 0)
            ResetSession("stale native UI hierarchy");

        _registeredItemUiManager = manager;
        _createdForItemUiManagerId = managerId;
        _parentRect = manager.Canvas.transform.TryCast<RectTransform>();
        if (_parentRect == null)
            return;

        _root = new GameObject("OGBackpack_PhoneLayout");
        _root.AddComponent<RectTransform>();
        _root.AddComponent<Canvas>();
        _root.AddComponent<GraphicRaycaster>();
        _rootRect = _root.GetComponent<RectTransform>();
        _panelCanvas = _root.GetComponent<Canvas>();
        _panelRaycaster = _root.GetComponent<GraphicRaycaster>();
        _rootRect.SetParent(_parentRect, false);
        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.one;
        _rootRect.offsetMin = Vector2.zero;
        _rootRect.offsetMax = Vector2.zero;
        _panelCanvas.overrideSorting = true;
        _panelCanvas.sortingLayerID = manager.Canvas.sortingLayerID;
        _panelCanvas.sortingOrder = manager.Canvas.sortingOrder + 1;
        _rootRect.SetAsFirstSibling();

        _leftPanel = CreatePanel("Left", _rootRect);
        _rightPanel = CreatePanel("Right", _rootRect);
        CreateLabel("Title", _leftPanel, "Backpack", storageMenu.TitleLabel, 220f, HeaderHeight, 22f);
        CreateLabel("Title", _rightPanel, "Backpack", storageMenu.TitleLabel, 220f, HeaderHeight, 22f);

        _leftGridRect = CreateGrid("Slots", _leftPanel, out _leftGrid, manager.ItemSlotUIPrefab.Rect.sizeDelta);
        _rightGridRect = CreateGrid("Slots", _rightPanel, out _rightGrid, manager.ItemSlotUIPrefab.Rect.sizeDelta);
        CreateControls(storageMenu.TitleLabel);
        RegisterRaycaster(manager, manager.Canvas.GetComponent<GraphicRaycaster>());
        RegisterRaycaster(manager, _panelRaycaster);
        _root.SetActive(false);
        Logger.Diagnostic($"[Lifecycle] Created dedicated Phone Backpack UI for ItemUIManager#{managerId}.");
    }

    private static RectTransform CreatePanel(string name, RectTransform parent)
    {
        var obj = new GameObject(name + "Panel");
        obj.AddComponent<RectTransform>();
        obj.AddComponent<Image>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        obj.GetComponent<Image>().color = new Color(0.025f, 0.03f, 0.035f, 0.98f);
        return rect;
    }

    private static RectTransform CreateGrid(string name, RectTransform parent, out GridLayoutGroup grid, Vector2 nativeCellSize)
    {
        var obj = new GameObject(name);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<GridLayoutGroup>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        grid = obj.GetComponent<GridLayoutGroup>();
        grid.cellSize = nativeCellSize.x > 1f && nativeCellSize.y > 1f ? nativeCellSize : new Vector2(72f, 72f);
        grid.spacing = new Vector2(GridSpacing, GridSpacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.childAlignment = TextAnchor.UpperCenter;
        return rect;
    }

    private static void CreateControls(TextMeshProUGUI template)
    {
        var obj = new GameObject("Controls");
        obj.AddComponent<RectTransform>();
        _controlsRect = obj.GetComponent<RectTransform>();
        _controlsRect.SetParent(_rightPanel, false);
        _controlsRect.anchorMin = new Vector2(0.5f, 0.5f);
        _controlsRect.anchorMax = new Vector2(0.5f, 0.5f);
        _controlsRect.pivot = new Vector2(0.5f, 0.5f);

        _previousButton = CreateButton("<", _controlsRect, new Vector2(-98f, 0f), PreviousPage, template, 38f);
        _pageLabel = CreateLabel("Page", _controlsRect, "1 / 1", template, 84f, 30f, 16f);
        _pageLabel.rectTransform.anchoredPosition = new Vector2(-35f, 0f);
        _nextButton = CreateButton(">", _controlsRect, new Vector2(28f, 0f), NextPage, template, 38f);
        _sortButton = CreateButton("Sort", _controlsRect, new Vector2(88f, 0f),
            () => PlayerBackpack.Instance?.SortContents(), template, 66f);
        _sortButton.gameObject.SetActive(Configuration.Instance.ShowSortButtons);
    }

    private static bool TryRecalculateLayout(string reason, bool force)
    {
        if (_phone == null || _parentRect == null || _leftGrid == null || _rightGrid == null)
        {
            _nextLayoutRetryAt = Time.unscaledTime + LayoutRetryIntervalSeconds;
            return false;
        }

        if (!TryGetPhoneScreenBounds(_phone, out var phoneScreenBounds, out var boundsSource))
        {
            HideForInvalidGeometry();
            Reject($"app={_activeAppState.Name} live app/phone bounds are not available yet");
            return false;
        }

        if (!AreProjectedBoundsUsable(phoneScreenBounds, _activeAppState.Layout))
        {
            HideForInvalidGeometry();
            Reject($"app={_activeAppState.Name} transient or off-screen geometry");
            return false;
        }

        var canvasRect = _parentRect.rect;
        var canvas = _registeredItemUiManager?.Canvas;
        var eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var automaticPresentationScale = ResolvePresentationScale(
            out var canvasScaleFactor,
            out var globalUiScale,
            out var globalScaleInfluence,
            out var currentScaleContribution);
        var configuredScalePercent = Configuration.Instance.PhoneBackpackUiScale;
        var presentationScale = PhoneBackpackScalePolicy.Apply(
            automaticPresentationScale,
            configuredScalePercent);
        var layoutMargin = CanvasMargin * presentationScale;
        var phoneGap = PhoneGap * presentationScale;
        var landscapeSafeBottom = canvasRect.yMin + layoutMargin;
        var nativeInventoryTop = float.NaN;
        var bottomBoundarySource = "canvas-safe-margin";
        if (_activeAppState.Layout == PhoneLayoutMode.Landscape &&
            TryGetNativeInventoryTop(eventCamera, out nativeInventoryTop, out var nativeInventorySource))
        {
            landscapeSafeBottom = Mathf.Max(
                landscapeSafeBottom,
                nativeInventoryTop + NativeInventoryGap * presentationScale);
            bottomBoundarySource = nativeInventorySource;
        }

        var landscapeBottomUnchanged = _activeAppState.Layout != PhoneLayoutMode.Landscape ||
                                       Mathf.Abs(_lastLandscapeSafeBottom - landscapeSafeBottom) <= BoundsEpsilon;
        var presentationScaleUnchanged = Mathf.Abs(_lastPresentationScale - presentationScale) <= 0.001f;
        if (!force && _layoutValid && NearlyEqual(_lastPhoneScreenBounds, phoneScreenBounds) &&
            NearlyEqual(_lastCanvasRect, canvasRect) && _activeAppState.Layout == _lastLayoutMode &&
            landscapeBottomUnchanged && presentationScaleUnchanged)
            return true;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect, new Vector2(phoneScreenBounds.xMin, phoneScreenBounds.yMin), eventCamera, out var phoneMin) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect, new Vector2(phoneScreenBounds.xMax, phoneScreenBounds.yMax), eventCamera, out var phoneMax))
        {
            HideForInvalidGeometry();
            Reject("phone bounds could not be converted into the inventory canvas");
            return false;
        }

        var cell = _leftGrid.cellSize;
        var spacing = _leftGrid.spacing;
        Rect firstUsable;
        Rect secondUsable;
        string firstName;
        string secondName;
        int firstMaxColumns;
        int firstMaxRows;
        int secondMaxColumns;
        int secondMaxRows;
        if (_activeAppState.Layout == PhoneLayoutMode.Portrait)
        {
            firstName = "left";
            secondName = "right";
            firstUsable = Rect.MinMaxRect(
                canvasRect.xMin + layoutMargin,
                canvasRect.yMin + layoutMargin,
                Mathf.Min(canvasRect.xMax, phoneMin.x - phoneGap),
                canvasRect.yMax - layoutMargin);
            secondUsable = Rect.MinMaxRect(
                Mathf.Max(canvasRect.xMin, phoneMax.x + phoneGap),
                canvasRect.yMin + layoutMargin,
                canvasRect.xMax - layoutMargin,
                canvasRect.yMax - layoutMargin);
            firstMaxColumns = MaxColumnsPerSide;
            firstMaxRows = MaxRows;
            secondMaxColumns = MaxColumnsPerSide;
            secondMaxRows = MaxPortraitRightRows;
        }
        else
        {
            firstName = "bottom";
            secondName = "right";
            firstUsable = Rect.MinMaxRect(
                Mathf.Max(canvasRect.xMin + layoutMargin, phoneMin.x),
                landscapeSafeBottom,
                Mathf.Min(canvasRect.xMax - layoutMargin, phoneMax.x),
                Mathf.Min(canvasRect.yMax - layoutMargin, phoneMin.y - phoneGap));
            secondUsable = Rect.MinMaxRect(
                Mathf.Max(canvasRect.xMin + layoutMargin, phoneMax.x + phoneGap),
                canvasRect.yMin + layoutMargin,
                canvasRect.xMax - layoutMargin,
                canvasRect.yMax - layoutMargin);
            firstMaxColumns = MaxLandscapeBottomColumns;
            firstMaxRows = MaxLandscapeBottomRows;
            secondMaxColumns = MaxLandscapeRightColumns;
            secondMaxRows = MaxRows;
        }

        var landscape = _activeAppState.Layout == PhoneLayoutMode.Landscape;
        var firstHasTitle = false;
        var secondHasTitle = landscape;
        var firstToolbarCount = landscape ? 0 : 1;
        var secondToolbarCount = landscape ? 1 : 0;
        var firstHorizontalAlignment = landscape
            ? PanelHorizontalAlignment.Center
            : PanelHorizontalAlignment.NearRight;
        var secondHorizontalAlignment = PanelHorizontalAlignment.NearLeft;
        var first = FitPanel(
            firstUsable,
            cell,
            spacing,
            firstMaxColumns,
            firstMaxRows,
            firstToolbarCount,
            firstHasTitle,
            landscape,
            firstHorizontalAlignment,
            presentationScale);
        var second = FitPanel(
            secondUsable,
            cell,
            spacing,
            secondMaxColumns,
            secondMaxRows,
            secondToolbarCount,
            secondHasTitle,
            false,
            secondHorizontalAlignment,
            presentationScale);
        if (!first.IsUsable && !second.IsUsable)
        {
            HideForInvalidGeometry();
            Reject(
                $"app={_activeAppState.Name} insufficient-safe-space " +
                $"{firstName}={firstUsable.width:F0}x{firstUsable.height:F0} " +
                $"{secondName}={secondUsable.width:F0}x{secondUsable.height:F0}");
            return false;
        }

        if (first.IsUsable && !second.IsUsable)
            first = FitPanel(
                firstUsable,
                cell,
                spacing,
                firstMaxColumns,
                firstMaxRows,
                1,
                firstHasTitle,
                landscape,
                firstHorizontalAlignment,
                presentationScale);
        else if (!first.IsUsable && second.IsUsable)
            second = FitPanel(
                secondUsable,
                cell,
                spacing,
                secondMaxColumns,
                secondMaxRows,
                1,
                true,
                false,
                secondHorizontalAlignment,
                presentationScale);

        if (!first.IsUsable && !second.IsUsable)
        {
            HideForInvalidGeometry();
            Reject($"app={_activeAppState.Name} one-region layout cannot fit pagination controls");
            return false;
        }

        ApplyAdaptivePanels(first, second, firstHasTitle, secondHasTitle, landscape, presentationScale);
        _leftColumns = first.Columns;
        _leftRows = first.Rows;
        _rightColumns = second.Columns;
        _rightRows = second.Rows;
        _visibleSlots = Mathf.Max(1, first.Capacity + second.Capacity);
        _leftGrid.constraintCount = Mathf.Max(1, first.Columns);
        _rightGrid.constraintCount = Mathf.Max(1, second.Columns);
        _lastPhoneScreenBounds = phoneScreenBounds;
        _lastCanvasRect = canvasRect;
        _lastLandscapeSafeBottom = landscapeSafeBottom;
        _lastPresentationScale = presentationScale;
        _lastLayoutMode = _activeAppState.Layout;
        _layoutValid = true;
        _lastRejectReason = null;
        if (_isOpen && _root != null && !_root.activeSelf)
            _root.SetActive(true);

        EnsureSlotUis(_leftColumns, _leftRows, _rightColumns, _rightRows);
        ApplyPage();
        if (Logger.DiagnosticLoggingEnabled && (force || Time.unscaledTime >= _nextLayoutLogTime))
        {
            _nextLayoutLogTime = Time.unscaledTime + 0.25f;
            var nativeInventoryTopText = float.IsNaN(nativeInventoryTop) ? "unavailable" : nativeInventoryTop.ToString("F0");
            var scaleDetails =
                $" canvasScale={canvasScaleFactor:F3} globalUiScale={globalUiScale:F3} " +
                $"globalInfluence={globalScaleInfluence:F3} scaleContribution={currentScaleContribution:F3} " +
                $"autoCompensation={automaticPresentationScale:F3} configuredScale={configuredScalePercent}% " +
                $"presentationScale={presentationScale:F3}";
            var landscapeDetails = landscape
                ? $" appBottom={phoneMin.y:F0} appRight={phoneMax.x:F0} nativeInventoryTop={nativeInventoryTopText} " +
                  $"bottomBoundary={bottomBoundarySource} safeBottom={landscapeSafeBottom:F0} " +
                  $"bottomRegionTop={firstUsable.yMax:F0} bottomRegionBottom={firstUsable.yMin:F0} " +
                  $"bottomColumns={first.Columns} bottomRows={first.Rows} " +
                  $"rightColumns={second.Columns} rightRows={second.Rows}"
                : string.Empty;
            Logger.Diagnostic(
                $"[PhoneBackpack] layout={_activeAppState.Layout.ToString().ToLowerInvariant()} app={_activeAppState.Name} " +
                $"reason={reason} source={boundsSource} phone={Format(phoneScreenBounds)} " +
                $"{firstName}Region={Format(firstUsable)} panel={Format(first.Bounds)} " +
                $"slots={first.Capacity}({first.Columns}x{first.Rows}) " +
                $"{secondName}Region={Format(secondUsable)} panel={Format(second.Bounds)} " +
                $"slots={second.Capacity}({second.Columns}x{second.Rows}) " +
                $"rendered={_visibleSlots} pages={_totalPages}.{scaleDetails}{landscapeDetails}");
        }

        return true;
    }

    private static float ResolvePresentationScale(
        out float canvasScaleFactor,
        out float globalUiScale,
        out float globalScaleInfluence,
        out float currentScaleContribution)
    {
        var canvas = _registeredItemUiManager?.Canvas;
        canvasScaleFactor = canvas?.scaleFactor ?? 1f;
        globalUiScale = GameCanvasScaler.GlobalScaleFactor;
        globalScaleInfluence = 0f;
        currentScaleContribution = 1f;
        if (canvas == null)
            return 1f;

        var scheduleScaler = canvas.GetComponent<GameCanvasScaler>()
                             ?? canvas.GetComponentInParent<GameCanvasScaler>();
        if (scheduleScaler == null)
            return 1f;

        globalScaleInfluence = Mathf.Clamp01(scheduleScaler._globalScaleInfluence);
        currentScaleContribution = Mathf.Lerp(1f, globalUiScale, globalScaleInfluence);
        if (currentScaleContribution <= 0.001f)
            return 1f;

        var referenceUiScale = Mathf.Min(globalUiScale, ReferenceGlobalUiScale);
        var referenceContribution = Mathf.Lerp(1f, referenceUiScale, globalScaleInfluence);
        return Mathf.Clamp(referenceContribution / currentScaleContribution, 0.25f, 1f);
    }

    private static PanelMetrics FitPanel(
        Rect usable,
        Vector2 cell,
        Vector2 spacing,
        int maxColumns,
        int maxRows,
        int toolbarCount,
        bool includeTitle,
        bool alignTop,
        PanelHorizontalAlignment horizontalAlignment,
        float presentationScale)
    {
        if (presentationScale <= 0f || usable.width < MinimumPanelWidth * presentationScale || usable.height <= 0f)
            return default;

        var availableContentWidth = usable.width / presentationScale;
        var availableContentHeight = usable.height / presentationScale;
        var interiorWidth = availableContentWidth - PanelPadding * 2f;
        var columns = Mathf.Clamp(
            Mathf.FloorToInt((interiorWidth + spacing.x) / (cell.x + spacing.x)),
            0,
            maxColumns);
        var fixedHeight = PanelPadding * 2f + toolbarCount * (ToolbarHeight + SectionGap) +
                          (includeTitle ? HeaderHeight + SectionGap : 0f);
        var rows = Mathf.Clamp(
            Mathf.FloorToInt((availableContentHeight - fixedHeight + spacing.y) / (cell.y + spacing.y)),
            0,
            maxRows);
        if (columns < 1 || rows < 1)
            return default;

        var gridWidth = columns * cell.x + Mathf.Max(0, columns - 1) * spacing.x;
        var gridHeight = rows * cell.y + Mathf.Max(0, rows - 1) * spacing.y;
        var panelWidth = Mathf.Max(MinimumPanelWidth, gridWidth + PanelPadding * 2f) * presentationScale;
        var panelHeight = (fixedHeight + gridHeight) * presentationScale;
        var bounds = PositionInside(usable, panelWidth, panelHeight, horizontalAlignment, alignTop);
        return new PanelMetrics(bounds, columns, rows, gridWidth, gridHeight);
    }

    private static Rect PositionInside(
        Rect usable,
        float width,
        float height,
        PanelHorizontalAlignment horizontalAlignment,
        bool alignTop)
    {
        width = Mathf.Min(width, usable.width);
        height = Mathf.Min(height, usable.height);
        var centeredX = usable.center.x - width * 0.5f;
        var phoneFacingSetback = _activeAppState.Layout == PhoneLayoutMode.Portrait
            ? Mathf.Clamp01(Configuration.Instance.PhoneBackpackDistance / 100f)
            : DefaultPhoneFacingSetbackFraction;
        var x = horizontalAlignment switch
        {
            PanelHorizontalAlignment.NearLeft => Mathf.Lerp(usable.xMin, centeredX, phoneFacingSetback),
            PanelHorizontalAlignment.NearRight => Mathf.Lerp(usable.xMax - width, centeredX, phoneFacingSetback),
            _ => centeredX
        };
        var y = alignTop ? usable.yMax - height : usable.center.y - height * 0.5f;
        return new Rect(x, y, width, height);
    }

    private static void ApplyAdaptivePanels(
        PanelMetrics first,
        PanelMetrics second,
        bool firstHasTitle,
        bool secondHasTitle,
        bool landscape,
        float presentationScale)
    {
        _leftPanel.gameObject.SetActive(first.IsUsable);
        _rightPanel.gameObject.SetActive(second.IsUsable);

        if (first.IsUsable && second.IsUsable)
        {
            if (landscape)
            {
                LayoutStandardControls();
                ApplyPanelLayout(_leftPanel, _leftGridRect, first, firstHasTitle, presentationScale);
                ApplyPanelLayout(_rightPanel, _rightGridRect, second, secondHasTitle, presentationScale, _controlsRect);
            }
            else
            {
                LayoutCompactControls();
                ApplyPanelLayout(_leftPanel, _leftGridRect, first, false, presentationScale, _controlsRect);
                ApplyPanelLayout(_rightPanel, _rightGridRect, second, false, presentationScale);
            }
        }
        else if (first.IsUsable)
        {
            if (landscape)
                LayoutStandardControls();
            else
                LayoutCompactControls();
            ApplyPanelLayout(_leftPanel, _leftGridRect, first, firstHasTitle, presentationScale, _controlsRect);
        }
        else
        {
            if (landscape)
                LayoutStandardControls();
            else
                LayoutCompactControls();
            ApplyPanelLayout(_rightPanel, _rightGridRect, second, true, presentationScale, _controlsRect);
        }
    }

    private static void LayoutCompactControls()
    {
        SetButtonText(_previousButton, "‹");
        SetButtonText(_nextButton, "›");
        SetButtonLayout(_previousButton, -73f, 28f);
        SetLabelLayout(_pageLabel, -29f, 54f);
        SetButtonLayout(_nextButton, 15f, 28f);
        SetButtonLayout(_sortButton, 57f, 48f);
    }

    private static void LayoutStandardControls()
    {
        SetButtonText(_previousButton, "<");
        SetButtonText(_nextButton, ">");
        SetButtonLayout(_previousButton, -98f, 38f);
        SetLabelLayout(_pageLabel, -35f, 84f);
        SetButtonLayout(_nextButton, 28f, 38f);
        SetButtonLayout(_sortButton, 88f, 66f);
    }

    private static void SetButtonLayout(Button button, float x, float width)
    {
        var rect = button?.GetComponent<RectTransform>();
        if (rect == null)
            return;

        rect.sizeDelta = new Vector2(width, 34f);
        rect.anchoredPosition = new Vector2(x, 0f);
        var label = rect.Find("Label")?.GetComponent<RectTransform>();
        if (label != null)
            label.sizeDelta = new Vector2(Mathf.Max(1f, width - 6f), 30f);
    }

    private static void SetButtonText(Button button, string text)
    {
        var label = button?.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (label != null)
            label.text = text;
    }

    private static void SetLabelLayout(TextMeshProUGUI label, float x, float width)
    {
        if (label?.rectTransform == null)
            return;

        label.rectTransform.sizeDelta = new Vector2(width, 30f);
        label.rectTransform.anchoredPosition = new Vector2(x, 0f);
    }

    private static void ApplyPanelLayout(
        RectTransform panel,
        RectTransform grid,
        PanelMetrics metrics,
        bool showTitle,
        float presentationScale,
        params RectTransform[] toolbars)
    {
        panel.localScale = new Vector3(presentationScale, presentationScale, 1f);
        panel.sizeDelta = metrics.Bounds.size / presentationScale;
        panel.anchoredPosition = metrics.Bounds.center;

        // Child positions are expressed in the panel's unscaled local space.
        // metrics.Bounds is the compensated footprint in parent space, so using
        // its height here left an empty strip above the content when scale < 1.
        var top = panel.sizeDelta.y * 0.5f - PanelPadding;
        var title = panel.Find("Title")?.GetComponent<RectTransform>();
        if (title != null)
        {
            title.gameObject.SetActive(showTitle);
            if (showTitle)
            {
                title.anchoredPosition = new Vector2(0f, top - HeaderHeight * 0.5f);
                top -= HeaderHeight + SectionGap;
            }
        }
        for (var i = 0; i < toolbars.Length; i++)
        {
            var toolbar = toolbars[i];
            toolbar.SetParent(panel, false);
            toolbar.gameObject.SetActive(true);
            toolbar.sizeDelta = new Vector2(metrics.Bounds.width - PanelPadding * 2f, ToolbarHeight);
            toolbar.anchoredPosition = new Vector2(0f, top - ToolbarHeight * 0.5f);
            top -= ToolbarHeight + SectionGap;
        }
        grid.sizeDelta = new Vector2(metrics.GridWidth, metrics.GridHeight);
        grid.anchoredPosition = new Vector2(0f, top - metrics.GridHeight * 0.5f);
    }

    private static void HideForInvalidGeometry()
    {
        _layoutValid = false;
        _nextLayoutRetryAt = Time.unscaledTime + LayoutRetryIntervalSeconds;
        if (_isOpen)
            SuspendForApp("phone/canvas geometry temporarily invalid");
        if (_root != null)
            _root.SetActive(false);
    }

    private static bool AreProjectedBoundsUsable(Rect bounds, PhoneLayoutMode layout)
    {
        if (float.IsNaN(bounds.x) || float.IsNaN(bounds.y) || float.IsNaN(bounds.width) || float.IsNaN(bounds.height) ||
            float.IsInfinity(bounds.x) || float.IsInfinity(bounds.y) || bounds.width < 100f || bounds.height < 100f)
            return false;

        var screen = new Rect(0f, 0f, Screen.width, Screen.height);
        var horizontalOverlap = Mathf.Max(0f, Mathf.Min(bounds.xMax, screen.xMax) - Mathf.Max(bounds.xMin, screen.xMin));
        if (horizontalOverlap < Mathf.Min(100f, bounds.width * 0.5f))
            return false;

        if (layout == PhoneLayoutMode.Portrait)
            return true;

        var verticalOverlap = Mathf.Max(0f, Mathf.Min(bounds.yMax, screen.yMax) - Mathf.Max(bounds.yMin, screen.yMin));
        if (verticalOverlap < Mathf.Min(100f, bounds.height * 0.5f))
            return false;

        const float settledTolerance = 8f;
        return bounds.xMin >= -settledTolerance && bounds.yMin >= -settledTolerance &&
               bounds.xMax <= Screen.width + settledTolerance && bounds.yMax <= Screen.height + settledTolerance;
    }

    private static bool TryGetPhoneScreenBounds(Phone phone, out Rect result, out string source)
    {
        result = default;
        source = null;
        var camera = Camera.main ?? phone.raycaster?.eventCamera;
        if (camera == null)
            return false;

        if (_activeAppState.Layout == PhoneLayoutMode.Landscape && _activeAppState.BoundsRect != null &&
            TryGetRectTransformScreenBounds(_activeAppState.BoundsRect, camera, out result))
        {
            source = $"{_activeAppState.Name}-appContainer";
            return true;
        }

        var hasPoint = false;
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var renderers = phone.phoneModel?.GetComponentsInChildren<Renderer>(true);
        if (renderers != null)
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                var bounds = renderer.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var world = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var screen = camera.WorldToScreenPoint(world);
                    if (screen.z <= 0f)
                        continue;

                    minimum.x = Mathf.Min(minimum.x, screen.x);
                    minimum.y = Mathf.Min(minimum.y, screen.y);
                    maximum.x = Mathf.Max(maximum.x, screen.x);
                    maximum.y = Mathf.Max(maximum.y, screen.y);
                    hasPoint = true;
                }
            }
        }

        if (hasPoint)
        {
            result = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            source = "phone-model";
            return result.width > 1f && result.height > 1f;
        }

        var raycasterRect = phone.raycaster?.transform.TryCast<RectTransform>();
        var phoneCanvas = phone.raycaster?.GetComponent<Canvas>();
        if (raycasterRect == null || phoneCanvas == null)
            return false;

        minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var rect = raycasterRect.rect;
        for (var corner = 0; corner < 4; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? rect.xMin : rect.xMax,
                (corner & 2) == 0 ? rect.yMin : rect.yMax,
                0f);
            var world = raycasterRect.TransformPoint(local);
            var screen = RectTransformUtility.WorldToScreenPoint(phoneCanvas.worldCamera ?? camera, world);
            minimum.x = Mathf.Min(minimum.x, screen.x);
            minimum.y = Mathf.Min(minimum.y, screen.y);
            maximum.x = Mathf.Max(maximum.x, screen.x);
            maximum.y = Mathf.Max(maximum.y, screen.y);
        }

        result = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        source = "phone-canvas";
        return result.width > 1f && result.height > 1f;
    }

    private static bool TryGetRectTransformScreenBounds(RectTransform rectTransform, Camera fallbackCamera, out Rect result)
    {
        result = default;
        if (rectTransform == null || !rectTransform.gameObject.activeInHierarchy)
            return false;

        var canvas = rectTransform.GetComponentInParent<Canvas>();
        var camera = canvas == null
            ? fallbackCamera
            : canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera ?? fallbackCamera;
        var rect = rectTransform.rect;
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var corner = 0; corner < 4; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? rect.xMin : rect.xMax,
                (corner & 2) == 0 ? rect.yMin : rect.yMax,
                0f);
            var world = rectTransform.TransformPoint(local);
            var screen = RectTransformUtility.WorldToScreenPoint(camera, world);
            minimum.x = Mathf.Min(minimum.x, screen.x);
            minimum.y = Mathf.Min(minimum.y, screen.y);
            maximum.x = Mathf.Max(maximum.x, screen.x);
            maximum.y = Mathf.Max(maximum.y, screen.y);
        }

        result = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        return result.width > 1f && result.height > 1f;
    }

    private static bool TryGetNativeInventoryTop(Camera eventCamera, out float top, out string source)
    {
        top = float.NegativeInfinity;
        source = null;
        var hud = Singleton<HUD>.Instance;
        if (hud == null || _parentRect == null)
            return false;

        var fallbackCamera = Camera.main ?? _phone?.raycaster?.eventCamera;
        IncludeNativeInventoryTop(hud.HotbarContainer, "HUD.HotbarContainer", eventCamera, fallbackCamera, ref top, ref source);
        IncludeNativeInventoryTop(hud.SlotContainer, "HUD.SlotContainer", eventCamera, fallbackCamera, ref top, ref source);
        IncludeNativeInventoryTop(hud.cashSlotUI, "HUD.cashSlotUI", eventCamera, fallbackCamera, ref top, ref source);
        IncludeNativeInventoryTop(hud.onlineBalanceSlotUI, "HUD.onlineBalanceSlotUI", eventCamera, fallbackCamera, ref top, ref source);
        IncludeNativeInventoryTop(hud.managementSlotUI?.Rect, "HUD.managementSlotUI", eventCamera, fallbackCamera, ref top, ref source);
        IncludeNativeInventoryTop(hud.discardSlot?.Rect, "HUD.discardSlot", eventCamera, fallbackCamera, ref top, ref source);
        return !float.IsNegativeInfinity(top);
    }

    private static void IncludeNativeInventoryTop(
        RectTransform candidate,
        string candidateSource,
        Camera eventCamera,
        Camera fallbackCamera,
        ref float top,
        ref string source)
    {
        if (!TryGetRectTransformScreenBounds(candidate, fallbackCamera, out var screenBounds) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect, new Vector2(screenBounds.center.x, screenBounds.yMax), eventCamera, out var localTop) ||
            localTop.y <= top)
            return;

        top = localTop.y;
        source = candidateSource;
    }

    private static void EnsureSlotUis(int leftColumns, int leftRows, int rightColumns, int rightRows)
    {
        EnsureSlotUis(LeftSlotUis, _leftGridRect, leftColumns * leftRows, "LeftPhoneBackpackSlot_");
        EnsureSlotUis(RightSlotUis, _rightGridRect, rightColumns * rightRows, "RightPhoneBackpackSlot_");
    }

    private static void EnsureSlotUis(List<ItemSlotUI> slots, RectTransform parent, int count, string prefix)
    {
        if (parent == null || _registeredItemUiManager?.ItemSlotUIPrefab == null)
            return;

        var stale = false;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null)
            {
                stale = true;
                break;
            }
        }

        if (stale)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                    UnityEngine.Object.Destroy(slots[i].gameObject);
            }
            slots.Clear();
        }

        var prefab = _registeredItemUiManager.ItemSlotUIPrefab.gameObject;
        while (slots.Count < count)
        {
            var obj = UnityEngine.Object.Instantiate(prefab, parent);
            obj.name = prefix + slots.Count;
            obj.SetActive(true);
            var slotUi = obj.GetComponent<ItemSlotUI>();
            slotUi.ClearSlot();
            slots.Add(slotUi);
        }
    }

    private static void ApplyPage()
    {
        if (!_layoutValid || PlayerBackpack.Instance == null)
            return;

        _totalPages = Mathf.Max(1, Mathf.CeilToInt(PlayerBackpack.Instance.BackpackSlotCount / (float)_visibleSlots));
        _currentPage = Mathf.Clamp(_currentPage, 0, _totalPages - 1);
        var start = _currentPage * _visibleSlots;
        var visibleIndex = 0;
        ApplySlots(LeftSlotUis, _leftColumns * _leftRows, start, ref visibleIndex);
        ApplySlots(RightSlotUis, _rightColumns * _rightRows, start, ref visibleIndex);

        if (_pageLabel != null)
            _pageLabel.text = $"{_currentPage + 1} / {_totalPages}";
        if (_previousButton != null)
            _previousButton.interactable = _currentPage > 0;
        if (_nextButton != null)
            _nextButton.interactable = _currentPage < _totalPages - 1;
    }

    private static void ApplySlots(List<ItemSlotUI> slots, int capacity, int pageStart, ref int visibleIndex)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var slotUi = slots[i];
            if (slotUi == null)
                continue;

            var inventoryIndex = i < capacity && pageStart + visibleIndex < PlayerBackpack.Instance.BackpackSlotCount
                ? pageStart + visibleIndex
                : -1;
            var shouldShow = i < capacity && inventoryIndex >= 0;
            if (!shouldShow)
            {
                if (slotUi.assignedSlot != null)
                    slotUi.ClearSlot();
                slotUi.gameObject.SetActive(false);
            }
            else
            {
                var target = PlayerBackpack.Instance.GetBackpackSlot(inventoryIndex);
                slotUi.gameObject.SetActive(true);
                if (!PlayerBackpack.SlotsMatch(slotUi.assignedSlot, target))
                {
                    if (slotUi.assignedSlot != null)
                        slotUi.ClearSlot();
                    slotUi.AssignSlot(target);
                }
                slotUi.SetVisible(true);
            }

            if (i < capacity)
                visibleIndex++;
        }
    }

    private static void ClearSlotBindings()
    {
        ClearSlotBindings(LeftSlotUis);
        ClearSlotBindings(RightSlotUis);
    }

    private static void ClearSlotBindings(List<ItemSlotUI> slots)
    {
        for (var i = 0; i < slots.Count; i++)
            slots[i]?.ClearSlot();
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

    private static Button CreateButton(
        string text,
        RectTransform parent,
        Vector2 position,
        Action onClick,
        TextMeshProUGUI template,
        float width)
    {
        var obj = new GameObject("Button_" + text);
        obj.AddComponent<RectTransform>();
        obj.AddComponent<Image>();
        obj.AddComponent<Button>();
        var rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, 34f);
        rect.anchoredPosition = position;
        obj.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.1f, 0.96f);
        var button = obj.GetComponent<Button>();
        button.onClick.AddListener((UnityAction)onClick);
        CreateLabel("Label", rect, text, template, width - 6f, 30f, 17f);
        return button;
    }

    private static TextMeshProUGUI CreateLabel(
        string name,
        RectTransform parent,
        string text,
        TextMeshProUGUI template,
        float width,
        float height,
        float fontSize)
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
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        return label;
    }

    private static bool ContainsSlotRect(List<ItemSlotUI> slots, RectTransform candidate)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var rect = slots[i]?.Rect;
            if (rect != null && rect.GetInstanceID() == candidate.GetInstanceID())
                return true;
        }
        return false;
    }

    private static void RegisterRaycaster(ItemUIManager manager, GraphicRaycaster raycaster)
    {
        if (manager == null || raycaster == null)
            return;

        for (var i = 0; i < manager._raycasters.Count; i++)
        {
#if IL2CPP
            var registered = manager._raycasters[new Index(i)];
#elif MONO
            var registered = manager._raycasters[i];
#endif
            if (registered != null && registered.GetInstanceID() == raycaster.GetInstanceID())
                return;
        }
        manager.AddRaycaster(raycaster);
    }

    private static void UnregisterRaycaster()
    {
        if (_registeredItemUiManager == null || _panelRaycaster == null)
            return;

        var id = _panelRaycaster.GetInstanceID();
        for (var i = _registeredItemUiManager._raycasters.Count - 1; i >= 0; i--)
        {
#if IL2CPP
            var registered = _registeredItemUiManager._raycasters[new Index(i)];
#elif MONO
            var registered = _registeredItemUiManager._raycasters[i];
#endif
            if (registered != null && registered.GetInstanceID() == id)
                _registeredItemUiManager._raycasters.RemoveAt(i);
        }
    }

    private static void EnsureTooltipLayer(ItemUIManager manager)
    {
        var infoPanel = manager.InfoPanel;
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
        if (nativeCanvas != null && nativeCanvas.GetInstanceID() != manager.Canvas.GetInstanceID())
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

    private static void EnsureDragLayer(ItemUIManager manager)
    {
        var target = manager.tempIcon;
        if (!manager.IsCurrentlyDragging || target == null)
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

    private static void ApplyOverlayOrder(Canvas canvas, int order)
    {
        if (canvas == null || _panelCanvas == null)
            return;

        canvas.overrideSorting = true;
        canvas.sortingLayerID = _panelCanvas.sortingLayerID;
        canvas.sortingOrder = order;
    }

    private static void CaptureCanvasState(Canvas canvas, out bool overrideSorting, out int layerId, out int order)
    {
        overrideSorting = canvas != null && canvas.overrideSorting;
        layerId = canvas?.sortingLayerID ?? 0;
        order = canvas?.sortingOrder ?? 0;
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
    }

    private static void RestoreCanvas(Canvas canvas, bool wasAdded, bool overrideSorting, int layerId, int order)
    {
        if (canvas == null)
            return;

        if (wasAdded)
        {
            UnityEngine.Object.Destroy(canvas);
            return;
        }

        canvas.overrideSorting = overrideSorting;
        canvas.sortingLayerID = layerId;
        canvas.sortingOrder = order;
    }

    private static bool NearlyEqual(Rect left, Rect right)
    {
        return Mathf.Abs(left.x - right.x) <= BoundsEpsilon &&
               Mathf.Abs(left.y - right.y) <= BoundsEpsilon &&
               Mathf.Abs(left.width - right.width) <= BoundsEpsilon &&
               Mathf.Abs(left.height - right.height) <= BoundsEpsilon;
    }

    private static string Format(Rect rect)
    {
        return $"({rect.xMin:F0},{rect.yMin:F0})-({rect.xMax:F0},{rect.yMax:F0})";
    }
}
