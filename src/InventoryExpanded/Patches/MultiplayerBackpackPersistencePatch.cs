#if MULTIPLAYER_PERSISTENCE
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;

namespace Backpack.Patches;

/// <summary>
/// Persists the host's native, server-side StorageEntity replica for a guest Player.
/// No client snapshot or new network message is introduced.
/// </summary>
internal static class MultiplayerBackpackPersistence
{
    private sealed class ObservedStorageState
    {
        public ObservedStorageState(string playerCode, int sessionGeneration, int resetGeneration)
        {
            PlayerCode = playerCode;
            SessionGeneration = sessionGeneration;
            ResetGeneration = resetGeneration;
        }

        public string PlayerCode { get; }
        public int SessionGeneration { get; }
        public int ResetGeneration { get; }
        public object SnapshotWriteLock { get; } = new();
        public long Version { get; set; }
        public long PersistedVersion { get; set; }
    }

    private static readonly object StateLock = new();
    private static readonly Dictionary<IntPtr, ObservedStorageState> ObservedStates = new();
    private static int _resetGeneration;

    public static bool TryGetReplicatedBackpackStorage(
        Player player,
        out StorageEntity storage,
        out long snapshotVersion,
        out object snapshotWriteLock,
        out string reason)
    {
        storage = null;
        snapshotVersion = 0;
        snapshotWriteLock = null;
        try
        {
            if (!TryValidateHostStorage(player, out var candidate, out reason))
                return false;

            var playerCode = player.PlayerCode;
            var sessionGeneration = BackpackMod.SessionGeneration;
            var observedMutation = false;
            lock (StateLock)
            {
                if (ObservedStates.TryGetValue(candidate.Pointer, out var state)
                    && state.SessionGeneration == sessionGeneration
                    && state.ResetGeneration == _resetGeneration
                    && string.Equals(state.PlayerCode, playerCode, StringComparison.Ordinal)
                    && BackpackPersistencePolicy.IsCurrentDirtySnapshot(
                        state.Version,
                        state.PersistedVersion,
                        state.Version))
                {
                    observedMutation = true;
                    snapshotVersion = state.Version;
                    snapshotWriteLock = state.SnapshotWriteLock;
                }
            }

            var identityMatches = !string.IsNullOrWhiteSpace(playerCode);
            var storageBoundToPlayer = IsStorageBoundToPlayer(player, candidate);
            var storageIsBackpack = string.Equals(
                candidate.StorageEntityName,
                PlayerBackpack.StorageName,
                StringComparison.Ordinal);
            var storageIsServerInitialized = candidate.IsServerInitialized;
            var storageHasSlots = candidate.ItemSlots != null && candidate.ItemSlots.Count > 0;
            if (!BackpackPersistencePolicy.CanWriteReplicatedBackpack(
                    isHostServer: true,
                    playerIsOwner: player.IsOwner,
                    playerIdentityMatches: identityMatches,
                    storageBoundToPlayer: storageBoundToPlayer,
                    storageIsBackpack: storageIsBackpack,
                    storageIsServerInitialized: storageIsServerInitialized,
                    storageHasSlots: storageHasSlots,
                    serverMutationObserved: observedMutation))
            {
                reason = observedMutation
                    ? "host-side guest storage failed identity, ownership, Backpack-name, network-object, or readiness validation"
                    : "no unpersisted server-side Backpack mutation was observed for this guest in the current save session";
                Trace("guest-backpack-snapshot-skipped", player, $"reason='{reason}',session={sessionGeneration}.");
                snapshotWriteLock = null;
                return false;
            }

            storage = candidate;
            reason = null;
            Trace("guest-backpack-snapshot-ready", player,
                $"session={sessionGeneration},snapshotVersion={snapshotVersion},source=host-server-StorageEntity.");
            return true;
        }
        catch (Exception exception)
        {
            storage = null;
            snapshotVersion = 0;
            snapshotWriteLock = null;
            reason = $"host-side guest storage inspection failed safely ({exception.GetType().Name})";
            Logger.Warning("[BackpackPersistence] Guest server replica inspection failed closed: {0}.",
                exception.GetType().Name);
            return false;
        }
    }

    public static bool MarkPersisted(
        Player player,
        StorageEntity storage,
        long snapshotVersion)
    {
        try
        {
            if (player == null || storage == null || storage.Pointer == IntPtr.Zero)
                return false;

            var playerCode = player.PlayerCode;
            var sessionGeneration = BackpackMod.SessionGeneration;
            lock (StateLock)
            {
                if (!ObservedStates.TryGetValue(storage.Pointer, out var state)
                    || state.SessionGeneration != sessionGeneration
                    || state.ResetGeneration != _resetGeneration
                    || !string.Equals(state.PlayerCode, playerCode, StringComparison.Ordinal))
                    return false;

                if (!BackpackPersistencePolicy.CanAcknowledgeSnapshot(
                        state.Version,
                        state.PersistedVersion,
                        snapshotVersion))
                    return false;

                state.PersistedVersion = Math.Max(state.PersistedVersion, snapshotVersion);
            }

            Trace("guest-backpack-snapshot-write-verified", player,
                $"session={sessionGeneration},snapshotVersion={snapshotVersion},sidecarReadbackMatched=true,pathBoundToPlayer=true.");
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warning("[BackpackPersistence] Guest server snapshot could not be marked persisted; it remains eligible for retry: {0}.",
                exception.GetType().Name);
            return false;
        }
    }

    public static bool TryResolveCandidateBackpackPath(
        Player player,
        string parentFolderPath,
        out string candidatePath,
        out string reason)
    {
        candidatePath = null;
        try
        {
            if (player == null || player.Pointer == IntPtr.Zero || player.IsOwner)
            {
                reason = "target is unavailable or is not a remote player";
                return false;
            }

            var playerCode = player.PlayerCode;
            if (string.IsNullOrWhiteSpace(playerCode))
            {
                reason = "target Player has no host-resolved identity";
                return false;
            }

            if (string.IsNullOrWhiteSpace(parentFolderPath))
            {
                reason = "save callback did not provide a parent directory";
                return false;
            }

            var saveable = player.Cast<ISaveable>();
            if (saveable == null || !saveable.ShouldSaveUnderFolder || string.IsNullOrWhiteSpace(saveable.SaveFolderName))
            {
                reason = "Player save folder metadata is unavailable";
                return false;
            }

            var folderName = saveable.SaveFolderName;
            if (Path.IsPathRooted(folderName)
                || !string.Equals(Path.GetFileName(folderName), folderName, StringComparison.Ordinal)
                || folderName is "." or "..")
            {
                reason = "Player save folder name is not a simple child name";
                return false;
            }

            var loadedGameFolder = Singleton<LoadManager>.Instance?.LoadedGameFolderPath;
            if (string.IsNullOrWhiteSpace(loadedGameFolder))
            {
                reason = "active save directory is unavailable";
                return false;
            }

            var playersRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(loadedGameFolder, "Players")));
            var parentDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentFolderPath));
            var playerDirectory = Path.GetFullPath(Path.Combine(parentDirectory, folderName));
            if (!BackpackPersistencePolicy.IsImmediateChildOfRoot(playerDirectory, playersRoot))
            {
                reason = "resolved Player directory is not an immediate child of the active save Players directory";
                return false;
            }

            if (!string.Equals(parentDirectory, playersRoot, StringComparison.OrdinalIgnoreCase))
            {
                reason = "save callback parent does not match the active save Players directory";
                return false;
            }

            if (!Directory.Exists(playersRoot) || !Directory.Exists(playerDirectory))
            {
                reason = "active Players or target Player directory is unavailable";
                return false;
            }

            var playerFile = Path.Combine(playerDirectory, "Player.json");
            if (!File.Exists(playerFile))
            {
                reason = "target Player identity file is unavailable";
                return false;
            }

            using (var document = JsonDocument.Parse(File.ReadAllText(playerFile)))
            {
                if (!document.RootElement.TryGetProperty("PlayerCode", out var identityElement)
                    || !BackpackPersistencePolicy.IsMatchingPlayerFileIdentity(
                        playerCode,
                        identityElement.GetString()))
                {
                    reason = "target Player directory identity does not match the host-resolved Player";
                    return false;
                }
            }

            candidatePath = Path.Combine(playerDirectory, "Backpack.json");
            if (!BackpackPersistencePolicy.IsPathWithinRoot(candidatePath, playersRoot))
            {
                candidatePath = null;
                reason = "resolved Backpack sidecar escaped the active Players directory";
                return false;
            }

            reason = null;
            return true;
        }
        catch (Exception exception)
        {
            candidatePath = null;
            reason = $"Player-specific save path validation failed safely ({exception.GetType().Name})";
            return false;
        }
    }

    public static void ObserveServerMutation(StorageEntity storage)
    {
        if (storage == null || storage.Pointer == IntPtr.Zero || !IsHostLobbyServer())
            return;

        try
        {
            var player = storage.gameObject.GetComponent<Player>();
            if (!TryValidateHostStorage(player, out var boundStorage, out _)
                || boundStorage.Pointer != storage.Pointer)
                return;

            var playerCode = player.PlayerCode;
            if (string.IsNullOrWhiteSpace(playerCode))
                return;

            var sessionGeneration = BackpackMod.SessionGeneration;
            ObservedStorageState state;
            lock (StateLock)
            {
                if (ObservedStates.TryGetValue(storage.Pointer, out var existing)
                    && existing.SessionGeneration == sessionGeneration
                    && existing.ResetGeneration == _resetGeneration
                    && string.Equals(existing.PlayerCode, playerCode, StringComparison.Ordinal))
                {
                    // Reuse the state object so save/write and mutation callbacks share a gate.
                }
                else
                {
                    state = new ObservedStorageState(playerCode, sessionGeneration, _resetGeneration);
                    ObservedStates[storage.Pointer] = state;
                }

                state = ObservedStates[storage.Pointer];
            }

            lock (state.SnapshotWriteLock)
            {
                var enteredDirtyState = false;
                lock (StateLock)
                {
                    if (!ObservedStates.TryGetValue(storage.Pointer, out var current)
                        || !ReferenceEquals(current, state)
                        || state.SessionGeneration != BackpackMod.SessionGeneration
                        || state.ResetGeneration != _resetGeneration
                        || !string.Equals(state.PlayerCode, playerCode, StringComparison.Ordinal))
                        return;

                    enteredDirtyState = state.Version <= state.PersistedVersion;
                    state.Version++;
                }

                if (enteredDirtyState)
                    Trace("guest-backpack-dirty", player,
                        $"session={sessionGeneration},snapshotVersion={state.Version},mutationObserved=true.");
            }
        }
        catch (Exception exception)
        {
            Logger.Warning("[BackpackPersistence] Guest server-state observation failed safely: {0}.",
                exception.GetType().Name);
        }
    }

    public static void Forget(StorageEntity storage)
    {
        if (storage == null || storage.Pointer == IntPtr.Zero)
            return;

        ObservedStorageState state;
        lock (StateLock)
        {
            if (!ObservedStates.TryGetValue(storage.Pointer, out state))
                return;
        }

        lock (state.SnapshotWriteLock)
        {
            lock (StateLock)
            {
                if (ObservedStates.TryGetValue(storage.Pointer, out var current)
                    && ReferenceEquals(current, state))
                    ObservedStates.Remove(storage.Pointer);
            }
        }
    }

    public static void ResetSession(string reason)
    {
        int count;
        ObservedStorageState[] states;
        lock (StateLock)
        {
            count = ObservedStates.Count;
            states = ObservedStates.Values.ToArray();
            _resetGeneration++;
            ObservedStates.Clear();
        }

        foreach (var state in states)
        {
            lock (state.SnapshotWriteLock)
            {
                // Wait for any in-flight verified write before the next session begins.
            }
        }

        Logger.Diagnostic($"[BackpackPersistence] Cleared {count} guest server-state marker(s): {reason}.");
    }

    private static bool TryValidateHostStorage(Player player, out StorageEntity storage, out string reason)
    {
        storage = null;
        try
        {
            if (!IsHostLobbyServer())
            {
                reason = "process is not the active lobby host server";
                return false;
            }

            if (player == null || player.Pointer == IntPtr.Zero)
            {
                reason = "target guest Player is unavailable";
                return false;
            }

            if (player.IsOwner)
            {
                reason = "target Player is the local owner; owner persistence uses the existing path";
                return false;
            }

            if (string.IsNullOrWhiteSpace(player.PlayerCode))
            {
                reason = "target guest has no authenticated PlayerCode";
                return false;
            }

            storage = player.GetBackpackStorage();
            if (storage == null || storage.Pointer == IntPtr.Zero)
            {
                reason = "target guest StorageEntity is unavailable";
                storage = null;
                return false;
            }

            if (!player.IsServerInitialized || !storage.IsServerInitialized)
            {
                reason = "target Player or Backpack StorageEntity is not server-initialized";
                storage = null;
                return false;
            }

            if (!IsStorageBoundToPlayer(player, storage))
            {
                reason = "Backpack StorageEntity does not share the target Player network object";
                storage = null;
                return false;
            }

            if (!string.Equals(storage.StorageEntityName, PlayerBackpack.StorageName, StringComparison.Ordinal))
            {
                reason = "target StorageEntity is not named Backpack";
                storage = null;
                return false;
            }

            if (storage.ItemSlots == null || storage.ItemSlots.Count <= 0)
            {
                reason = "host-side Backpack slot list is not initialized";
                storage = null;
                return false;
            }

            reason = null;
            return true;
        }
        catch (Exception exception)
        {
            reason = $"host-side guest StorageEntity validation failed safely ({exception.GetType().Name})";
            storage = null;
            return false;
        }
    }

    private static bool IsStorageBoundToPlayer(Player player, StorageEntity storage)
    {
        if (player == null || storage == null)
            return false;

        var attachedStorage = player.gameObject.GetComponent<StorageEntity>();
        var playerNetworkObject = player.NetworkObject;
        var storageNetworkObject = storage.NetworkObject;
        return attachedStorage != null
               && attachedStorage.Pointer == storage.Pointer
               && playerNetworkObject != null
               && storageNetworkObject != null
               && playerNetworkObject.Pointer != IntPtr.Zero
               && playerNetworkObject.Pointer == storageNetworkObject.Pointer;
    }

    private static bool IsHostLobbyServer()
    {
        try
        {
            var lobby = Lobby.Instance;
            return InstanceFinder.IsServer && lobby != null && lobby.IsInLobby && lobby.IsHost;
        }
        catch
        {
            return false;
        }
    }

    private static void Trace(string eventName, Player player, string details)
    {
        Logger.Diagnostic(() =>
            $"[BackpackPersistence] event={eventName},playerKey={SafePlayerKey(player)},owner={SafeIsOwner(player)},{details}contentsLogged=false.");
    }

    private static string SafePlayerKey(Player player)
    {
        try
        {
            var playerCode = player?.PlayerCode;
            if (string.IsNullOrWhiteSpace(playerCode))
                return "unavailable";

            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(playerCode));
            return Convert.ToHexString(digest.AsSpan(0, 6));
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string SafeIsOwner(Player player)
    {
        try
        {
            return player == null ? "unknown" : player.IsOwner.ToString();
        }
        catch
        {
            return "unknown";
        }
    }
}

[HarmonyPatch(typeof(StorageEntity), "SetStoredInstance_Internal", typeof(NetworkConnection), typeof(int), typeof(Il2CppScheduleOne.ItemFramework.ItemInstance))]
internal static class MultiplayerBackpackStoredInstanceMutationPatch
{
    [HarmonyPostfix]
    private static void Postfix(StorageEntity __instance)
    {
        MultiplayerBackpackPersistence.ObserveServerMutation(__instance);
    }
}

[HarmonyPatch(typeof(StorageEntity), "SetItemSlotQuantity_Internal", typeof(int), typeof(int))]
internal static class MultiplayerBackpackQuantityMutationPatch
{
    [HarmonyPostfix]
    private static void Postfix(StorageEntity __instance)
    {
        MultiplayerBackpackPersistence.ObserveServerMutation(__instance);
    }
}

[HarmonyPatch(typeof(StorageEntity), "OnDestroy")]
internal static class MultiplayerBackpackStorageDestroyPatch
{
    [HarmonyPostfix]
    private static void Postfix(StorageEntity __instance)
    {
        try
        {
            MultiplayerBackpackPersistence.Forget(__instance);
        }
        catch (Exception exception)
        {
            Logger.Warning("[BackpackPersistence] Guest storage cleanup failed safely: {0}.",
                exception.GetType().Name);
        }
    }
}
#endif
