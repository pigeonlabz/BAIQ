#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
#elif MONO
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
#endif

namespace Backpack;

public enum UserSlotOwnerKind
{
    PlayerInventory,
    Backpack,
    SessionStorage
}

public readonly struct UserSlotIdentity
{
    public UserSlotIdentity(UserSlotOwnerKind ownerKind, int slotIndex, IItemSlotOwner storageOwner = null)
    {
        OwnerKind = ownerKind;
        SlotIndex = slotIndex;
        StorageOwner = storageOwner;
    }

    public UserSlotOwnerKind OwnerKind { get; }
    public int SlotIndex { get; }
    internal IItemSlotOwner StorageOwner { get; }
}

public static class UserSlotLockRegistry
{
    public const string SaveFileName = "InventoryExpandedSlotLocks";

    private const string FormatVersion = "v1";
    private static readonly HashSet<int> PlayerLocks = new();
    private static readonly HashSet<int> BackpackLocks = new();
    private static readonly List<StorageLockSet> StorageLocks = new();
#if IL2CPP
    private static readonly Dictionary<IntPtr, UserSlotIdentity> SlotIdentities = new();
#elif MONO
    private static readonly Dictionary<ItemSlot, UserSlotIdentity> SlotIdentities = new(new ItemSlotReferenceComparer());
#endif
    private static bool _backpackIdentityCacheInitialized;
    private static bool _playerIdentityCacheInitialized;

    private sealed class StorageLockSet
    {
        public IItemSlotOwner Owner { get; init; }
        public IntPtr OwnerPointer { get; init; }
        public int UnityInstanceId { get; init; }
        public int ManagedIdentity { get; init; }
        public bool IdentityCacheInitialized { get; set; }
        public HashSet<int> LockedIndices { get; } = new();
    }

#if MONO
    private sealed class ItemSlotReferenceComparer : IEqualityComparer<ItemSlot>
    {
        public bool Equals(ItemSlot left, ItemSlot right) => ReferenceEquals(left, right);

        public int GetHashCode(ItemSlot slot) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slot);
    }
#endif

    public static bool IsUserLocked(ItemSlot slot)
    {
        return TryResolve(slot, out var identity) && IsUserLocked(identity);
    }

    public static bool IsUserLocked(IItemSlotOwner owner, ItemSlot slot)
    {
        return TryResolve(owner, slot, out var identity) && IsUserLocked(identity);
    }

    public static bool TryResolve(ItemSlot slot, out UserSlotIdentity identity)
    {
        return TryResolve(null, slot, out identity);
    }

    public static bool TryResolve(IItemSlotOwner owner, ItemSlot slot, out UserSlotIdentity identity)
    {
        identity = default;
        if (!TryGetSlotKey(slot, out var slotKey))
            return false;

        if (!TryEnsureCoreIdentityCaches())
            return false;

        if (TryGetCachedIdentity(slotKey, out identity))
            return true;

        owner ??= TryGetSlotOwner(slot);
        CacheStorageOwner(owner);
        return TryGetCachedIdentity(slotKey, out identity);
    }

    public static void InvalidateSlotIdentityCache()
    {
        SlotIdentities.Clear();
        _backpackIdentityCacheInitialized = false;
        _playerIdentityCacheInitialized = false;

        for (var i = 0; i < StorageLocks.Count; i++)
            StorageLocks[i].IdentityCacheInitialized = false;
    }

    public static bool Toggle(ItemSlot slot, out bool isLocked)
    {
        isLocked = false;
        if (!TryResolve(slot, out var identity))
            return false;

        isLocked = !IsUserLocked(identity);
        SetLocked(identity, isLocked);
        return true;
    }

    public static string SerializePersistent()
    {
        return string.Join("\n", new[]
        {
            FormatVersion,
            "player:" + SerializeIndices(PlayerLocks),
            "backpack:" + SerializeIndices(BackpackLocks)
        });
    }

    public static void LoadPersistent(string contents)
    {
        PlayerLocks.Clear();
        BackpackLocks.Clear();
        StorageLocks.Clear();
        InvalidateSlotIdentityCache();

        if (string.IsNullOrWhiteSpace(contents))
            return;

        try
        {
            var lines = contents.Replace("\r", string.Empty).Split('\n');
            if (lines.Length == 0 || !string.Equals(lines[0].Trim(), FormatVersion, StringComparison.Ordinal))
            {
                Logger.Warning("Ignoring unsupported user slot lock save data.");
                return;
            }

            for (var i = 1; i < lines.Length; i++)
            {
                var separator = lines[i].IndexOf(':');
                if (separator < 0)
                    continue;

                var name = lines[i].Substring(0, separator).Trim();
                var values = lines[i].Substring(separator + 1);
                if (string.Equals(name, "player", StringComparison.OrdinalIgnoreCase))
                    ParseIndices(values, PlayerLocks);
                else if (string.Equals(name, "backpack", StringComparison.OrdinalIgnoreCase))
                    ParseIndices(values, BackpackLocks);
            }
        }
        catch (Exception exception)
        {
            PlayerLocks.Clear();
            BackpackLocks.Clear();
            Logger.Warning($"Ignoring invalid user slot lock save data: {exception.Message}");
        }
    }

    public static void ResetSession(string reason)
    {
        var count = PlayerLocks.Count + BackpackLocks.Count;
        for (var i = 0; i < StorageLocks.Count; i++)
            count += StorageLocks[i].LockedIndices.Count;

        PlayerLocks.Clear();
        BackpackLocks.Clear();
        StorageLocks.Clear();
        InvalidateSlotIdentityCache();

        if (count > 0)
            Logger.Diagnostic($"[Lifecycle] Cleared {count} user slot locks ({reason}).");
    }

    public static bool IsUserLocked(UserSlotIdentity identity)
    {
        return identity.OwnerKind switch
        {
            UserSlotOwnerKind.PlayerInventory => PlayerLocks.Contains(identity.SlotIndex),
            UserSlotOwnerKind.Backpack => BackpackLocks.Contains(identity.SlotIndex),
            UserSlotOwnerKind.SessionStorage => FindStorageLockSet(identity.StorageOwner, false)?.LockedIndices.Contains(identity.SlotIndex) == true,
            _ => false
        };
    }

    private static void SetLocked(UserSlotIdentity identity, bool locked)
    {
        HashSet<int> locks = identity.OwnerKind switch
        {
            UserSlotOwnerKind.PlayerInventory => PlayerLocks,
            UserSlotOwnerKind.Backpack => BackpackLocks,
            UserSlotOwnerKind.SessionStorage => FindStorageLockSet(identity.StorageOwner, locked)?.LockedIndices,
            _ => null
        };

        if (locks == null)
            return;

        if (locked)
            locks.Add(identity.SlotIndex);
        else
            locks.Remove(identity.SlotIndex);
    }

    private static void EnsureCoreIdentityCaches()
    {
        if (!_backpackIdentityCacheInitialized)
        {
            var backpack = PlayerBackpack.Instance;
            if (backpack?.Storage?.ItemSlots != null)
            {
                for (var i = 0; i < backpack.BackpackSlotCount; i++)
                    CacheSlot(backpack.GetBackpackSlot(i), new UserSlotIdentity(UserSlotOwnerKind.Backpack, i));

                _backpackIdentityCacheInitialized = true;
            }
        }

        if (!_playerIdentityCacheInitialized)
        {
            var playerSlots = PlayerInventory.Instance?.GetAllInventorySlots();
            if (playerSlots != null)
            {
                for (var i = 0; i < playerSlots.Count; i++)
                    CacheSlot(GetSlot(playerSlots, i), new UserSlotIdentity(UserSlotOwnerKind.PlayerInventory, i));

                _playerIdentityCacheInitialized = true;
            }
        }
    }

    private static bool TryEnsureCoreIdentityCaches()
    {
        try
        {
            EnsureCoreIdentityCaches();
            return true;
        }
        catch (NullReferenceException)
        {
            return false;
        }
#if IL2CPP
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
#endif
    }

    private static void CacheStorageOwner(IItemSlotOwner owner)
    {
        if (owner == null)
            return;

        if (!TryGetOwnerSlots(owner, out var ownerSlots))
            return;

        var storageLockSet = FindStorageLockSet(owner, true);
        if (storageLockSet == null || storageLockSet.IdentityCacheInitialized)
            return;

        try
        {
            for (var i = 0; i < ownerSlots.Count; i++)
            {
                CacheSlot(
                    GetSlot(ownerSlots, i),
                    new UserSlotIdentity(UserSlotOwnerKind.SessionStorage, i, owner));
            }

            storageLockSet.IdentityCacheInitialized = true;
        }
        catch (NullReferenceException)
        {
            // Transient or destroyed owners are not authoritative lock targets.
        }
#if IL2CPP
        catch (Il2CppException)
        {
            // Transient or destroyed owners are not authoritative lock targets.
        }
        catch (ObjectCollectedException)
        {
            // Transient or destroyed owners are not authoritative lock targets.
        }
#endif
    }

    private static IItemSlotOwner TryGetSlotOwner(ItemSlot slot)
    {
        try
        {
            return slot?.SlotOwner;
        }
        catch
        {
            return null;
        }
    }

    private static void CacheSlot(ItemSlot slot, UserSlotIdentity identity)
    {
        if (TryGetSlotKey(slot, out var key))
            SlotIdentities[key] = identity;
    }

#if IL2CPP
    private static bool TryGetCachedIdentity(IntPtr key, out UserSlotIdentity identity)
#elif MONO
    private static bool TryGetCachedIdentity(ItemSlot key, out UserSlotIdentity identity)
#endif
    {
        if (!SlotIdentities.TryGetValue(key, out identity))
            return false;

        if (IsAuthoritativeSlot(key, identity))
            return true;

        SlotIdentities.Remove(key);
        identity = default;
        return false;
    }

#if IL2CPP
    private static bool IsAuthoritativeSlot(IntPtr key, UserSlotIdentity identity)
#elif MONO
    private static bool IsAuthoritativeSlot(ItemSlot key, UserSlotIdentity identity)
#endif
    {
        try
        {
            ItemSlot authoritativeSlot;
            switch (identity.OwnerKind)
            {
                case UserSlotOwnerKind.Backpack:
                    var backpack = PlayerBackpack.Instance;
                    if (backpack == null || identity.SlotIndex < 0 || identity.SlotIndex >= backpack.BackpackSlotCount)
                        return false;

                    authoritativeSlot = backpack.GetBackpackSlot(identity.SlotIndex);
                    break;
                case UserSlotOwnerKind.PlayerInventory:
                    var playerSlots = PlayerInventory.Instance?.GetAllInventorySlots();
                    if (playerSlots == null || identity.SlotIndex < 0 || identity.SlotIndex >= playerSlots.Count)
                        return false;

                    authoritativeSlot = GetSlot(playerSlots, identity.SlotIndex);
                    break;
                case UserSlotOwnerKind.SessionStorage:
                    if (!TryGetOwnerSlots(identity.StorageOwner, out var ownerSlots) ||
                        identity.SlotIndex < 0 || identity.SlotIndex >= ownerSlots.Count)
                        return false;

                    authoritativeSlot = GetSlot(ownerSlots, identity.SlotIndex);
                    break;
                default:
                    return false;
            }

            return SlotKeyMatches(authoritativeSlot, key);
        }
        catch
        {
            return false;
        }
    }

#if IL2CPP
    private static bool SlotKeyMatches(ItemSlot slot, IntPtr expectedKey)
#elif MONO
    private static bool SlotKeyMatches(ItemSlot slot, ItemSlot expectedKey)
#endif
    {
#if IL2CPP
        return TryGetSlotKey(slot, out var actualKey) && actualKey == expectedKey;
#elif MONO
        return TryGetSlotKey(slot, out var actualKey) && ReferenceEquals(actualKey, expectedKey);
#endif
    }

#if IL2CPP
    private static bool TryGetSlotKey(ItemSlot slot, out IntPtr key)
    {
        key = IntPtr.Zero;
        if (slot == null)
            return false;

        try
        {
#if IL2CPP
            if (slot.WasCollected)
                return false;
#endif
            key = slot.Pointer;
            return key != IntPtr.Zero;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
    }
#elif MONO
    private static bool TryGetSlotKey(ItemSlot slot, out ItemSlot key)
    {
        key = slot;
        return slot != null;
    }
#endif

    private static StorageLockSet FindStorageLockSet(IItemSlotOwner owner, bool create)
    {
        if (owner == null)
            return null;

        for (var i = StorageLocks.Count - 1; i >= 0; i--)
        {
            if (!IsOwnerAlive(StorageLocks[i]))
            {
                StorageLocks.RemoveAt(i);
                continue;
            }

            if (OwnersMatch(StorageLocks[i], owner))
                return StorageLocks[i];
        }

        if (!create)
            return null;

        var created = new StorageLockSet
        {
            Owner = owner,
            OwnerPointer = GetOwnerPointer(owner),
            UnityInstanceId = GetUnityInstanceId(owner),
            ManagedIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(owner)
        };
        StorageLocks.Add(created);
        return created;
    }

    private static bool OwnersMatch(StorageLockSet existing, IItemSlotOwner candidate)
    {
        if (ReferenceEquals(existing.Owner, candidate))
            return true;

#if IL2CPP
        var candidateUnityId = GetUnityInstanceId(candidate);
        if (existing.UnityInstanceId != 0 && candidateUnityId != 0)
            return existing.UnityInstanceId == candidateUnityId;

        var candidatePointer = GetOwnerPointer(candidate);
        return existing.OwnerPointer != IntPtr.Zero && existing.OwnerPointer == candidatePointer;
#elif MONO
        var candidateUnityId = GetUnityInstanceId(candidate);
        if (existing.UnityInstanceId != 0 && candidateUnityId != 0)
            return existing.UnityInstanceId == candidateUnityId;

        return existing.ManagedIdentity == System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(candidate);
#endif
    }

    private static bool IsOwnerAlive(StorageLockSet owner)
    {
        if (owner.UnityInstanceId == 0)
            return true;

        return GetUnityInstanceId(owner.Owner) == owner.UnityInstanceId;
    }

    private static int GetUnityInstanceId(IItemSlotOwner owner)
    {
        if (owner == null)
            return 0;

        try
        {
#if IL2CPP
            var wrapper = owner as Il2CppObjectBase;
            if (!IsIl2CppObjectUsable(wrapper))
                return 0;

            var unityObject = wrapper?.TryCast<UnityEngine.Object>();
            return unityObject == null ? 0 : unityObject.GetInstanceID();
#elif MONO
            return owner is UnityEngine.Object unityObject && unityObject != null
                ? unityObject.GetInstanceID()
                : 0;
#endif
        }
        catch
        {
            return 0;
        }
    }

    private static IntPtr GetOwnerPointer(IItemSlotOwner owner)
    {
#if IL2CPP
        return owner is Il2CppObjectBase wrapper && IsIl2CppObjectUsable(wrapper)
            ? wrapper.Pointer
            : IntPtr.Zero;
#else
        return IntPtr.Zero;
#endif
    }

#if IL2CPP
    private static bool IsIl2CppObjectUsable(Il2CppObjectBase wrapper)
    {
        try
        {
            return wrapper != null && wrapper.Pointer != IntPtr.Zero && !wrapper.WasCollected;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
    }
#endif

    private static bool TryGetOwnerSlots(
        IItemSlotOwner owner,
#if IL2CPP
        out Il2CppSystem.Collections.Generic.List<ItemSlot> slots)
#elif MONO
        out List<ItemSlot> slots)
#endif
    {
        slots = null;
        if (owner == null)
            return false;

#if IL2CPP
        if (owner is Il2CppObjectBase wrapper && !IsIl2CppObjectUsable(wrapper))
            return false;
#endif

        try
        {
            slots = owner.ItemSlots;
            return slots != null;
        }
        catch (NullReferenceException)
        {
            return false;
        }
#if IL2CPP
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
#endif
    }

    private static string SerializeIndices(HashSet<int> indices)
    {
        return string.Join(",", indices.OrderBy(index => index));
    }

    private static void ParseIndices(string values, HashSet<int> destination)
    {
        if (string.IsNullOrWhiteSpace(values))
            return;

        var parts = values.Split(',');
        for (var i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i].Trim(), out var index) && index >= 0)
                destination.Add(index);
        }
    }

    private static ItemSlot GetSlot(
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<ItemSlot> slots,
#elif MONO
        List<ItemSlot> slots,
#endif
        int index)
    {
#if IL2CPP
        return slots[new Index(index)]?.TryCast<ItemSlot>();
#elif MONO
        return slots[index];
#endif
    }
}
