using Backpack.Config;
using Backpack.UI;
using UnityEngine;

#if IL2CPP
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Tools;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using Il2CppSystem.Linq;
#elif MONO
using ScheduleOne.Core.Items.Framework;
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Levelling;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Product;
using ScheduleOne.Product.Packaging;
using ScheduleOne.Storage;
using ScheduleOne.Tools;
using ScheduleOne.UI;
using ScheduleOne.UI.Phone;
#endif

namespace Backpack;

public class PlayerBackpack : MonoBehaviour
{
    public const string StorageName = "Backpack";
    public const int MaxStorageSlots = 128;

    private bool _backpackEnabled = true;
    private StorageEntity _storage;

#if IL2CPP
    public PlayerBackpack(IntPtr ptr) : base(ptr)
    {
    }
#endif

    public static PlayerBackpack Instance { get; private set; }

    public StorageEntity Storage => _storage;
    public int BackpackSlotCount => _storage?.ItemSlots?.Count ?? 0;
    public bool IsUnlocked => true;
    public bool IsOpen => Singleton<StorageMenu>.Instance.IsOpen && Singleton<StorageMenu>.Instance.TitleLabel.text == StorageName;
#if IL2CPP
    public Il2CppSystem.Collections.Generic.List<ItemSlot> ItemSlots => GetBackpackSlots();
#elif MONO
    public List<ItemSlot> ItemSlots => GetBackpackSlots();
#endif

#if IL2CPP
    public Il2CppSystem.Collections.Generic.List<ItemSlot> GetBackpackSlots()
    {
        return _storage.ItemSlots.Cast<Il2CppSystem.Collections.Generic.IEnumerable<ItemSlot>>().ToList();
    }
#elif MONO
    public List<ItemSlot> GetBackpackSlots()
    {
        return _storage.ItemSlots.ToList();
    }
#endif

    public ItemSlot GetBackpackSlot(int index)
    {
        if (_storage == null || index < 0 || index >= _storage.ItemSlots.Count)
            return null;

#if IL2CPP
        return _storage.ItemSlots[new Index(index)].Cast<ItemSlot>();
#elif MONO
        return _storage.ItemSlots[index];
#endif
    }

    public bool OwnsSlot(ItemSlot candidate)
    {
        if (candidate == null || _storage == null)
            return false;

        for (var i = 0; i < _storage.ItemSlots.Count; i++)
        {
            if (SlotsMatch(candidate, GetBackpackSlot(i)))
                return true;
        }

        return false;
    }

    public uint GetQuantityOfItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || _storage?.ItemSlots == null)
            return 0;

        uint total = 0;
        for (var i = 0; i < _storage.ItemSlots.Count; i++)
        {
            var slot = GetBackpackSlot(i);
            if (slot?.ItemInstance?.Definition?.ID == null
                || !slot.ItemInstance.Definition.ID.Equals(itemId, StringComparison.OrdinalIgnoreCase))
                continue;

            total += (uint) Math.Max(0, slot.Quantity);
        }

        return total;
    }

    public bool TryRemoveQuantityOfItem(string itemId, uint amount)
    {
        if (amount == 0)
            return true;
        if (GetQuantityOfItem(itemId) < amount)
            return false;

        var remaining = amount;
        for (var i = 0; i < _storage.ItemSlots.Count && remaining > 0; i++)
        {
            var slot = GetBackpackSlot(i);
            if (slot?.ItemInstance?.Definition?.ID == null
                || !slot.ItemInstance.Definition.ID.Equals(itemId, StringComparison.OrdinalIgnoreCase))
                continue;

            var removed = Math.Min((uint) Math.Max(0, slot.Quantity), remaining);
            var newQuantity = slot.Quantity - (int) removed;
            if (newQuantity <= 0)
                slot.ClearStoredInstance(true);
            else
                slot.SetQuantity(newQuantity, true);
            remaining -= removed;
        }

        return remaining == 0;
    }

    public static bool SlotsMatch(ItemSlot left, ItemSlot right)
    {
        if (left == null || right == null)
            return false;

#if IL2CPP
        return left.Pointer == right.Pointer;
#elif MONO
        return ReferenceEquals(left, right);
#endif
    }

    private void Awake()
    {
        _storage = gameObject.GetComponentInParent<StorageEntity>();
        if (_storage == null)
        {
            Logger.Error("Player does not have a BackpackStorage component!");
            return;
        }

        UpdateSize(Configuration.Instance.StorageSlots);
        LogLifecycle("Awake");
        OnStartClient(true);
    }

    private void Update()
    {
        if (!_backpackEnabled)
            return;

        if (IsOpen && IsExternalUiOpen())
        {
            Close();
            return;
        }

        if (Configuration.Instance.ToggleKey == KeyCode.None || !Input.GetKeyDown(Configuration.Instance.ToggleKey))
            return;

        if (StorageHotkeys.IsTyping())
        {
            Logger.Diagnostic("[Input] Backpack toggle ignored because an active text field has focus.");
            return;
        }

        try
        {
            if (IsOpen)
                Close();
            else
                Open();
        }
        catch (Exception e)
        {
            Logger.Error("Error toggling backpack: " + e.Message);
        }
    }

    public void SetBackpackEnabled(bool state)
    {
        if (!state)
            Close();

        _backpackEnabled = state;
    }

    public void Open()
    {
        if (!_backpackEnabled || Singleton<ManagementClipboard>.Instance.IsEquipped || Singleton<StorageMenu>.Instance.IsOpen || IsExternalUiOpen())
            return;

        var storageMenu = Singleton<StorageMenu>.Instance;
#if IL2CPP
        storageMenu.Open(_storage.Cast<IItemSlotOwner>(), StorageName, string.Empty, null);
#elif MONO
        storageMenu.Open(StorageName, string.Empty, _storage);
#endif
        _storage.SendAccessor(Player.Local.NetworkObject);
    }

    public void Close()
    {
        if (!_backpackEnabled || !IsOpen)
            return;

        Singleton<StorageMenu>.Instance.Close();
        _storage.SendAccessor(null);
    }

    public void SetContextualAccessor(bool isAccessing)
    {
        if (_storage == null || IsOpen)
            return;

        _storage.SendAccessor(isAccessing ? Player.Local.NetworkObject : null);
    }

    public bool SortContents()
    {
        if (!InventorySorter.SortBackpack(this))
            return false;

        UI.ResponsiveBackpackUI.Refresh();
        UI.ContextualBackpackPanel.Refresh();
        UI.PhoneBackpackUI.Refresh();
        return true;
    }

    public bool ConsolidateMagazines()
    {
        if (!MagazineConsolidator.ConsolidateBackpack(this))
            return false;

        UI.ResponsiveBackpackUI.Refresh();
        UI.ContextualBackpackPanel.Refresh();
        UI.PhoneBackpackUI.Refresh();
        return true;
    }

    public bool ContainsItemsOfInterest(EStealthLevel maxStealthLevel)
    {
        for (var i = 0; i < _storage.ItemSlots.Count; i++)
        {
#if IL2CPP
            var itemSlot = _storage.ItemSlots[new Index(i)].Cast<ItemSlot>();
#elif MONO
            var itemSlot = _storage.ItemSlots[i];
#endif
            if (itemSlot?.ItemInstance == null)
                continue;

#if IL2CPP
            var productInstance = itemSlot.ItemInstance.TryCast<ProductItemInstance>();
#elif MONO
            var productInstance = itemSlot.ItemInstance as ProductItemInstance;
#endif
            if (productInstance == null)
            {
                if (itemSlot.ItemInstance.Definition.legalStatus != ELegalStatus.Legal)
                    return true;

                continue;
            }

            if (productInstance.AppliedPackaging == null || productInstance.AppliedPackaging.StealthLevel <= maxStealthLevel)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds the specified number of slots to the backpack.
    /// </summary>
    /// <remarks>The maximum number of slots for storage is 128.</remarks>
    /// <param name="slotCount">The number of slots to add.</param>
    public void Upgrade(int slotCount)
    {
        if (slotCount is < 1 or > MaxStorageSlots)
            return;

        var newSlotCount = _storage.SlotCount + slotCount;
        if (newSlotCount > MaxStorageSlots)
        {
            Logger.Warning("Cannot upgrade backpack to more than {0} slots.", MaxStorageSlots);
            return;
        }

        UpdateSize(newSlotCount);
    }

    /// <summary>
    /// Removes the specified number of slots from the backpack.
    /// </summary>
    /// <param name="slotCount">The number of slots to remove.</param>
    /// <param name="force">If true, will remove slots even if they contain items.</param>
    public void Downgrade(int slotCount, bool force = false)
    {
        if (slotCount < 1)
            return;

        if (!force && slotCount >= _storage.SlotCount)
        {
            Logger.Warning("Cannot downgrade backpack to zero slots. A minimum of one must remain.");
            return;
        }

        var newSlotCount = _storage.SlotCount - slotCount;
        if (newSlotCount < 1)
            newSlotCount = 1;

        if (force)
        {
            UpdateSize(newSlotCount);
            return;
        }

        var isSafeToRemove = true;
        var removedSlots = _storage.ItemSlots.GetRange(newSlotCount, _storage.SlotCount - newSlotCount);
        for (var i = 0; i < removedSlots.Count; i++)
        {
#if IL2CPP
            var itemSlot = removedSlots[new Index(i)].Cast<ItemSlot>();
#elif MONO
            var itemSlot = removedSlots[new Index(i)] as ItemSlot;
#endif
            if (itemSlot?.ItemInstance == null)
                continue;

            Logger.Warning("Downgrading backpack will remove item: " + itemSlot.ItemInstance.Definition.Name);
            isSafeToRemove = false;
        }

        if (!isSafeToRemove)
        {
            Logger.Warning("Cannot downgrade backpack due to items present in removed slots.");
            return;
        }

        UpdateSize(newSlotCount);
    }

    public void ApplyConfiguredStorageSlots()
    {
        var configuredSlots = Math.Clamp(Configuration.Instance.StorageSlots, 1, MaxStorageSlots);
        if (configuredSlots == _storage.SlotCount)
            return;

        if (configuredSlots > _storage.SlotCount)
        {
            UpdateSize(configuredSlots);
            return;
        }

        Downgrade(_storage.SlotCount - configuredSlots);
    }

    private void UpdateSize(int newSize)
    {
        UserSlotLockRegistry.InvalidateSlotIdentityCache();
        _storage.SlotCount = newSize;
        _storage.DisplayRowCount = newSize switch
        {
            <= 20 => (int) Math.Ceiling(newSize / 5.0),
            <= 80 => (int) Math.Ceiling(newSize / 10.0),
            _ => (int) Math.Ceiling(newSize / 16.0)
        };

        if (_storage.ItemSlots.Count > newSize)
        {
            _storage.ItemSlots.RemoveRange(newSize, _storage.ItemSlots.Count - newSize);
            return;
        }

        for (var i = _storage.ItemSlots.Count; i < newSize; i++)
        {
            var itemSlot = new ItemSlot();
#if IL2CPP
            if (itemSlot.onItemDataChanged == null)
                itemSlot.onItemDataChanged = (Il2CppSystem.Action) _storage.ContentsChanged;
            else
                itemSlot.onItemDataChanged.CombineImpl((Il2CppSystem.Action) _storage.ContentsChanged);

            itemSlot.SetSlotOwner(_storage.Cast<IItemSlotOwner>());
#elif MONO
            itemSlot.onItemDataChanged += _storage.ContentsChanged;
            itemSlot.SetSlotOwner(_storage);
#endif
        }
    }

    private void OnStartClient(bool isOwner)
    {
        if (!isOwner)
        {
            Destroy(this);
            return;
        }

        if (Instance != null && !InstancesMatch(Instance, this))
        {
            Logger.Warning(
                "[Lifecycle] Replacing a prior PlayerBackpack reference with the current local player's component.");
        }

        Instance = this;
        LogLifecycle("Bound local instance");
    }

    private void OnDestroy()
    {
#if SCHEDULE_I_047
        Patches.PlayerPatch.CancelPendingHydration(gameObject.GetComponentInParent<Player>(), "PlayerBackpack destroyed");
#endif
        var wasAuthoritative = InstancesMatch(Instance, this);
        if (wasAuthoritative)
            Instance = null!;

        LogLifecycle(wasAuthoritative ? "Destroyed and cleared local instance" : "Destroyed non-authoritative instance");
    }

    internal static void ResetSessionReference(string reason)
    {
        var prior = Instance;
        Instance = null!;
        Logger.Diagnostic(() => $"[Lifecycle] PlayerBackpack session reference cleared ({reason}); prior={DescribeInstance(prior)}.");
    }

    private void LogLifecycle(string stage)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        try
        {
            var player = gameObject.GetComponentInParent<Player>();
            var firstSlot = GetBackpackSlot(0);
            Logger.Diagnostic(
                $"[Lifecycle] PlayerBackpack {stage}: component={DescribeInstance(this)}, " +
                $"player={DescribePlayer(player)}, storage={DescribeStorage(_storage)}, " +
                $"slots={BackpackSlotCount}, firstSlot={DescribeSlot(firstSlot)}.");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[Lifecycle] PlayerBackpack {stage} diagnostics failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool InstancesMatch(PlayerBackpack left, PlayerBackpack right)
    {
#if IL2CPP
        return left != null && right != null && left.Pointer != IntPtr.Zero && left.Pointer == right.Pointer;
#elif MONO
        return ReferenceEquals(left, right);
#endif
    }

    private static bool IsExternalUiOpen()
    {
        return IsPhoneOpen() || IsGameplayMenuOpen();
    }

    private static bool IsPhoneOpen()
    {
        try
        {
            return Phone.Instance?.IsOpen == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGameplayMenuOpen()
    {
        try
        {
            return Singleton<GameplayMenu>.Instance?.IsOpen == true;
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeInstance(PlayerBackpack backpack)
    {
#if IL2CPP
        return backpack == null || backpack.Pointer == IntPtr.Zero
            ? "none"
            : $"#{backpack.GetInstanceID()}/0x{backpack.Pointer.ToInt64():X}";
#elif MONO
        return backpack == null ? "none" : $"#{backpack.GetInstanceID()}";
#endif
    }

    private static string DescribePlayer(Player player)
    {
#if IL2CPP
        return player == null || player.Pointer == IntPtr.Zero
            ? "none"
            : $"#{player.GetInstanceID()}/0x{player.Pointer.ToInt64():X}";
#elif MONO
        return player == null ? "none" : $"#{player.GetInstanceID()}";
#endif
    }

    private static string DescribeStorage(StorageEntity storage)
    {
#if IL2CPP
        return storage == null || storage.Pointer == IntPtr.Zero
            ? "none"
            : $"#{storage.GetInstanceID()}/0x{storage.Pointer.ToInt64():X}";
#elif MONO
        return storage == null ? "none" : $"#{storage.GetInstanceID()}";
#endif
    }

    private static string DescribeSlot(ItemSlot slot)
    {
#if IL2CPP
        return slot == null || slot.Pointer == IntPtr.Zero ? "none" : $"0x{slot.Pointer.ToInt64():X}";
#elif MONO
        return slot == null ? "none" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slot).ToString();
#endif
    }
}
