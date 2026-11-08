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
        var isOwner = __instance.IsOwner;
        if (isOwner)
        {
            if (TryGetAuthoritativeBackpackStorage(__instance, out var ownerStorage, out var ownerReason))
                WriteBackpackData(__instance, parentFolderPath, ownerStorage, true, 0, null);
            else
                Logger.Warning(
                    "[BackpackPersistence] Backpack write skipped for authoritative player {0}: {1}. Existing persistence was left untouched.",
                    DescribePlayer(__instance),
                    ownerReason);

            var lockData = UserSlotLockRegistry.SerializePersistent();
#if IL2CPP
            __instance.Cast<ISaveable>().WriteSubfile(parentFolderPath, UserSlotLockRegistry.SaveFileName, lockData);
#elif MONO
            ISaveable instance = __instance;
            instance.WriteSubfile(parentFolderPath, UserSlotLockRegistry.SaveFileName, lockData);
#endif
            return;
        }

#if MULTIPLAYER_PERSISTENCE
        if (!MultiplayerBackpackPersistence.TryGetReplicatedBackpackStorage(
                __instance,
                out var initialStorage,
                out _,
                out var snapshotWriteLock,
                out var initialReason))
        {
            Logger.Diagnostic(() =>
                $"[BackpackPersistence] Guest Backpack write skipped for {DescribePlayer(__instance)}: {initialReason}. Existing persistence was left untouched.");
            return;
        }

        lock (snapshotWriteLock)
        {
            if (!MultiplayerBackpackPersistence.TryGetReplicatedBackpackStorage(
                    __instance,
                    out var backpackStorage,
                    out var snapshotVersion,
                    out var confirmedWriteLock,
                    out var storageReason)
                || !ReferenceEquals(snapshotWriteLock, confirmedWriteLock)
                || backpackStorage.Pointer != initialStorage.Pointer)
            {
                Logger.Diagnostic(() =>
                    $"[BackpackPersistence] Guest Backpack write skipped for {DescribePlayer(__instance)} because its dirty replica changed before the write lock was acquired.");
                return;
            }

            if (!MultiplayerBackpackPersistence.TryResolveCandidateBackpackPath(
                    __instance,
                    parentFolderPath,
                    out var candidateBackpackPath,
                    out var pathReason))
            {
                Logger.Warning(
                    "[BackpackPersistence] Guest Backpack write failed closed for {0}: {1}. Existing sidecar was left untouched.",
                    DescribePlayer(__instance),
                    pathReason);
                return;
            }

            WriteBackpackData(
                __instance,
                parentFolderPath,
                backpackStorage,
                false,
                snapshotVersion,
                candidateBackpackPath);
        }

        return;
#else
#if MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
            Logger.Diagnostic(() =>
                $"[BackpackPersistence] Write skipped for non-owner player {DescribePlayer(__instance)}; candidateTarget='{BackpackPersistenceDiagnostics.ResolveCandidateBackpackPath(__instance, parentFolderPath)}'.");
#else
            Logger.Diagnostic(() =>
                $"[BackpackPersistence] Write skipped for non-owner player {DescribePlayer(__instance)}; candidateTarget='{DescribeBackpackPath(__instance, parentFolderPath)}'.");
#endif
#if MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
            BackpackPersistenceDiagnostics.LogBackpackWriteSkipped(
                __instance,
                parentFolderPath,
                "non-owner guard returned before local Backpack storage was inspected");
#endif
            return;
#endif
    }

    private static void WriteBackpackData(
        Player player,
        string parentFolderPath,
        StorageEntity backpackStorage,
        bool isOwner,
        long replicatedSnapshotVersion,
        string candidateBackpackPath)
    {
        try
        {
            var contents = new ItemSet(backpackStorage.ItemSlots).GetJSON();
            var (nonEmptySlots, totalQuantity) = CountBackpackContents(backpackStorage);
#if IL2CPP
            if (isOwner)
            {
                player.Cast<ISaveable>().WriteSubfile(parentFolderPath, BackpackSaveFile, contents);
                Logger.Diagnostic(() =>
                    $"[BackpackPersistence] Backpack written for {DescribePlayer(player)}; storage={DescribeStorage(backpackStorage)}, target='{DescribeBackpackPath(player, parentFolderPath)}', slots={backpackStorage.ItemSlots.Count}, nonEmpty={nonEmptySlots}, quantity={totalQuantity}, payloadLength={contents?.Length ?? 0}.");
            }
            else
            {
                if (!AtomicTextFileWriter.TryWrite(candidateBackpackPath, contents, out var writeReason))
                {
                    Logger.Warning(
                        "[BackpackPersistence] Guest Backpack sidecar write could not be committed and verified for {0}; the dirty marker remains set: {1}.",
                        DescribePlayer(player),
                        writeReason);
                    return;
                }

                if (!MultiplayerBackpackPersistence.MarkPersisted(
                        player,
                        backpackStorage,
                        replicatedSnapshotVersion))
                {
                    Logger.Warning(
                        "[BackpackPersistence] Guest Backpack sidecar was verified for {0}, but its save-session marker changed; a later save will retry.",
                        DescribePlayer(player));
                    return;
                }

                Logger.Diagnostic(() =>
                    $"[BackpackPersistence] Guest Backpack snapshot verified for {DescribePlayer(player)}; sessionVersion={replicatedSnapshotVersion}, slots={backpackStorage.ItemSlots.Count}, nonEmpty={nonEmptySlots}, quantity={totalQuantity}, contentsLogged=false.");
            }
#elif MONO
            ISaveable instance = player;
            instance.WriteSubfile(parentFolderPath, BackpackSaveFile, contents);
#endif
        }
        catch (Exception exception)
        {
            Logger.Error(exception,
                "[BackpackPersistence] Failed to write authoritative Backpack for {0}.",
                DescribePlayer(player));
        }
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
                if (string.Equals(reason, "player is not the local owner", StringComparison.Ordinal))
                {
                    Logger.Diagnostic(
                        "[BackpackPersistence] 0.4.7 remote-player hydration was not applied for {0} after {1} bounded readiness attempts; the local-owner guard remains in force.",
                        DescribePlayer(pending.Player),
                        MaxHydrationReadinessAttempts);
                }
                else
                {
                    Logger.Warning(
                        "[BackpackPersistence] 0.4.7 Backpack hydration cancelled for {0} after {1} bounded readiness attempts: {2}.",
                        DescribePlayer(pending.Player),
                        MaxHydrationReadinessAttempts,
                        reason);
                }
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
#if MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
        BackpackPersistenceDiagnostics.LogPlayerEvent(
            "player-exit-all",
            __instance,
            "method=Player.ExitAll;disconnectOrSceneTransition=not-inferred.");
#endif
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

#if MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
        return $"{BackpackPersistenceDiagnostics.DescribeRole()} {BackpackPersistenceDiagnostics.DescribePlayer(player)}";
#else
#if IL2CPP
        return $"ptr=0x{player.Pointer.ToInt64():X},owner={player.IsOwner}";
#elif MONO
        return $"instance={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(player)},owner={player.IsOwner}";
#endif
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

    private static string DescribeBackpackPath(Player player, string parentFolderPath)
    {
        if (string.IsNullOrWhiteSpace(parentFolderPath))
            return "<missing parent folder>";

        try
        {
#if IL2CPP
            var saveable = player?.Cast<ISaveable>();
#elif MONO
            ISaveable saveable = player;
#endif
            var folderName = saveable?.SaveFolderName;
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                if (Path.IsPathRooted(folderName)
                    || !string.Equals(Path.GetFileName(folderName), folderName, StringComparison.Ordinal)
                    || folderName is "." or "..")
                    return "<invalid native SaveFolderName>";

                parentFolderPath = Path.Combine(parentFolderPath, folderName);
            }

            return Path.Combine(parentFolderPath, BackpackSaveFile + ".json");
        }
        catch (Exception exception)
        {
            return $"<native save path unavailable ({exception.GetType().Name})>";
        }
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
