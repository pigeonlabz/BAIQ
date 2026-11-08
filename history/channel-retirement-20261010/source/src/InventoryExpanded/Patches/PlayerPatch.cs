using HarmonyLib;

#if SCHEDULE_I_047
using System.Collections;
using MelonLoader;
using UnityEngine;
#endif

#if IL2CPP
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
#elif MONO
using ScheduleOne.Persistence;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(Player))]
public static class PlayerPatch
{
    private const string BackpackSaveFile = "Backpack";

#if SCHEDULE_I_047
    private const int MaxHydrationReadinessAttempts = 3;
    private const float HydrationRetryDelaySeconds = 0.1f;
    private static readonly Dictionary<IntPtr, PendingBackpackHydration> PendingHydrations = new();

    private sealed class PendingBackpackHydration
    {
        public PendingBackpackHydration(Player player, DeserializedItemSet itemSet, int sessionGeneration, int payloadLength)
        {
            Player = player;
            PlayerKey = player.Pointer;
            ItemSet = itemSet;
            SessionGeneration = sessionGeneration;
            PayloadLength = payloadLength;
        }

        public Player Player { get; }
        public IntPtr PlayerKey { get; }
        public DeserializedItemSet ItemSet { get; }
        public int SessionGeneration { get; }
        public int PayloadLength { get; }
        public bool Applying { get; set; }
    }
#endif

    [HarmonyPatch("Awake")]
    [HarmonyPrefix]
    public static void Awake(Player __instance)
    {
        if (!__instance.LocalExtraFiles.Contains(BackpackSaveFile))
            __instance.LocalExtraFiles.Add(BackpackSaveFile);

        if (!__instance.LocalExtraFiles.Contains(UserSlotLockRegistry.SaveFileName))
            __instance.LocalExtraFiles.Add(UserSlotLockRegistry.SaveFileName);
    }

    [HarmonyPatch("WriteData")]
    [HarmonyPostfix]
    public static void WriteData(Player __instance, string parentFolderPath)
    {
        if (!__instance.IsOwner)
        {
            Logger.Diagnostic(() =>
                $"[BackpackPersistence] Write skipped for non-owner player {DescribePlayer(__instance)}; target='{DescribeBackpackPath(parentFolderPath)}'.");
            return;
        }

        if (TryGetAuthoritativeBackpackStorage(__instance, out var backpackStorage, out var storageReason))
        {
            try
            {
                var contents = new ItemSet(backpackStorage.ItemSlots).GetJSON();
                var (nonEmptySlots, totalQuantity) = CountBackpackContents(backpackStorage);

#if IL2CPP
                __instance.Cast<ISaveable>().WriteSubfile(parentFolderPath, BackpackSaveFile, contents);
#elif MONO
                ISaveable instance = __instance;
                instance.WriteSubfile(parentFolderPath, BackpackSaveFile, contents);
#endif

                Logger.Diagnostic(() =>
                    $"[BackpackPersistence] Backpack written for {DescribePlayer(__instance)}; storage={DescribeStorage(backpackStorage)}, target='{DescribeBackpackPath(parentFolderPath)}', slots={backpackStorage.ItemSlots.Count}, nonEmpty={nonEmptySlots}, quantity={totalQuantity}, payloadLength={contents?.Length ?? 0}.");
            }
            catch (Exception exception)
            {
                Logger.Error(exception,
                    "[BackpackPersistence] Failed to write authoritative Backpack for {0}.",
                    DescribePlayer(__instance));
            }
        }
        else
        {
            Logger.Warning(
                "[BackpackPersistence] Backpack write skipped for authoritative player {0}: {1}. Existing persistence was left untouched.",
                DescribePlayer(__instance),
                storageReason);
        }

        var lockData = UserSlotLockRegistry.SerializePersistent();
#if IL2CPP
        __instance.Cast<ISaveable>().WriteSubfile(parentFolderPath, UserSlotLockRegistry.SaveFileName, lockData);
#elif MONO
        ISaveable instance = __instance;
        instance.WriteSubfile(parentFolderPath, UserSlotLockRegistry.SaveFileName, lockData);
#endif
    }

#if !SCHEDULE_I_047
    [HarmonyPatch("Load", typeof(PlayerData), typeof(string))]
    [HarmonyPrefix]
    public static void Load(Player __instance, PlayerData data, string containerPath)
    {
        if (__instance.IsOwner)
        {
            if (__instance.Loader.TryLoadFile(containerPath, UserSlotLockRegistry.SaveFileName, out var lockData))
                UserSlotLockRegistry.LoadPersistent(lockData);
            else
                UserSlotLockRegistry.LoadPersistent(null);
        }

        if (!__instance.Loader.TryLoadFile(containerPath, BackpackSaveFile, out var backpackData))
            return;

        try
        {
            var backpackStorage = __instance.GetBackpackStorage();
            if (!ItemSet.TryDeserialize(backpackData, out var itemSet))
            {
                Logger.Error("Failed to deserialize backpack data.");
                return;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);
        }
        catch (Exception e)
        {
            Logger.Error($"Error loading backpack data: {e.Message}");
        }
    }
#endif

#if SCHEDULE_I_047
    [HarmonyPatch("LoadInventory")]
    [HarmonyPrefix]
    private static void LoadInventoryPrefix(
        Player __instance,
        ref string contentsString,
        out PendingBackpackHydration __state)
    {
        __state = null;
        if (!BackpackPersistencePolicy.TryExtractBackpackPayload(
                contentsString,
                out var nativeInventory,
                out var backpackData))
            return;

        // The native method must never see the mod's delimited payload.
        contentsString = nativeInventory;
        if (string.IsNullOrWhiteSpace(backpackData))
        {
            Logger.Warning("[BackpackPersistence] 0.4.7 LoadInventory contained an empty Backpack payload; hydration was skipped.");
            return;
        }

        if (!ItemSet.TryDeserialize(backpackData, out var itemSet))
        {
            Logger.Error("[BackpackPersistence] Failed to deserialize 0.4.7 Backpack payload; runtime Backpack was not changed.");
            return;
        }

        if (__instance == null || __instance.Pointer == IntPtr.Zero)
        {
            Logger.Warning("[BackpackPersistence] 0.4.7 Backpack payload had no live Player target; hydration was skipped.");
            return;
        }

        var pending = new PendingBackpackHydration(
            __instance,
            itemSet,
            BackpackMod.SessionGeneration,
            backpackData.Length);
        if (PendingHydrations.TryGetValue(pending.PlayerKey, out var replaced))
        {
            Logger.Diagnostic(() =>
                $"[BackpackPersistence] Replaced pending 0.4.7 hydration for {DescribePlayer(replaced.Player)} before it was applied.");
        }

        PendingHydrations[pending.PlayerKey] = pending;
        __state = pending;
        Logger.Diagnostic(() =>
            $"[BackpackPersistence] 0.4.7 payload cached for {DescribePlayer(__instance)}; owner={__instance.IsOwner}, session={pending.SessionGeneration}, nativeLength={nativeInventory.Length}, payloadLength={pending.PayloadLength}.");
    }

    [HarmonyPatch("LoadInventory")]
    [HarmonyPostfix]
    private static void LoadInventoryPostfix(PendingBackpackHydration __state)
    {
        if (__state == null || !IsCurrentPending(__state))
            return;

        Logger.Diagnostic(() =>
            $"[BackpackPersistence] Native 0.4.7 LoadInventory completed for {DescribePlayer(__state.Player)}; deferred Backpack hydration scheduled.");
        MelonCoroutines.Start(CompletePendingHydration(__state));
    }

    [HarmonyPatch("LoadInventory")]
    [HarmonyFinalizer]
    private static Exception LoadInventoryFinalizer(Exception __exception, PendingBackpackHydration __state)
    {
        if (__exception != null && __state != null)
        {
            RemovePending(__state);
            Logger.Error(__exception,
                "[BackpackPersistence] Native 0.4.7 LoadInventory failed for {0}; pending Backpack hydration was cancelled.",
                DescribePlayer(__state.Player));
        }

        return __exception;
    }

    internal static void ResetPendingHydration(string reason)
    {
        if (PendingHydrations.Count == 0)
            return;

        var count = PendingHydrations.Count;
        PendingHydrations.Clear();
        Logger.Diagnostic($"[BackpackPersistence] Cancelled {count} pending 0.4.7 hydration(s): {reason}.");
    }

    internal static void CancelPendingHydration(Player player, string reason)
    {
        if (player == null || player.Pointer == IntPtr.Zero)
            return;

        if (PendingHydrations.Remove(player.Pointer))
            Logger.Diagnostic(() =>
                $"[BackpackPersistence] Cancelled pending 0.4.7 hydration for {DescribePlayer(player)}: {reason}.");
    }

    private static IEnumerator CompletePendingHydration(PendingBackpackHydration pending)
    {
        // Cross the native LoadInventory frame boundary before touching Backpack slots.
        yield return null;

        for (var attempt = 1; attempt <= MaxHydrationReadinessAttempts; attempt++)
        {
            if (!IsCurrentPending(pending))
                yield break;

            if (pending.SessionGeneration != BackpackMod.SessionGeneration)
            {
                RemovePending(pending);
                Logger.Diagnostic(() =>
                    $"[BackpackPersistence] 0.4.7 hydration cancelled for {DescribePlayer(pending.Player)} because the save session changed.");
                yield break;
            }

            if (TryGetAuthoritativeBackpackStorage(pending.Player, out var storage, out var reason))
            {
                pending.Applying = true;
                RemovePending(pending);
                try
                {
                    pending.ItemSet.LoadTo(storage.ItemSlots);
                    var (nonEmptySlots, totalQuantity) = CountBackpackContents(storage);
                    Logger.Diagnostic(() =>
                        $"[BackpackPersistence] 0.4.7 Backpack hydration completed once for {DescribePlayer(pending.Player)}; storage={DescribeStorage(storage)}, session={pending.SessionGeneration}, slots={storage.ItemSlots.Count}, nonEmpty={nonEmptySlots}, quantity={totalQuantity}, attempt={attempt}.");
                }
                catch (Exception exception)
                {
                    // Never retry after LoadTo begins: a partial application must not be applied twice.
                    Logger.Error(exception,
                        "[BackpackPersistence] 0.4.7 Backpack hydration failed during its single application for {0}; no retry was attempted.",
                        DescribePlayer(pending.Player));
                }

                yield break;
            }

            if (attempt == MaxHydrationReadinessAttempts)
            {
                RemovePending(pending);
                Logger.Warning(
                    "[BackpackPersistence] 0.4.7 Backpack hydration cancelled for {0} after {1} bounded readiness attempts: {2}.",
                    DescribePlayer(pending.Player),
                    MaxHydrationReadinessAttempts,
                    reason);
                yield break;
            }

            Logger.Diagnostic(() =>
                $"[BackpackPersistence] 0.4.7 hydration readiness attempt {attempt}/{MaxHydrationReadinessAttempts} deferred for {DescribePlayer(pending.Player)}: {reason}.");
            yield return new WaitForSeconds(HydrationRetryDelaySeconds);
        }
    }

    private static bool IsCurrentPending(PendingBackpackHydration pending)
    {
        return pending != null
               && !pending.Applying
               && PendingHydrations.TryGetValue(pending.PlayerKey, out var current)
               && ReferenceEquals(current, pending);
    }

    private static void RemovePending(PendingBackpackHydration pending)
    {
        if (pending == null)
            return;

        if (PendingHydrations.TryGetValue(pending.PlayerKey, out var current)
            && ReferenceEquals(current, pending))
            PendingHydrations.Remove(pending.PlayerKey);
    }
#else
    [HarmonyPatch("LoadInventory")]
    [HarmonyPrefix]
    public static void LoadInventory(Player __instance, ref string contentsString)
    {
        if (string.IsNullOrEmpty(contentsString))
            return;

        if (!__instance.IsOwner)
        {
            return;
        }

        var backpackString = contentsString.Split(["|||"], StringSplitOptions.None);
        if (backpackString.Length < 2)
            return;

        contentsString = backpackString[0];
        var backpackData = backpackString[1];
        try
        {
            var backpackStorage = __instance.GetBackpackStorage();
            if (!ItemSet.TryDeserialize(backpackData, out var itemSet))
            {
                Logger.Error("Failed to deserialize backpack data.");
                return;
            }

            itemSet.LoadTo(backpackStorage.ItemSlots);
        }
        catch (Exception e)
        {
            Logger.Error($"Error loading backpack data: {e.Message}");
        }
    }
#endif

    [HarmonyPatch("ExitAll")]
    [HarmonyPrefix]
    public static void ExitAll(Player __instance)
    {
        if (!__instance.IsOwner)
            return;

        PlayerBackpack.Instance?.Close();
    }

    private static bool TryGetAuthoritativeBackpackStorage(
        Player player,
        out StorageEntity storage,
        out string reason)
    {
        storage = null;
        if (player == null)
        {
            reason = "player is unavailable";
            return false;
        }

        if (!player.IsOwner)
        {
            reason = "player is not the local owner";
            return false;
        }

        try
        {
            storage = player.GetBackpackStorage();
        }
        catch (Exception exception)
        {
            reason = $"player-owned Backpack storage is unavailable ({exception.Message})";
            return false;
        }

        if (storage?.ItemSlots == null)
        {
            reason = "player-owned Backpack slots are unavailable";
            return false;
        }

        var backpack = player.LocalGameObject?.GetComponent<PlayerBackpack>();
        var storageBoundToPlayer = backpack != null && SameStorage(backpack.Storage, storage);
        if (!BackpackPersistencePolicy.CanWriteBackpack(player.IsOwner, true, storageBoundToPlayer))
        {
            reason = "Backpack component is not bound to this player's storage";
            return false;
        }

        if (storage.ItemSlots.Count <= 0)
        {
            reason = "player-owned Backpack slots are not initialized";
            return false;
        }

        reason = null;
        return true;
    }

    private static (int NonEmptySlots, long TotalQuantity) CountBackpackContents(StorageEntity storage)
    {
        if (storage?.ItemSlots == null)
            return (0, 0);

        var nonEmptySlots = 0;
        long totalQuantity = 0;
        for (var i = 0; i < storage.ItemSlots.Count; i++)
        {
#if IL2CPP
            var slot = storage.ItemSlots[new Index(i)];
#elif MONO
            var slot = storage.ItemSlots[i];
#endif
            if (slot?.ItemInstance == null)
                continue;

            nonEmptySlots++;
            totalQuantity += Math.Max(0, slot.Quantity);
        }

        return (nonEmptySlots, totalQuantity);
    }

    private static bool SameStorage(StorageEntity left, StorageEntity right)
    {
#if IL2CPP
        return left != null && right != null && left.Pointer != IntPtr.Zero && left.Pointer == right.Pointer;
#elif MONO
        return ReferenceEquals(left, right);
#endif
    }

    private static string DescribePlayer(Player player)
    {
        if (player == null)
            return "none";

#if IL2CPP
        return $"ptr=0x{player.Pointer.ToInt64():X},owner={player.IsOwner}";
#elif MONO
        return $"instance={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(player)},owner={player.IsOwner}";
#endif
    }

    private static string DescribeStorage(StorageEntity storage)
    {
        if (storage == null)
            return "none";

#if IL2CPP
        return $"0x{storage.Pointer.ToInt64():X}";
#elif MONO
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(storage).ToString();
#endif
    }

    private static string DescribeBackpackPath(string parentFolderPath)
    {
        return string.IsNullOrWhiteSpace(parentFolderPath)
            ? "<missing>"
            : Path.Combine(parentFolderPath, BackpackSaveFile + ".json");
    }

}

[HarmonyPatch(typeof(Player), "OnDied")]
public static class PlayerDiedPatch
{
    [HarmonyPrefix]
    public static void OnDied(Player __instance)
    {
        if (!__instance.Owner.IsLocalClient)
            return;

        PlayerBackpack.Instance?.SetBackpackEnabled(false);
    }
}

[HarmonyPatch(typeof(Player), "OnRevived")]
public static class PlayerRevivedPatch
{
    [HarmonyPostfix]
    public static void OnRevived(Player __instance)
    {
        if (!__instance.Owner.IsLocalClient)
            return;

        PlayerBackpack.Instance?.SetBackpackEnabled(true);
    }
}
