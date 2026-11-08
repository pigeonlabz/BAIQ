using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppScheduleOne.UI.Items;
#elif MONO
using ScheduleOne.UI.Items;
#endif

namespace Backpack.UI;

public static class BackpackItemInfoPlacement
{
    private const float EdgeMargin = 12f;
    private const float SlotGap = 8f;
    private static readonly Vector3[] SourceCorners = new Vector3[4];
    private static readonly Vector3[] PanelCorners = new Vector3[4];
    private static readonly Vector3[] CanvasCorners = new Vector3[4];

    public static void KeepOnScreen(ItemInfoPanel panel, RectTransform source)
    {
        if (panel?.Container == null || source == null || !IsBackpackSource(source))
            return;

        var parent = panel.Container.parent?.GetComponent<RectTransform>();
        if (parent == null)
            return;

        try
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel.Container);
            source.GetWorldCorners(SourceCorners);
            panel.Container.GetWorldCorners(PanelCorners);

            GetBoundsInParent(SourceCorners, parent, out var sourceMin, out var sourceMax);
            GetBoundsInParent(PanelCorners, parent, out var panelMin, out var panelMax);

            var viewport = panel.Canvas?.transform.GetComponent<RectTransform>() ?? parent;
            viewport.GetWorldCorners(CanvasCorners);
            GetBoundsInParent(CanvasCorners, parent, out var viewportMin, out var viewportMax);
            var safeMin = viewportMin + new Vector2(EdgeMargin, EdgeMargin);
            var safeMax = viewportMax - new Vector2(EdgeMargin, EdgeMargin);
            var panelSize = panelMax - panelMin;
            var panelCenter = (panelMin + panelMax) * 0.5f;
            var sourceCenter = (sourceMin + sourceMax) * 0.5f;
            var placedRight = panelCenter.x >= sourceCenter.x;

            if (panelMin.x < safeMin.x || panelMax.x > safeMax.x)
            {
                panelCenter.x = placedRight
                    ? sourceMin.x - SlotGap - panelSize.x * 0.5f
                    : sourceMax.x + SlotGap + panelSize.x * 0.5f;
            }

            var half = panelSize * 0.5f;
            panelCenter.x = Mathf.Clamp(panelCenter.x, safeMin.x + half.x, safeMax.x - half.x);
            panelCenter.y = Mathf.Clamp(panelCenter.y, safeMin.y + half.y, safeMax.y - half.y);

            var currentCenter = (panelMin + panelMax) * 0.5f;
            var local = panel.Container.localPosition;
            local.x += panelCenter.x - currentCenter.x;
            local.y += panelCenter.y - currentCenter.y;
            panel.Container.localPosition = local;
        }
        catch (Exception exception)
        {
            Logger.Warning($"Backpack item information placement was skipped safely: {exception.Message}");
        }
    }

    private static bool IsBackpackSource(RectTransform source)
    {
        return ResponsiveBackpackUI.ContainsSlotRect(source)
               || ContextualBackpackPanel.ContainsSlotRect(source)
               || PhoneBackpackUI.ContainsSlotRect(source);
    }

    private static void GetBoundsInParent(Vector3[] corners, RectTransform parent, out Vector2 minimum, out Vector2 maximum)
    {
        minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < corners.Length; i++)
        {
            var local = parent.InverseTransformPoint(corners[i]);
            minimum.x = Mathf.Min(minimum.x, local.x);
            minimum.y = Mathf.Min(minimum.y, local.y);
            maximum.x = Mathf.Max(maximum.x, local.x);
            maximum.y = Mathf.Max(maximum.y, local.y);
        }
    }
}
