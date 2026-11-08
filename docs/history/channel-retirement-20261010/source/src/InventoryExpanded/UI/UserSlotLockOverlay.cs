using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.Money;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
#endif

namespace Backpack.UI;

public static class UserSlotLockOverlay
{
    private static readonly Dictionary<int, Binding> Bindings = new();
    private static int _transientRefreshLogsRemaining = 3;

    private sealed class Binding
    {
        public ItemSlotUI SlotUi { get; init; }
        public GameObject Root { get; init; }
        public Image Background { get; init; }
        public Image Body { get; init; }
        public Image LeftShackle { get; init; }
        public Image RightShackleImage { get; init; }
        public Image TopShackle { get; init; }
        public RectTransform RightShackle { get; init; }
        public Button Button { get; init; }
        public bool Locked { get; set; }
    }

    public static void Refresh(ItemSlotUI slotUi)
    {
        if (slotUi == null)
            return;

        try
        {
            var slot = slotUi.assignedSlot;
            if (slot == null || AsCashSlot(slot) != null || !UserSlotLockRegistry.TryResolve(slot, out var identity))
            {
                HideExisting(slotUi);
                return;
            }

            var binding = GetOrCreate(slotUi);
            if (binding == null)
                return;

            binding.Locked = UserSlotLockRegistry.IsUserLocked(identity);
            ApplyVisualState(binding);
        }
        catch (Exception exception)
        {
            HandleTransientRefreshFailure(slotUi, exception);
        }
    }

    public static void RefreshSafely(ItemSlotUI slotUi)
    {
        try
        {
            Refresh(slotUi);
        }
        catch (Exception exception)
        {
            HandleTransientRefreshFailure(slotUi, exception);
        }
    }

    private static void ApplyVisualState(Binding binding)
    {
        var itemUiManager = Singleton<ItemUIManager>.Instance;
        if (itemUiManager?.QuickMoveEnabled != true)
        {
            binding.Root.SetActive(false);
            return;
        }

        var locked = binding.Locked;
        binding.Root.name = locked ? "OGBackpack_UnlockSlot" : "OGBackpack_LockSlot";
        binding.Root.SetActive(true);
        binding.Root.transform.SetAsLastSibling();
        binding.Button.interactable = true;
        binding.Background.raycastTarget = true;

        var accent = locked
            ? new Color(0.91f, 0.69f, 0.25f, 0.82f)
            : new Color(0.82f, 0.84f, 0.86f, 0.82f);
        binding.Background.color = new Color(0.055f, 0.06f, 0.065f, 0.58f);
        binding.Body.color = accent;
        binding.LeftShackle.color = accent;
        binding.RightShackleImage.color = accent;
        binding.TopShackle.color = accent;
        binding.RightShackle.anchoredPosition = locked ? new Vector2(2.75f, 2.75f) : new Vector2(4.25f, 4f);
    }

    public static void RefreshAll()
    {
        var stale = new List<int>();
        foreach (var pair in Bindings)
        {
            if (pair.Value.SlotUi == null || pair.Value.Root == null)
            {
                stale.Add(pair.Key);
                continue;
            }

            Refresh(pair.Value.SlotUi);
        }

        for (var i = 0; i < stale.Count; i++)
            Bindings.Remove(stale[i]);
    }

    public static void Forget(ItemSlotUI slotUi)
    {
        if (slotUi != null)
            Bindings.Remove(slotUi.GetInstanceID());
    }

    public static void ResetSession()
    {
        foreach (var binding in Bindings.Values)
        {
            if (binding.Root != null)
                UnityEngine.Object.Destroy(binding.Root);
        }

        Bindings.Clear();
    }

    private static Binding GetOrCreate(ItemSlotUI slotUi)
    {
        var id = slotUi.GetInstanceID();
        if (Bindings.TryGetValue(id, out var existing))
        {
            if (existing.SlotUi == slotUi && existing.Root != null)
                return existing;

            if (existing.Root != null)
                UnityEngine.Object.Destroy(existing.Root);
            Bindings.Remove(id);
        }

        if (slotUi.Rect == null)
            return null;

        var root = new GameObject("OGBackpack_LockSlot");
        root.AddComponent<RectTransform>();
        root.AddComponent<Image>();
        root.AddComponent<Button>();
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(slotUi.Rect, false);
        rootRect.anchorMin = new Vector2(0f, 0f);
        rootRect.anchorMax = new Vector2(0f, 0f);
        rootRect.pivot = new Vector2(0f, 0f);
        rootRect.anchoredPosition = new Vector2(3f, 3f);
        rootRect.sizeDelta = new Vector2(16f, 16f);

        var background = root.GetComponent<Image>();
        background.color = new Color(0.07f, 0.07f, 0.08f, 0.52f);
        if (slotUi.Background?.sprite != null)
        {
            background.sprite = slotUi.Background.sprite;
            background.type = Image.Type.Sliced;
        }

        var button = root.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        button.colors = colors;
        Action toggle = () => Toggle(slotUi);
        button.onClick.AddListener(toggle);

        var sprite = slotUi.Background?.sprite;
        var body = CreatePart("Body", rootRect, new Vector2(9f, 6.5f), new Vector2(0f, -2.5f), sprite);
        var leftShackle = CreatePart("ShackleLeft", rootRect, new Vector2(1.75f, 5f), new Vector2(-2.75f, 2.75f), sprite);
        var rightShackle = CreatePart("ShackleRight", rootRect, new Vector2(1.75f, 5f), new Vector2(2.75f, 2.75f), sprite);
        var topShackle = CreatePart("ShackleTop", rootRect, new Vector2(7.25f, 1.75f), new Vector2(0f, 5.1f), sprite);

        var binding = new Binding
        {
            SlotUi = slotUi,
            Root = root,
            Background = background,
            Body = body.GetComponent<Image>(),
            LeftShackle = leftShackle.GetComponent<Image>(),
            RightShackleImage = rightShackle.GetComponent<Image>(),
            TopShackle = topShackle.GetComponent<Image>(),
            RightShackle = rightShackle,
            Button = button
        };
        Bindings[id] = binding;
        return binding;
    }

    private static RectTransform CreatePart(
        string name,
        RectTransform parent,
        Vector2 size,
        Vector2 position,
        Sprite sprite)
    {
        var part = new GameObject(name);
        part.AddComponent<RectTransform>();
        part.AddComponent<Image>();
        var rect = part.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var image = part.GetComponent<Image>();
        image.color = new Color(0.72f, 0.75f, 0.78f, 0.72f);
        image.raycastTarget = false;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Simple;
        }
        return rect;
    }

    private static void HideExisting(ItemSlotUI slotUi)
    {
        try
        {
            if (slotUi != null && Bindings.TryGetValue(slotUi.GetInstanceID(), out var binding) && binding.Root != null)
                binding.Root.SetActive(false);
        }
        catch
        {
            // Destroyed and transient UI wrappers have no lock overlay to maintain.
        }
    }

    private static void HandleTransientRefreshFailure(ItemSlotUI slotUi, Exception exception)
    {
        HideExisting(slotUi);

        if (_transientRefreshLogsRemaining <= 0)
            return;

        _transientRefreshLogsRemaining--;
        Logger.Warning(
            $"Skipped transient slot-lock overlay refresh: {exception.GetType().Name}: {exception.Message}");
    }

    private static CashSlot AsCashSlot(
#if IL2CPP
        Il2CppScheduleOne.ItemFramework.ItemSlot slot
#elif MONO
        ScheduleOne.ItemFramework.ItemSlot slot
#endif
    )
    {
        if (slot == null)
            return null;

#if IL2CPP
        try
        {
            return slot.TryCast<CashSlot>();
        }
        catch
        {
            return null;
        }
#elif MONO
        return slot as CashSlot;
#endif
    }

    private static void Toggle(ItemSlotUI slotUi)
    {
        try
        {
            var slot = slotUi?.assignedSlot;
            if (slot == null || !UserSlotLockRegistry.Toggle(slot, out var locked))
                return;

            Logger.Diagnostic(() => $"User {GetAction(locked)} {Describe(slot)}.");
            var id = slotUi.GetInstanceID();
            if (Bindings.TryGetValue(id, out var binding) && binding.Root != null)
            {
                binding.Locked = locked;
                ApplyVisualState(binding);
            }
            else
            {
                Refresh(slotUi);
            }
        }
        catch
        {
            HideExisting(slotUi);
        }
    }

    private static string GetAction(bool locked)
    {
        return locked ? "locked" : "unlocked";
    }

    private static string Describe(ItemSlotUI slotUi)
    {
        return slotUi?.assignedSlot == null ? "an unavailable slot" : Describe(slotUi.assignedSlot);
    }

    private static string Describe(
#if IL2CPP
        Il2CppScheduleOne.ItemFramework.ItemSlot slot
#elif MONO
        ScheduleOne.ItemFramework.ItemSlot slot
#endif
    )
    {
        if (!UserSlotLockRegistry.TryResolve(slot, out var identity))
            return "an unsupported slot";

        return $"{identity.OwnerKind} slot {identity.SlotIndex + 1}";
    }
}
