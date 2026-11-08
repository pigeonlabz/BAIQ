using Backpack.Config;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppTMPro;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using TMPro;
#endif

namespace Backpack.UI;

/// <summary>
/// Experimental, per-open movement for the native StorageMenu container.
/// This deliberately does not alter the native slot tree or save positions.
/// </summary>
public static class MovableStorageWindow
{
    private const int MinimumCaptureFrames = 3;
    private const int StableCaptureFrames = 3;
    private const int MaximumCaptureFrames = 120;
    private const int BoundsDiagnosticIntervalFrames = 15;
    private const float PositionStabilityEpsilon = 0.5f;
    private const float ScreenMargin = 16f;
    private const float HeaderHeight = 28f;
    private const float HeaderGap = 5f;
    private const float LockButtonSize = 24f;
    private const int MaximumRememberedStoragePlacements = 32;

    private sealed class PlacementMemory
    {
        public StorageEntity Entity;
        public Vector2 CanvasOffset;
    }

    private static readonly Dictionary<int, PlacementMemory> RememberedPlacements = new();
    private static readonly Queue<int> RememberedPlacementOrder = new();

    private static StorageMenu _menu;
    private static RectTransform _trackedContainer;
    private static RectTransform _capturedCanvasRect;
    private static int _activeStorageInstanceId;
    private static Vector3 _lockedWorldPosition;
    private static bool _workspacePositionCorrectionLogged;
    private static GameObject _headerObject;
    private static RectTransform _headerRect;
    private static GameObject _lockButtonObject;
    private static RectTransform _lockButtonRect;
    private static RectTransform _lockShackleLeft;
    private static RectTransform _lockShackleRight;
    private static RectTransform _lockShackleTop;
    private static RectTransform _lockBody;
    private static int _openedAtFrame;
    private static int _stableFrames;
    private static Vector2 _lastObservedPosition;
    private static Vector3 _lastObservedWorldPosition;
    private static Vector2 _vanillaPosition;
    private static Vector3 _vanillaWorldPosition;
    private static bool _positionCaptured;
    private static bool _isDragging;
    private static bool _isLocked = true;
    private static bool _hierarchyLogged;
    private static bool _hasLastWrittenPosition;
    private static bool _positionResetLogged;
    private static string _lastBoundsDiagnosticSignature;
    private static int _lastBoundsDiagnosticFrame = -1;
    private static Rect _nativeContentBoundsInContainer;
    private static Rect _nativeContentBoundsInCanvasAtCapture;
    private static Rect _movableBoundsInCanvasAtCapture;
    private static bool _nativeContentBoundsCaptured;
    private static int _capturedActiveSlotCount;
    private static Vector2 _dragStartPointerLocal;
    private static Vector2 _dragStartScreenPointer;
    private static Vector2 _dragStartAnchoredPosition;
    private static Vector2 _dragStartCanvasOffset;
    private static Vector3 _lastWrittenWorldPosition;
    private static bool _dragXMotionLogged;
    private static bool _dragYMotionLogged;
    private static bool _dragPointerConversionFailedLogged;
    private static bool _clampNoFitLogged;
    private static bool _lastDragClampX;
    private static bool _lastDragClampY;
    private static bool _lastClampUsedCanvasFallback;
    private static Vector2 _lastDragPointerDelta;
    private static Vector2 _lastDragScreenDelta;
    private static Vector2 _lastDragAppliedDelta;
    private static Vector2 _lastDragMinAllowedDelta;
    private static Vector2 _lastDragMaxAllowedDelta;
    private static Rect _lastVisibleBoundsInCanvas;

    public static void BeforeOpen(StorageMenu menu)
    {
        if (_menu == null)
            return;

        RestoreAndClear("another native storage window opened", Configuration.Instance.ExperimentalMovableStorageWindows);
    }

    public static void OnStorageOpened(StorageMenu menu, string title)
    {
        if (menu == null || title == PlayerBackpack.StorageName || menu.OpenedStorageEntity == null)
            return;

        if (!Configuration.Instance.ExperimentalMovableStorageWindows)
        {
            Logger.Diagnostic($"[ExperimentalUIWorkspace] Runtime activation skipped for '{title}'; preference=false.");
            return;
        }

        if (_menu == menu)
            return;

        if (_menu != null)
            RestoreAndClear("storage window reopened", Configuration.Instance.ExperimentalMovableStorageWindows);

        var container = GetContainerRect(menu);
        if (container == null)
        {
            Logger.Warning("[ExperimentalUIWorkspace] Native StorageMenu has no RectTransform container; movable storage support was skipped.");
            return;
        }

        _menu = menu;
        _trackedContainer = container;
        _activeStorageInstanceId = menu.OpenedStorageEntity.GetInstanceID();
        _capturedCanvasRect = null;
        _openedAtFrame = Time.frameCount;
        _stableFrames = 0;
        _lastObservedPosition = container.anchoredPosition;
        _lastObservedWorldPosition = container.position;
        _positionCaptured = false;
        _isDragging = false;
        _isLocked = true;
        _hierarchyLogged = false;
        _hasLastWrittenPosition = false;
        _positionResetLogged = false;
        _lastBoundsDiagnosticSignature = null;
        _lastBoundsDiagnosticFrame = -1;
        _nativeContentBoundsInContainer = default;
        _nativeContentBoundsInCanvasAtCapture = default;
        _movableBoundsInCanvasAtCapture = default;
        _nativeContentBoundsCaptured = false;
        _capturedActiveSlotCount = 0;
        _workspacePositionCorrectionLogged = false;
        Logger.Diagnostic($"[ExperimentalUIWorkspace] Runtime activation accepted for ordinary StorageEntity '{title}'; preference=true, waiting for native placement to settle.");
    }

    public static void OnStorageClosed(StorageMenu menu)
    {
        if (_menu == menu)
            RestoreAndClear("native storage window closed", preservePlacement: Configuration.Instance.ExperimentalMovableStorageWindows);
    }

    public static void Update()
    {
        if (_menu == null)
            return;

        if (!Configuration.Instance.ExperimentalMovableStorageWindows)
        {
            RestoreAndClear("experimental setting disabled", preservePlacement: false);
            return;
        }

        if (!_menu.IsOpen)
        {
            RestoreAndClear("native storage window is no longer open", preservePlacement: Configuration.Instance.ExperimentalMovableStorageWindows);
            return;
        }

        var container = GetContainerRect(_menu);
        if (container == null)
        {
            RestoreAndClear("native storage window container was destroyed", preservePlacement: false);
            return;
        }

        if (container != _trackedContainer)
        {
            ReacquireContainer(container);
            return;
        }

        if (!_positionCaptured)
        {
            CaptureVanillaPlacement(container);
            return;
        }

        var currentCanvasRect = GetValidatedCanvasRect(container);
        if (currentCanvasRect == null || currentCanvasRect != _capturedCanvasRect)
        {
            ReacquireCanvas(currentCanvasRect, container);
            return;
        }

        HandleDrag(container);
    }

    public static void ResetSession(string reason)
    {
        if (_menu != null)
            RestoreAndClear(reason, preservePlacement: false);
        else
            ClearRememberedPlacement();
    }

    public static void LateUpdate()
    {
        if (_menu == null || !_positionCaptured || !Configuration.Instance.ExperimentalMovableStorageWindows)
            return;

        var container = GetContainerRect(_menu);
        if (container == null || container != _trackedContainer)
            return;

        EnforceWorkspacePosition(container, "late layout update");
    }

    private static void ReacquireContainer(RectTransform container)
    {
        var oldContainer = _trackedContainer;
        if (_positionCaptured && oldContainer != null)
        {
            RememberPlacement(oldContainer, _capturedCanvasRect);
            oldContainer.position = _vanillaWorldPosition;
        }

        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] StorageMenu container reference changed; previous='{GetHierarchyPath(oldContainer)}', " +
            $"current='{GetHierarchyPath(container)}'. Discarding old handle and reacquiring placement/bounds.");
        ResetCaptureForContainer(container);
    }

    private static void ReacquireCanvas(RectTransform currentCanvas, RectTransform container)
    {
        var previousCanvas = _capturedCanvasRect;
        if (_positionCaptured && container != null)
        {
            RememberPlacement(container, previousCanvas);
            container.position = _vanillaWorldPosition;
        }

        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] StorageMenu Canvas reference changed or became unavailable; " +
            $"previous='{GetHierarchyPath(previousCanvas)}', current='{GetHierarchyPath(currentCanvas)}', " +
            $"containerParent='{GetHierarchyPath(container?.parent)}'. Restoring native baseline and reacquiring bounds.");
        ResetCaptureForContainer(container);
    }

    private static void ResetCaptureForContainer(RectTransform container)
    {
        DestroyHeaderBar();
        _trackedContainer = container;
        _capturedCanvasRect = null;
        _openedAtFrame = Time.frameCount;
        _stableFrames = 0;
        _lastObservedPosition = container != null ? container.anchoredPosition : Vector2.zero;
        _lastObservedWorldPosition = container != null ? container.position : Vector3.zero;
        _positionCaptured = false;
        _isDragging = false;
        _isLocked = true;
        _hasLastWrittenPosition = false;
        _positionResetLogged = false;
        _workspacePositionCorrectionLogged = false;
        _hierarchyLogged = false;
        _lastBoundsDiagnosticSignature = null;
        _lastBoundsDiagnosticFrame = -1;
        _nativeContentBoundsInContainer = default;
        _nativeContentBoundsInCanvasAtCapture = default;
        _movableBoundsInCanvasAtCapture = default;
        _nativeContentBoundsCaptured = false;
        _capturedActiveSlotCount = 0;
    }

    private static void RememberPlacement(RectTransform container, RectTransform preferredCanvas)
    {
        if (!_positionCaptured || container == null || _activeStorageInstanceId == 0)
            return;

        var entity = _menu?.OpenedStorageEntity;
        var canvas = preferredCanvas != null ? preferredCanvas : GetValidatedCanvasRect(container);
        if (canvas == null || entity == null)
        {
            Logger.Diagnostic("[ExperimentalUIWorkspace] Placement was not retained because the live StorageEntity or Canvas coordinate space was unavailable at close.");
            return;
        }

        var currentCanvas = (Vector2) canvas.InverseTransformPoint(container.position);
        var vanillaCanvas = (Vector2) canvas.InverseTransformPoint(_vanillaWorldPosition);
        var offset = currentCanvas - vanillaCanvas;
        if (!RememberedPlacements.ContainsKey(_activeStorageInstanceId))
            RememberedPlacementOrder.Enqueue(_activeStorageInstanceId);
        RememberedPlacements[_activeStorageInstanceId] = new PlacementMemory
        {
            Entity = entity,
            CanvasOffset = offset
        };

        while (RememberedPlacements.Count > MaximumRememberedStoragePlacements && RememberedPlacementOrder.Count > 0)
        {
            var expiredId = RememberedPlacementOrder.Dequeue();
            if (expiredId == _activeStorageInstanceId)
            {
                RememberedPlacementOrder.Enqueue(expiredId);
                continue;
            }

            RememberedPlacements.Remove(expiredId);
        }

        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] Retained in-session placement for StorageEntity id={_activeStorageInstanceId}; " +
            $"canvasOffset=({offset.x:0.0},{offset.y:0.0}), rememberedCount={RememberedPlacements.Count}.");
    }

    private static void ApplyRememberedPlacement(RectTransform container, RectTransform canvas)
    {
        if (!RememberedPlacements.TryGetValue(_activeStorageInstanceId, out var memory))
            return;

        if (memory?.Entity == null || memory.Entity != _menu?.OpenedStorageEntity)
        {
            RememberedPlacements.Remove(_activeStorageInstanceId);
            Logger.Diagnostic($"[ExperimentalUIWorkspace] Discarded stale placement identity for StorageEntity id={_activeStorageInstanceId}.");
            return;
        }

        var requestedOffset = memory.CanvasOffset;
        var clampedOffset = ClampCanvasOffset(
            canvas,
            requestedOffset,
            out var minAllowed,
            out var maxAllowed,
            out var visibleBounds,
            out var usedFallback);
        var worldOffset = canvas.TransformVector(new Vector3(clampedOffset.x, clampedOffset.y, 0f));
        container.position = _vanillaWorldPosition + worldOffset;
        memory.CanvasOffset = clampedOffset;
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] Restored in-session placement after native open; " +
            $"storageId={_activeStorageInstanceId}, requested=({requestedOffset.x:0.0},{requestedOffset.y:0.0}), " +
            $"applied=({clampedOffset.x:0.0},{clampedOffset.y:0.0}), " +
            $"allowedX=({minAllowed.x:0.0}..{maxAllowed.x:0.0}), allowedY=({minAllowed.y:0.0}..{maxAllowed.y:0.0}), " +
            $"visibleBounds={visibleBounds}, canvasFallback={usedFallback}.");
    }

    private static void ClearRememberedPlacement()
    {
        RememberedPlacements.Clear();
        RememberedPlacementOrder.Clear();
    }
    private static void EnforceWorkspacePosition(RectTransform container, string source)
    {
        if (!_hasLastWrittenPosition || container == null)
            return;

        var targetPosition = _isLocked ? _lockedWorldPosition : _lastWrittenWorldPosition;
        if ((container.position - targetPosition).sqrMagnitude <= 0.0001f)
            return;

        var before = container.position;
        container.position = targetPosition;
        _lastWrittenWorldPosition = container.position;
        _hasLastWrittenPosition = true;
        if (!_workspacePositionCorrectionLogged)
        {
            _workspacePositionCorrectionLogged = true;
            Logger.Diagnostic(
                $"[ExperimentalUIWorkspace] {(_isLocked ? "Locked" : "Placed")} storage position corrected after {source}; " +
                $"before=({before.x:0.000},{before.y:0.000},{before.z:0.000}), " +
                $"target=({targetPosition.x:0.000},{targetPosition.y:0.000},{targetPosition.z:0.000}).");
        }
    }
    private static void CaptureVanillaPlacement(RectTransform container)
    {
        var frameAge = Time.frameCount - _openedAtFrame;
        var current = container.anchoredPosition;
        var currentWorld = container.position;
        var anchoredStable = (current - _lastObservedPosition).sqrMagnitude <= PositionStabilityEpsilon * PositionStabilityEpsilon;
        var worldStable = (currentWorld - _lastObservedWorldPosition).sqrMagnitude <= PositionStabilityEpsilon * PositionStabilityEpsilon;
        if (anchoredStable && worldStable)
            _stableFrames++;
        else
            _stableFrames = 0;

        _lastObservedPosition = current;
        _lastObservedWorldPosition = currentWorld;
        if (frameAge >= MinimumCaptureFrames && _stableFrames >= StableCaptureFrames)
        {
            var canvasSpace = GetValidatedCanvasRect(container);
            var measurementDetails = string.Empty;
            var measured = canvasSpace != null && TryGetNativeContentBounds(
                container,
                canvasSpace,
                out _nativeContentBoundsInContainer,
                out _nativeContentBoundsInCanvasAtCapture,
                out _capturedActiveSlotCount,
                out measurementDetails);
            if (!measured)
            {
                var reason = canvasSpace == null
                    ? $"no valid Canvas RectTransform contains StorageMenu.Container; {DescribeCanvasResolution(container)}"
                    : measurementDetails;
                LogBoundsMeasurementFailure(container, canvasSpace, frameAge, reason);
                if (frameAge <= MaximumCaptureFrames)
                    return;

                Logger.Warning("[ExperimentalUIWorkspace] StorageMenu content bounds remained unavailable after the bounded layout retry; this open remains at vanilla position.");
                ClearTracking();
                return;
            }

            _vanillaPosition = current;
            _vanillaWorldPosition = container.position;
            _positionCaptured = true;
            _capturedCanvasRect = canvasSpace;
            _nativeContentBoundsCaptured = true;
            Logger.Diagnostic($"[ExperimentalUIWorkspace] Captured vanilla StorageMenu position ({current.x:0.0}, {current.y:0.0}) after {frameAge} frames.");
            CreateHeaderBar(container, _nativeContentBoundsInContainer);
            if (TryGetRectBoundsInSpace(_headerRect, canvasSpace, out var headerBoundsInCanvas))
                _movableBoundsInCanvasAtCapture = Union(_nativeContentBoundsInCanvasAtCapture, headerBoundsInCanvas);
            else
                _movableBoundsInCanvasAtCapture = _nativeContentBoundsInCanvasAtCapture;

            ApplyRememberedPlacement(container, canvasSpace);
            _lockedWorldPosition = container.position;
            _lastWrittenWorldPosition = container.position;
            _hasLastWrittenPosition = true;

            Logger.Diagnostic(
                $"[ExperimentalUIWorkspace] Measured native content bounds; activeSlots={_capturedActiveSlotCount}, " +
                $"containerLocal={_nativeContentBoundsInContainer}, canvasLocal={_nativeContentBoundsInCanvasAtCapture}, " +
                $"movableBoundsWithHeader={_movableBoundsInCanvasAtCapture}, " +
                $"containerSpace='{GetHierarchyPath(container)}' canvasSpace='{GetHierarchyPath(canvasSpace)}'; {measurementDetails}.");
            return;
        }

        if (frameAge <= MaximumCaptureFrames)
            return;

        Logger.Warning("[ExperimentalUIWorkspace] Native StorageMenu position did not settle within the bounded capture window; this open remains at vanilla position.");
        ClearTracking();
    }

    private static void HandleDrag(RectTransform container)
    {
        var mouse = (Vector2) Input.mousePosition;
        var canvasSpace = GetValidatedCanvasRect(container);
        LogNativeHierarchyOnce(container);
        DetectPositionReset(container);
        EnforceWorkspacePosition(container, "mod update");
        if (canvasSpace == null)
        {
            if (!_dragPointerConversionFailedLogged)
            {
                _dragPointerConversionFailedLogged = true;
                Logger.Diagnostic($"[ExperimentalUIWorkspace] Drag input unavailable; no validated Canvas RectTransform contains container='{GetHierarchyPath(container)}', parent='{GetHierarchyPath(container.parent)}'.");
            }
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            var insideWindow = ContainsNativeContentPoint(container, canvasSpace, mouse);
            var insideHeader = ContainsScreenPoint(_headerRect, mouse);
            var overLockButton = ContainsScreenPoint(_lockButtonRect, mouse);
            if (insideWindow || insideHeader)
                HandlePointerDown(container, canvasSpace, mouse, insideWindow, insideHeader, overLockButton);
        }

        if (_isDragging && Input.GetMouseButton(0))
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasSpace, mouse, GetEventCamera(), out var currentPointerLocal))
            {
                if (!_dragPointerConversionFailedLogged)
                {
                    _dragPointerConversionFailedLogged = true;
                    Logger.Diagnostic(
                        $"[ExperimentalUIWorkspace] Drag pointer conversion failed; screen=({mouse.x:0.0},{mouse.y:0.0}), " +
                        $"canvas='{DescribeRect(canvasSpace)}', canvasMode={GetCanvas()?.renderMode}, eventCamera='{GetEventCamera()?.name}'.");
                }
                return;
            }

            var pointerDelta = currentPointerLocal - _dragStartPointerLocal;
            var requestedCanvasOffset = _dragStartCanvasOffset + pointerDelta;
            var clampedCanvasOffset = ClampCanvasOffset(
                canvasSpace,
                requestedCanvasOffset,
                out var minAllowedDelta,
                out var maxAllowedDelta,
                out var visibleBounds,
                out var usedCanvasFallback);
            var worldOffset = canvasSpace.TransformVector(new Vector3(clampedCanvasOffset.x, clampedCanvasOffset.y, 0f));
            container.position = _vanillaWorldPosition + worldOffset;
            _lastWrittenWorldPosition = container.position;
            _hasLastWrittenPosition = true;
            var requestedDelta = requestedCanvasOffset - _dragStartCanvasOffset;
            var appliedCanvasPosition = (Vector2) canvasSpace.InverseTransformPoint(container.position);
            var vanillaCanvasPosition = (Vector2) canvasSpace.InverseTransformPoint(_vanillaWorldPosition);
            var appliedCanvasOffset = appliedCanvasPosition - vanillaCanvasPosition;
            var appliedDelta = appliedCanvasOffset - _dragStartCanvasOffset;
            _lastDragPointerDelta = pointerDelta;
            _lastDragScreenDelta = mouse - _dragStartScreenPointer;
            _lastDragAppliedDelta = appliedDelta;
            _lastDragMinAllowedDelta = minAllowedDelta;
            _lastDragMaxAllowedDelta = maxAllowedDelta;
            _lastVisibleBoundsInCanvas = visibleBounds;
            _lastDragClampX = Mathf.Abs(appliedDelta.x - requestedDelta.x) > 0.5f;
            _lastDragClampY = Mathf.Abs(appliedDelta.y - requestedDelta.y) > 0.5f;
            _lastClampUsedCanvasFallback = usedCanvasFallback;
            LogDragMotionSample(
                mouse,
                currentPointerLocal,
                _lastDragScreenDelta,
                pointerDelta,
                requestedCanvasOffset,
                appliedCanvasOffset,
                minAllowedDelta,
                maxAllowedDelta,
                visibleBounds,
                usedCanvasFallback);
        }

        if (_isDragging && Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
            var after = container.anchoredPosition;
            var afterCanvas = (Vector2) canvasSpace.InverseTransformPoint(container.position);
            var vanillaCanvas = (Vector2) canvasSpace.InverseTransformPoint(_vanillaWorldPosition);
            var offsetCanvas = afterCanvas - vanillaCanvas;
            Logger.Diagnostic(
                $"[ExperimentalUIWorkspace] Drag ended; locked={_isLocked}, anchoredBefore=({_dragStartAnchoredPosition.x:0.0},{_dragStartAnchoredPosition.y:0.0}), " +
                $"anchoredAfter=({after.x:0.0},{after.y:0.0}), pointerDelta=({_lastDragPointerDelta.x:0.0},{_lastDragPointerDelta.y:0.0}), " +
                $"screenDelta=({_lastDragScreenDelta.x:0.0},{_lastDragScreenDelta.y:0.0}), " +
                $"appliedDelta=({_lastDragAppliedDelta.x:0.0},{_lastDragAppliedDelta.y:0.0}), " +
                $"canvasOffset=({offsetCanvas.x:0.0},{offsetCanvas.y:0.0}), clampedX={_lastDragClampX},clampedY={_lastDragClampY}, " +
                $"allowedDeltaX=({_lastDragMinAllowedDelta.x:0.0}..{_lastDragMaxAllowedDelta.x:0.0}), " +
                $"allowedDeltaY=({_lastDragMinAllowedDelta.y:0.0}..{_lastDragMaxAllowedDelta.y:0.0}), " +
                $"visibleBoundsCanvas={_lastVisibleBoundsInCanvas}, canvasFallback={_lastClampUsedCanvasFallback}.");
        }
    }
    private static void HandlePointerDown(
        RectTransform container,
        RectTransform canvasSpace,
        Vector2 mouse,
        bool insideWindow,
        bool insideHeader,
        bool overLockButton)
    {
        var insideTitle = ContainsScreenPoint(GetRect(_menu.TitleLabel), mouse);
        var overContextualBackpack = ContextualBackpackPanel.ContainsScreenPoint(mouse);
        var overCloseControl = ContainsScreenPoint(GetRect(_menu.CloseButtonContainer), mouse);
        var itemManagerDragging = IsItemUiManagerDragging();
        var slotDragging = AnyStorageSlotBeingDragged(_menu);
        var reasons = new List<string>();

        if (!insideHeader)
            reasons.Add("outside BAIQ storage header");
        if (overLockButton)
            reasons.Add("over header lock control");
        if (_isLocked)
            reasons.Add("storage header is locked");
        if (overContextualBackpack)
            reasons.Add("over contextual Backpack");
        if (overCloseControl)
            reasons.Add("over native Done/close control");
        if (itemManagerDragging)
            reasons.Add("ItemUIManager reports an item drag");
        if (slotDragging)
            reasons.Add("a native storage slot reports an item drag");
        if (canvasSpace == null)
            reasons.Add("validated Canvas RectTransform is unavailable");

        var pointerLocal = Vector2.zero;
        var pointerConverted = canvasSpace != null
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasSpace, mouse, GetEventCamera(), out pointerLocal);
        if (!pointerConverted)
            reasons.Add("pointer could not be converted to Canvas local coordinates");

        if (reasons.Count > 0)
        {
            Logger.Diagnostic(
                $"[ExperimentalUIWorkspace] Pointer-down rejected; reasons=[{string.Join("; ", reasons)}], " +
                $"insideWindow={insideWindow}, insideTitle={insideTitle}, insideHeader={insideHeader}, overLockButton={overLockButton}, locked={_isLocked}, " +
                $"itemDragging={itemManagerDragging || slotDragging}, anchoredPosition=({container.anchoredPosition.x:0.0},{container.anchoredPosition.y:0.0}), " +
                $"canvas='{DescribeRect(canvasSpace)}', pointer=({mouse.x:0},{mouse.y}).");
            return;
        }

        _isDragging = true;
        _dragStartPointerLocal = pointerLocal;
        _dragStartScreenPointer = mouse;
        _dragStartAnchoredPosition = container.anchoredPosition;
        var currentCanvasPosition = (Vector2) canvasSpace.InverseTransformPoint(container.position);
        var vanillaCanvasPosition = (Vector2) canvasSpace.InverseTransformPoint(_vanillaWorldPosition);
        _dragStartCanvasOffset = currentCanvasPosition - vanillaCanvasPosition;
        _lastWrittenWorldPosition = container.position;
        _hasLastWrittenPosition = true;
        _positionResetLogged = false;
        _dragXMotionLogged = false;
        _dragYMotionLogged = false;
        _dragPointerConversionFailedLogged = false;
        _clampNoFitLogged = false;
        _lastDragClampX = false;
        _lastDragClampY = false;
        _lastClampUsedCanvasFallback = false;
        _lastDragPointerDelta = Vector2.zero;
        _lastDragScreenDelta = Vector2.zero;
        _lastDragAppliedDelta = Vector2.zero;
        _lastDragMinAllowedDelta = Vector2.zero;
        _lastDragMaxAllowedDelta = Vector2.zero;
        _lastVisibleBoundsInCanvas = default;
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] Drag started from unlocked BAIQ storage header; anchoredBefore=({_dragStartAnchoredPosition.x:0.0},{_dragStartAnchoredPosition.y:0.0}), " +
            $"canvasOffsetBefore=({_dragStartCanvasOffset.x:0.0},{_dragStartCanvasOffset.y:0.0}), " +
            $"screenPointer=({mouse.x:0.0},{mouse.y:0.0}), canvasPointerLocal=({pointerLocal.x:0.0},{pointerLocal.y:0.0}), " +
            $"insideTitle={insideTitle}, canvasMode={GetCanvas()?.renderMode}, eventCamera='{GetEventCamera()?.name}', canvas='{GetHierarchyPath(canvasSpace)}'.");
    }

    private static Vector2 ClampCanvasOffset(
        RectTransform canvasSpace,
        Vector2 requestedCanvasOffset,
        out Vector2 minAllowedDelta,
        out Vector2 maxAllowedDelta,
        out Rect visibleBounds,
        out bool usedCanvasFallback)
    {
        minAllowedDelta = Vector2.zero;
        maxAllowedDelta = Vector2.zero;
        visibleBounds = default;
        usedCanvasFallback = false;
        if (!_nativeContentBoundsCaptured || canvasSpace == null)
            return Vector2.zero;

        usedCanvasFallback = !TryGetVisibleBoundsInSpace(canvasSpace, out visibleBounds);
        if (usedCanvasFallback)
            visibleBounds = InsetRect(canvasSpace.rect, ScreenMargin);

        minAllowedDelta = new Vector2(
            visibleBounds.xMin - _movableBoundsInCanvasAtCapture.xMin,
            visibleBounds.yMin - _movableBoundsInCanvasAtCapture.yMin);
        maxAllowedDelta = new Vector2(
            visibleBounds.xMax - _movableBoundsInCanvasAtCapture.xMax,
            visibleBounds.yMax - _movableBoundsInCanvasAtCapture.yMax);

        var fitsX = minAllowedDelta.x <= maxAllowedDelta.x;
        var fitsY = minAllowedDelta.y <= maxAllowedDelta.y;
        var clampedX = fitsX ? Mathf.Clamp(requestedCanvasOffset.x, minAllowedDelta.x, maxAllowedDelta.x) : 0f;
        var clampedY = fitsY ? Mathf.Clamp(requestedCanvasOffset.y, minAllowedDelta.y, maxAllowedDelta.y) : 0f;
        if ((!fitsX || !fitsY) && !_clampNoFitLogged)
        {
            _clampNoFitLogged = true;
            Logger.Diagnostic(
                $"[ExperimentalUIWorkspace] Canvas clamp has no fitting range; fitsX={fitsX},fitsY={fitsY}, " +
                $"allowedX=({minAllowedDelta.x:0.0}..{maxAllowedDelta.x:0.0}), " +
                $"allowedY=({minAllowedDelta.y:0.0}..{maxAllowedDelta.y:0.0}), " +
                $"contentBounds={_movableBoundsInCanvasAtCapture}, visibleBounds={visibleBounds}, " +
                $"canvasRect={canvasSpace.rect}, canvasFallback={usedCanvasFallback}.");
        }
        return new Vector2(clampedX, clampedY);
    }
    private static bool TryGetVisibleBoundsInSpace(RectTransform canvasSpace, out Rect bounds)
    {
        bounds = default;
        if (canvasSpace == null)
            return false;

        var camera = GetEventCamera();
        var viewport = camera != null
            ? camera.pixelRect
            : new Rect(0f, 0f, Screen.width, Screen.height);
        var screenMin = new Vector2(viewport.xMin + ScreenMargin, viewport.yMin + ScreenMargin);
        var screenMax = new Vector2(viewport.xMax - ScreenMargin, viewport.yMax - ScreenMargin);
        if (screenMax.x <= screenMin.x || screenMax.y <= screenMin.y)
            return false;

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var corner = 0; corner < 4; corner++)
        {
            var screenPoint = corner switch
            {
                0 => new Vector2(screenMin.x, screenMin.y),
                1 => new Vector2(screenMin.x, screenMax.y),
                2 => new Vector2(screenMax.x, screenMax.y),
                _ => new Vector2(screenMax.x, screenMin.y)
            };
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasSpace, screenPoint, camera, out var localPoint))
                return false;

            min = Vector2.Min(min, localPoint);
            max = Vector2.Max(max, localPoint);
        }

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return bounds.width > 1f && bounds.height > 1f;
    }

    private static Rect InsetRect(Rect rect, float margin)
    {
        var xMin = rect.xMin + margin;
        var xMax = rect.xMax - margin;
        var yMin = rect.yMin + margin;
        var yMax = rect.yMax - margin;
        return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : rect;
    }

    private static void LogDragMotionSample(
        Vector2 screenPointer,
        Vector2 canvasPointerLocal,
        Vector2 screenDelta,
        Vector2 pointerDelta,
        Vector2 requestedPosition,
        Vector2 appliedPosition,
        Vector2 minAllowedDelta,
        Vector2 maxAllowedDelta,
        Rect visibleBounds,
        bool usedCanvasFallback)
    {
        var logX = Mathf.Abs(screenDelta.x) > 0.5f && !_dragXMotionLogged;
        var logY = Mathf.Abs(screenDelta.y) > 0.5f && !_dragYMotionLogged;
        if (!logX && !logY)
            return;

        _dragXMotionLogged |= logX;
        _dragYMotionLogged |= logY;
        var requestedCanvasOffset = requestedPosition;
        var appliedCanvasOffset = appliedPosition;
        var clampedX = Mathf.Abs(requestedCanvasOffset.x - appliedCanvasOffset.x) > 0.5f;
        var clampedY = Mathf.Abs(requestedCanvasOffset.y - appliedCanvasOffset.y) > 0.5f;
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] Drag motion sample; axes={(logX && logY ? "X,Y" : logX ? "X" : "Y")}, " +
            $"screenPointer=({screenPointer.x:0.0},{screenPointer.y:0.0}), canvasPointerLocal=({canvasPointerLocal.x:0.0},{canvasPointerLocal.y:0.0}), " +
            $"screenDelta=({screenDelta.x:0.0},{screenDelta.y:0.0}), canvasLocalDelta=({pointerDelta.x:0.0},{pointerDelta.y:0.0}), " +
            $"requestedCanvasOffset=({requestedCanvasOffset.x:0.0},{requestedCanvasOffset.y:0.0}), " +
            $"appliedCanvasOffset=({appliedCanvasOffset.x:0.0},{appliedCanvasOffset.y:0.0}), clampedX={clampedX},clampedY={clampedY}, " +
            $"allowedX=({minAllowedDelta.x:0.0}..{maxAllowedDelta.x:0.0}), allowedY=({minAllowedDelta.y:0.0}..{maxAllowedDelta.y:0.0}), " +
            $"visibleBoundsCanvas={visibleBounds}, canvasFallback={usedCanvasFallback}.");
    }

    private static void CreateHeaderBar(RectTransform container, Rect contentBounds)
    {
        DestroyHeaderBar();

        _headerObject = new GameObject("BAIQ_Experimental_StorageWindow_Header");
        _headerObject.AddComponent<RectTransform>();
        _headerObject.AddComponent<Image>();
        var layout = _headerObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        _headerRect = _headerObject.GetComponent<RectTransform>();
        _headerRect.SetParent(container, false);
        _headerRect.anchorMin = new Vector2(0.5f, 0.5f);
        _headerRect.anchorMax = new Vector2(0.5f, 0.5f);
        _headerRect.pivot = new Vector2(0.5f, 0f);
        _headerRect.sizeDelta = new Vector2(Mathf.Max(contentBounds.width, LockButtonSize + 8f), HeaderHeight);
        _headerRect.anchoredPosition = new Vector2(contentBounds.center.x, contentBounds.yMax + HeaderGap);
        _headerObject.transform.SetAsLastSibling();

        var image = _headerObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.12f, 0.16f, 0.92f);
        image.raycastTarget = false;

        CreateLockButton();
        _isLocked = true;
        UpdateLockIcon();
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] Storage header created above measured content; width={_headerRect.rect.width:0.0}, " +
            $"height={_headerRect.rect.height:0.0}, startsLocked={_isLocked}, backgroundRaycast={image.raycastTarget}.");
        LogHeaderRegionBinding(container);
    }

    private static void CreateLockButton()
    {
        _lockButtonObject = new GameObject("LockButton");
        _lockButtonObject.AddComponent<RectTransform>();
        _lockButtonObject.AddComponent<Image>();
        _lockButtonObject.AddComponent<Button>();
        _lockButtonRect = _lockButtonObject.GetComponent<RectTransform>();
        _lockButtonRect.SetParent(_headerRect, false);
        _lockButtonRect.anchorMin = new Vector2(1f, 0.5f);
        _lockButtonRect.anchorMax = new Vector2(1f, 0.5f);
        _lockButtonRect.pivot = new Vector2(1f, 0.5f);
        _lockButtonRect.sizeDelta = new Vector2(LockButtonSize, LockButtonSize);
        _lockButtonRect.anchoredPosition = new Vector2(-2f, 0f);

        var buttonImage = _lockButtonObject.GetComponent<Image>();
        buttonImage.color = new Color(0f, 0f, 0f, 0.14f);
        buttonImage.raycastTarget = true;
        var button = _lockButtonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.AddListener((UnityAction)ToggleLock);

        _lockShackleLeft = CreateLockIconPart("ShackleLeft", _lockButtonRect);
        _lockShackleRight = CreateLockIconPart("ShackleRight", _lockButtonRect);
        _lockShackleTop = CreateLockIconPart("ShackleTop", _lockButtonRect);
        _lockBody = CreateLockIconPart("Body", _lockButtonRect);
    }

    private static RectTransform CreateLockIconPart(string name, RectTransform parent)
    {
        var part = new GameObject(name);
        part.AddComponent<RectTransform>();
        part.AddComponent<Image>();
        var rect = part.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.one;
        var image = part.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
        return rect;
    }

    private static void UpdateLockIcon()
    {
        if (_lockShackleLeft == null || _lockShackleRight == null || _lockShackleTop == null || _lockBody == null)
            return;

        _lockShackleLeft.sizeDelta = new Vector2(2f, 7f);
        _lockShackleLeft.anchoredPosition = new Vector2(-3f, 4.5f);
        _lockShackleRight.sizeDelta = _isLocked ? new Vector2(2f, 7f) : new Vector2(2f, 4f);
        _lockShackleRight.anchoredPosition = _isLocked ? new Vector2(3f, 4.5f) : new Vector2(5f, 7.5f);
        _lockShackleTop.sizeDelta = new Vector2(_isLocked ? 8f : 9f, 2f);
        _lockShackleTop.anchoredPosition = _isLocked ? new Vector2(0f, 8f) : new Vector2(1f, 9f);
        _lockBody.sizeDelta = new Vector2(12f, 8f);
        _lockBody.anchoredPosition = new Vector2(0f, -2.5f);
        _lockBody.SetAsLastSibling();
    }

    private static void ToggleLock()
    {
        if (_menu == null || _headerRect == null)
            return;

        _isLocked = !_isLocked;
        _isDragging = false;
        var container = GetContainerRect(_menu);
        if (_isLocked && container != null)
        {
            _lockedWorldPosition = container.position;
            _lastWrittenWorldPosition = _lockedWorldPosition;
            _hasLastWrittenPosition = true;
            _workspacePositionCorrectionLogged = false;
        }
        UpdateLockIcon();
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] Storage header lock changed; locked={_isLocked}, " +
            $"movementEnabled={!_isLocked}, containerWorldPosition=({container?.position.x:0.000},{container?.position.y:0.000},{container?.position.z:0.000}), " +
            $"containerAnchoredPosition=({container?.anchoredPosition.x:0.0},{container?.anchoredPosition.y:0.0}).");
    }

    private static void LogHeaderRegionBinding(RectTransform container)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        var title = GetRect(_menu.TitleLabel);
        var canvas = GetCanvas();
        var titleGraphic = GetGraphic(_menu.TitleLabel);
        var headerImage = _headerObject?.GetComponent<Image>();
        var lockButtonImage = _lockButtonObject?.GetComponent<Image>();
        var scaler = canvas?.GetComponent<UnityEngine.UI.CanvasScaler>();
        var doneButton = GetCloseButtonTransform();
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] Storage header bound; header='{GetHierarchyPath(_headerRect)}', " +
            $"headerBounds={GetScreenBounds(_headerRect)}, headerSize={_headerRect.rect.size}, headerRaycast={headerImage?.raycastTarget}, " +
            $"lock='{GetHierarchyPath(_lockButtonRect)}', lockBounds={GetScreenBounds(_lockButtonRect)}, " +
            $"lockButtonRaycast={lockButtonImage?.raycastTarget}, lockButtonEnabled={_lockButtonObject?.GetComponent<Button>()?.enabled}, locked={_isLocked}, " +
            $"container='{GetHierarchyPath(container)}', containerScreenBounds={GetScreenBounds(container)}, " +
            $"contentContainerLocal={_nativeContentBoundsInContainer}, " +
            $"contentScreenBounds={GetScreenBounds(GetValidatedCanvasRect(container), _nativeContentBoundsInCanvasAtCapture)}, " +
            $"title='{GetHierarchyPath(title)}', titleScreenBounds={GetScreenBounds(title)}, titleRaycastTarget={titleGraphic?.raycastTarget}, " +
            $"slotContainer='{GetHierarchyPath(GetRect(_menu.SlotContainer))}', " +
            $"doneButton='{GetHierarchyPath(doneButton)}', doneButtonBounds={GetScreenBounds(doneButton as RectTransform)}, " +
            $"doneContainer='{GetHierarchyPath(GetRect(_menu.CloseButtonContainer))}', " +
            $"canvas='{GetHierarchyPath(canvas?.transform)}', canvasMode={canvas?.renderMode}, " +
            $"worldCamera='{canvas?.worldCamera?.name}', graphicRaycaster={canvas?.GetComponent<GraphicRaycaster>() != null}; " +
            $"screen=({Screen.width}x{Screen.height}),cameraPixelRect={GetEventCamera()?.pixelRect}, " +
            $"canvasScaleFactor={canvas?.scaleFactor},scalerMode={scaler?.uiScaleMode},referenceResolution={scaler?.referenceResolution}; " +
            $"ancestorMasks={DescribeAncestorMasks(container)}; " +
            "header drag uses UnityEngine.Input; lock toggling uses the lock button click.");
    }

    private static void LogNativeHierarchyOnce(RectTransform container)
    {
        if (_hierarchyLogged || !Logger.DiagnosticLoggingEnabled)
            return;

        _hierarchyLogged = true;
        var title = GetRect(_menu.TitleLabel);
        var slots = GetRect(_menu.SlotContainer);
        var done = GetRect(_menu.CloseButtonContainer);
        var sort = StorageSortButton.ExperimentalDebugTransform;
        var closeButton = GetCloseButtonTransform();
        var sortRect = sort as RectTransform;
        var doneRect = done as RectTransform;
        var doneGraphic = GetGraphic(closeButton?.gameObject);
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] StorageMenu hierarchy; container='{GetHierarchyPath(container)}', " +
            $"containerRect={container.rect}, anchors=({container.anchorMin}->{container.anchorMax}), pivot={container.pivot}, " +
            $"titleInside={IsDescendantOf(title, container)}, slotContainerInside={IsDescendantOf(slots, container)}, " +
            $"sort='{GetHierarchyPath(sort)}', sortInside={IsDescendantOf(sort, container)}, sortBounds={GetScreenBounds(sortRect)}, " +
            $"doneContainer='{GetHierarchyPath(done)}', closeButton='{GetHierarchyPath(closeButton)}', " +
            $"doneInside={IsDescendantOf(done, container)}, doneBounds={GetScreenBounds(doneRect)}, doneRaycastTarget={doneGraphic?.raycastTarget}; " +
            $"titleBounds={GetScreenBounds(title)}, slotBounds={GetScreenBounds(slots)}, " +
            $"activeSlots={_capturedActiveSlotCount}, nativeContent={_nativeContentBoundsInContainer}, " +
            $"headerBoundsInCanvas={_movableBoundsInCanvasAtCapture}.");
    }

    private static void DetectPositionReset(RectTransform container)
    {
        if (!_hasLastWrittenPosition)
            return;

        var actual = container.position;
        if ((actual - _lastWrittenWorldPosition).sqrMagnitude <= 0.0001f)
            return;

        if (!_positionResetLogged)
        {
            _positionResetLogged = true;
            Logger.Diagnostic(
                $"[ExperimentalUIWorkspace] Position reset detected; expectedWorld=({_lastWrittenWorldPosition.x:0.000},{_lastWrittenWorldPosition.y:0.000},{_lastWrittenWorldPosition.z:0.000}), " +
                $"observedWorld=({actual.x:0.000},{actual.y:0.000},{actual.z:0.000}), dragging={_isDragging}.");
        }
    }

    private static void DestroyHeaderBar()
    {
        if (_headerObject != null)
            UnityEngine.Object.Destroy(_headerObject);

        _headerObject = null;
        _headerRect = null;
        _lockButtonObject = null;
        _lockButtonRect = null;
        _lockShackleLeft = null;
        _lockShackleRight = null;
        _lockShackleTop = null;
        _lockBody = null;
    }

    private static bool ContainsScreenPoint(RectTransform rect, Vector2 screenPoint)
    {
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, GetEventCamera());
    }

    private static bool ContainsNativeContentPoint(RectTransform container, RectTransform canvasSpace, Vector2 screenPoint)
    {
        if (container == null || canvasSpace == null || !_nativeContentBoundsCaptured
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasSpace, screenPoint, GetEventCamera(), out var canvasPoint))
        {
            return false;
        }

        var currentBounds = _nativeContentBoundsInCanvasAtCapture;
        var currentCanvasPosition = (Vector2) canvasSpace.InverseTransformPoint(container.position);
        var vanillaCanvasPosition = (Vector2) canvasSpace.InverseTransformPoint(_vanillaWorldPosition);
        currentBounds.position += currentCanvasPosition - vanillaCanvasPosition;
        return currentBounds.Contains(canvasPoint);
    }

    private static bool TryGetNativeContentBounds(
        RectTransform container,
        RectTransform canvasSpace,
        out Rect boundsInContainer,
        out Rect boundsInCanvas,
        out int activeSlotCount,
        out string diagnostic)
    {
        boundsInContainer = default;
        boundsInCanvas = default;
        activeSlotCount = 0;
        var hasBounds = false;
        var details = new List<string>();
        var slotsMeasured = StorageSortButton.TryMeasureActiveStorageSlotBounds(
            _menu,
            container,
            canvasSpace,
            out var slotBoundsInContainer,
            out var slotBoundsInCanvas,
            out _,
            out activeSlotCount,
            out var slotMetrics);
        details.Add($"slotsMeasured={slotsMeasured} ({slotMetrics})");
        if (slotsMeasured)
        {
            boundsInContainer = slotBoundsInContainer;
            boundsInCanvas = slotBoundsInCanvas;
            hasBounds = true;
        }

        var titleRect = GetRect(_menu.TitleLabel);
        var titleIncluded = false;
        details.Add($"titleExcludedFromMovementBounds ({DescribeRect(titleRect)}); title text remains part of the native container");

        var sortRect = StorageSortButton.ExperimentalDebugTransform as RectTransform;
        var sortIsVisible = sortRect != null && sortRect.gameObject.activeInHierarchy;
        var sortIncluded = TryIncludeRect("Sort", sortRect, container, canvasSpace, ref boundsInContainer, ref boundsInCanvas, ref hasBounds, details);
        var doneButton = GetCloseButtonTransform();
        var doneButtonRect = doneButton as RectTransform;
        var doneIncluded = TryIncludeRect("DoneButton", doneButtonRect, container, canvasSpace, ref boundsInContainer, ref boundsInCanvas, ref hasBounds, details);

        var validUnion = hasBounds && boundsInContainer.width > 1f && boundsInContainer.height > 1f
            && boundsInCanvas.width > 1f && boundsInCanvas.height > 1f;
        diagnostic =
            $"coordinateSpaces container='{DescribeRect(container)}'; canvas='{DescribeRect(canvasSpace)}'; " +
            $"{string.Join("; ", details)}; titleIncluded={titleIncluded}, " +
            $"sortVisible={sortIsVisible}, sortIncluded={sortIncluded}, doneIncluded={doneIncluded}, unionValid={validUnion}, " +
            $"unionContainer={boundsInContainer}, unionCanvas={boundsInCanvas}";

        // Native title RectTransforms can stretch across the full screen. They
        // remain attached to the native container but do not size the compact
        // draggable header or expand the movement clamp.
        return slotsMeasured && validUnion && (!sortIsVisible || sortIncluded) && doneIncluded;
    }

    private static bool TryIncludeRect(
        string label,
        RectTransform rect,
        RectTransform container,
        RectTransform canvasSpace,
        ref Rect boundsInContainer,
        ref Rect boundsInCanvas,
        ref bool hasBounds,
        List<string> details)
    {
        if (rect == null)
        {
            details.Add($"{label}=missing");
            return false;
        }

        if (!rect.gameObject.activeInHierarchy)
        {
            details.Add($"{label}=inactive ({DescribeRect(rect)})");
            return false;
        }

        var localRect = rect.rect;
        if (localRect.width <= 0.5f || localRect.height <= 0.5f)
        {
            details.Add($"{label}=zero-size ({DescribeRect(rect)})");
            return false;
        }

        if (!TryTransformRectBounds(rect, localRect, container, out var rectInContainer)
            || !TryTransformRectBounds(rect, localRect, canvasSpace, out var rectInCanvas))
        {
            details.Add($"{label}=transform-invalid ({DescribeRect(rect)}; containerSpace='{DescribeRect(container)}'; canvasSpace='{DescribeRect(canvasSpace)}')");
            return false;
        }

        boundsInContainer = hasBounds ? Union(boundsInContainer, rectInContainer) : rectInContainer;
        boundsInCanvas = hasBounds ? Union(boundsInCanvas, rectInCanvas) : rectInCanvas;
        hasBounds = true;
        details.Add($"{label}=measured ({DescribeRect(rect)}; containerLocal={rectInContainer}; canvasLocal={rectInCanvas})");
        return true;
    }

    private static bool TryTransformRectBounds(RectTransform rectTransform, Rect localRect, RectTransform coordinateSpace, out Rect bounds)
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
            var worldPoint = rectTransform.TransformPoint(localPoint);
            var point = coordinateSpace.InverseTransformPoint(worldPoint);
            min = Vector2.Min(min, new Vector2(point.x, point.y));
            max = Vector2.Max(max, new Vector2(point.x, point.y));
        }

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return bounds.width > 0.5f && bounds.height > 0.5f
            && !float.IsNaN(bounds.xMin) && !float.IsNaN(bounds.yMin)
            && !float.IsNaN(bounds.xMax) && !float.IsNaN(bounds.yMax);
    }

    private static void LogBoundsMeasurementFailure(RectTransform container, RectTransform canvasSpace, int frameAge, string details)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        var signature = details;
        if (_lastBoundsDiagnosticSignature == signature
            || frameAge - _lastBoundsDiagnosticFrame < BoundsDiagnosticIntervalFrames)
            return;

        _lastBoundsDiagnosticSignature = signature;
        _lastBoundsDiagnosticFrame = frameAge;
        Logger.Diagnostic(
            $"[ExperimentalUIWorkspace] StorageMenu content bounds pending after {frameAge} frames; " +
            $"container='{DescribeRect(container)}'; canvas='{DescribeRect(canvasSpace)}'; {details}.");
    }

    private static string DescribeRect(RectTransform rect)
    {
        if (rect == null)
            return "(missing RectTransform)";

        return $"'{GetHierarchyPath(rect)}',activeSelf={rect.gameObject.activeSelf},activeInHierarchy={rect.gameObject.activeInHierarchy}, " +
            $"rect={rect.rect},size={rect.rect.size},anchors=({rect.anchorMin}->{rect.anchorMax}),pivot={rect.pivot}";
    }

    private static bool TryGetRectBoundsInSpace(RectTransform rect, RectTransform coordinateSpace, out Rect bounds)
    {
        bounds = default;
        return rect != null && TryTransformRectBounds(rect, rect.rect, coordinateSpace, out bounds);
    }

    private static Rect Union(Rect first, Rect second)
    {
        return Rect.MinMaxRect(
            Mathf.Min(first.xMin, second.xMin),
            Mathf.Min(first.yMin, second.yMin),
            Mathf.Max(first.xMax, second.xMax),
            Mathf.Max(first.yMax, second.yMax));
    }

    private static RectTransform GetRect(object uiObject)
    {
        var transform = uiObject switch
        {
            Graphic graphic => graphic.rectTransform,
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null
        };
        return transform as RectTransform;
    }

    private static Graphic GetGraphic(object uiObject)
    {
        return uiObject switch
        {
            Graphic graphic => graphic,
            GameObject gameObject => gameObject.GetComponent<Graphic>(),
            Component component => component.GetComponent<Graphic>(),
            _ => null
        };
    }

    private static Transform GetCloseButtonTransform()
    {
        return _menu?.CloseButtonContainer?.GetComponentInChildren<Button>(true)?.transform;
    }

    private static string DescribeAncestorMasks(Transform start)
    {
        var masks = new List<string>();
        for (var current = start; current != null; current = current.parent)
        {
            if (current.GetComponent<Mask>() != null || current.GetComponent<RectMask2D>() != null)
                masks.Add(GetHierarchyPath(current));
        }

        return masks.Count == 0 ? "none" : string.Join("|", masks);
    }

    private static Canvas GetCanvas()
    {
        var container = GetContainerRect(_menu);
        if (container == null)
            return null;

        // The native StorageMenu.Canvas field can keep pointing at the Canvas
        // used before StorageMenu was reparented. Resolve the live hierarchy
        // first; only accept cached menu references when they contain the
        // current native container.
        var hierarchyCanvas = container.GetComponentInParent<Canvas>();
        if (CanvasContainsContainer(hierarchyCanvas, container))
            return hierarchyCanvas;

        var menuHierarchyCanvas = _menu?.GetComponentInParent<Canvas>();
        if (CanvasContainsContainer(menuHierarchyCanvas, container))
            return menuHierarchyCanvas;

        var menuCanvas = _menu?.Canvas;
        return CanvasContainsContainer(menuCanvas, container) ? menuCanvas : null;
    }

    private static string DescribeCanvasResolution(RectTransform container)
    {
        var hierarchyCanvas = container?.GetComponentInParent<Canvas>();
        var menuHierarchyCanvas = _menu?.GetComponentInParent<Canvas>();
        var menuCanvas = _menu?.Canvas;
        return $"containerParent='{GetHierarchyPath(container?.parent)}', " +
            $"containerCanvas='{GetHierarchyPath(hierarchyCanvas?.transform)}' (contains={CanvasContainsContainer(hierarchyCanvas, container)}), " +
            $"menuCanvas='{GetHierarchyPath(menuHierarchyCanvas?.transform)}' (contains={CanvasContainsContainer(menuHierarchyCanvas, container)}), " +
            $"declaredCanvas='{GetHierarchyPath(menuCanvas?.transform)}' (contains={CanvasContainsContainer(menuCanvas, container)}), " +
            $"menu='{GetHierarchyPath(_menu?.transform)}', container='{DescribeRect(container)}'.";
    }

    private static bool CanvasContainsContainer(Canvas canvas, RectTransform container)
    {
        return canvas != null
            && canvas.transform is RectTransform
            && container != null
            && IsDescendantOf(container, canvas.transform);
    }

    private static RectTransform GetValidatedCanvasRect(RectTransform container)
    {
        var canvas = GetCanvas();
        var canvasRect = canvas?.transform as RectTransform;
        if (container == null || canvasRect == null
            || !container.gameObject.activeInHierarchy
            || !canvasRect.gameObject.activeInHierarchy
            || !IsDescendantOf(container, canvasRect))
        {
            return null;
        }

        return canvasRect;
    }

    private static Camera GetEventCamera()
    {
        var canvas = GetCanvas();
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return "(missing)";

        var names = new List<string>();
        for (var current = transform; current != null; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static string GetScreenBounds(RectTransform rect)
    {
        if (rect == null)
            return "(missing)";

        var localRect = rect.rect;
        var localCorners = new[]
        {
            new Vector3(localRect.xMin, localRect.yMin, 0f),
            new Vector3(localRect.xMin, localRect.yMax, 0f),
            new Vector3(localRect.xMax, localRect.yMax, 0f),
            new Vector3(localRect.xMax, localRect.yMin, 0f)
        };
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        for (var i = 0; i < localCorners.Length; i++)
        {
            var world = rect.TransformPoint(localCorners[i]);
            var screen = RectTransformUtility.WorldToScreenPoint(GetEventCamera(), world);
            minX = Mathf.Min(minX, screen.x);
            minY = Mathf.Min(minY, screen.y);
            maxX = Mathf.Max(maxX, screen.x);
            maxY = Mathf.Max(maxY, screen.y);
        }

        return $"({minX:0},{minY:0})-({maxX:0},{maxY:0})";
    }

    private static string GetScreenBounds(RectTransform coordinateSpace, Rect bounds)
    {
        if (coordinateSpace == null)
            return "(missing coordinate space)";

        var localCorners = new[]
        {
            new Vector3(bounds.xMin, bounds.yMin, 0f),
            new Vector3(bounds.xMin, bounds.yMax, 0f),
            new Vector3(bounds.xMax, bounds.yMax, 0f),
            new Vector3(bounds.xMax, bounds.yMin, 0f)
        };
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        for (var i = 0; i < localCorners.Length; i++)
        {
            var world = coordinateSpace.TransformPoint(localCorners[i]);
            var screen = RectTransformUtility.WorldToScreenPoint(GetEventCamera(), world);
            minX = Mathf.Min(minX, screen.x);
            minY = Mathf.Min(minY, screen.y);
            maxX = Mathf.Max(maxX, screen.x);
            maxY = Mathf.Max(maxY, screen.y);
        }

        return $"({minX:0},{minY:0})-({maxX:0},{maxY:0})";
    }

    private static bool IsDescendantOf(Transform candidate, Transform ancestor)
    {
        for (var current = candidate; current != null; current = current.parent)
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    private static bool IsDescendantOf(RectTransform candidate, RectTransform ancestor)
    {
        return IsDescendantOf(candidate as Transform, ancestor as Transform);
    }

    private static bool IsItemUiManagerDragging()
    {
        var manager = Singleton<ItemUIManager>.Instance;
        return manager != null && manager.IsCurrentlyDragging;
    }

    private static bool AnyStorageSlotBeingDragged(StorageMenu menu)
    {
        if (menu?.SlotsUIs == null)
            return false;

        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            if (menu.SlotsUIs[i] != null && menu.SlotsUIs[i].IsBeingDragged)
                return true;
        }

        return false;
    }

    private static RectTransform GetContainerRect(StorageMenu menu)
    {
        return menu?.Container?.transform as RectTransform;
    }

    private static void RestoreAndClear(string reason, bool preservePlacement)
    {
        var container = _trackedContainer != null ? _trackedContainer : GetContainerRect(_menu);
        if (preservePlacement)
            RememberPlacement(container, _capturedCanvasRect);
        else
            ClearRememberedPlacement();

        var before = container != null ? container.anchoredPosition : Vector2.zero;
        var beforeWorld = container != null ? container.position : Vector3.zero;
        if (_positionCaptured && container != null && (beforeWorld - _vanillaWorldPosition).sqrMagnitude > 0.0001f)
        {
            container.position = _vanillaWorldPosition;
            var after = container.anchoredPosition;
            Logger.Diagnostic(
                $"[ExperimentalUIWorkspace] Restored vanilla StorageMenu position; anchoredBefore=({before.x:0.0},{before.y:0.0}), " +
                $"anchoredAfter=({after.x:0.0},{after.y:0.0}), " +
                $"worldBefore=({beforeWorld.x:0.000},{beforeWorld.y:0.000},{beforeWorld.z:0.000}), " +
                $"worldAfter=({_vanillaWorldPosition.x:0.000},{_vanillaWorldPosition.y:0.000},{_vanillaWorldPosition.z:0.000}) ({reason}).");
        }

        ClearTracking();
    }

    private static void ClearTracking()
    {
        DestroyHeaderBar();
        _menu = null;
        _trackedContainer = null;
        _capturedCanvasRect = null;
        _activeStorageInstanceId = 0;
        _openedAtFrame = 0;
        _stableFrames = 0;
        _lastObservedPosition = Vector2.zero;
        _lastObservedWorldPosition = Vector3.zero;
        _vanillaPosition = Vector2.zero;
        _vanillaWorldPosition = Vector3.zero;
        _lockedWorldPosition = Vector3.zero;
        _positionCaptured = false;
        _isDragging = false;
        _isLocked = true;
        _hierarchyLogged = false;
        _hasLastWrittenPosition = false;
        _positionResetLogged = false;
        _workspacePositionCorrectionLogged = false;
        _lastBoundsDiagnosticSignature = null;
        _lastBoundsDiagnosticFrame = -1;
        _dragStartPointerLocal = Vector2.zero;
        _dragStartScreenPointer = Vector2.zero;
        _dragStartAnchoredPosition = Vector2.zero;
        _dragStartCanvasOffset = Vector2.zero;
        _lastWrittenWorldPosition = Vector3.zero;
        _nativeContentBoundsInContainer = default;
        _nativeContentBoundsInCanvasAtCapture = default;
        _movableBoundsInCanvasAtCapture = default;
        _nativeContentBoundsCaptured = false;
        _capturedActiveSlotCount = 0;
        _dragXMotionLogged = false;
        _dragYMotionLogged = false;
        _dragPointerConversionFailedLogged = false;
        _clampNoFitLogged = false;
        _lastDragClampX = false;
        _lastDragClampY = false;
        _lastClampUsedCanvasFallback = false;
        _lastDragPointerDelta = Vector2.zero;
        _lastDragScreenDelta = Vector2.zero;
        _lastDragAppliedDelta = Vector2.zero;
        _lastDragMinAllowedDelta = Vector2.zero;
        _lastDragMaxAllowedDelta = Vector2.zero;
        _lastVisibleBoundsInCanvas = default;
    }
}
