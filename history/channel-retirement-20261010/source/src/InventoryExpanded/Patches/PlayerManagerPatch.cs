using HarmonyLib;

#if IL2CPP && SCHEDULE_I_047
using System.Text.Json;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
#endif

#if IL2CPP
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.PlayerScripts;
#elif MONO
using ScheduleOne.Persistence.Datas;
using ScheduleOne.Persistence.Loaders;
using ScheduleOne.PlayerScripts;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(PlayerManager))]
public static class PlayerManagerPatch
{
#if IL2CPP && SCHEDULE_I_047
    [HarmonyPatch(
        nameof(PlayerManager.TryGetPlayerData),
        [typeof(string), typeof(bool), typeof(FullPlayerData)],
        [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out])]
    [HarmonyPostfix]
    public static void TryGetPlayerData(
        PlayerManager __instance,
        string __0,
        bool __result,
        bool __1,
        ref FullPlayerData __2)
    {
        if (!__result || __2?.BasicData == null)
            return;

        var playerDataCount = __instance.loadedPlayerData?.Count ?? 0;
        var playerIndex = __instance.loadedPlayerData?.IndexOf(__2.BasicData) ?? -1;
        if (!BackpackPersistencePolicy.IsValidPlayerDataIndex(playerIndex, playerDataCount))
        {
            Logger.Warning(
                "[BackpackPersistence] 0.4.7 player identity resolution failed: index={0}, playerDataCount={1}, isHost={2}.",
                playerIndex,
                playerDataCount,
                __1);
            return;
        }

        var indexedData = __instance.loadedPlayerData[new Index(playerIndex)];
        var requestedPlayerCode = __0;
        var returnedPlayerCode = __2.BasicData.PlayerCode;
        var indexedPlayerCode = indexedData?.PlayerCode;
        if (!BackpackPersistencePolicy.IsMatchingPlayerIdentity(
                requestedPlayerCode,
                returnedPlayerCode,
                indexedPlayerCode))
        {
            Logger.Warning(
                "[BackpackPersistence] 0.4.7 player path resolution rejected inconsistent player identity at index {0}; data was not read.",
                playerIndex);
            return;
        }

        if (!TryResolvePlayerDirectory(
                __instance,
                playerIndex,
                requestedPlayerCode,
                out var dataPath,
                out var pathSource,
                out var resolutionReason))
        {
            Logger.Warning(
                "[BackpackPersistence] 0.4.7 player path resolution failed closed at index {0}: {1}",
                playerIndex,
                resolutionReason);
            return;
        }

        var loader = new PlayerLoader();
        var playerPathCount = __instance.loadedPlayerDataPaths?.Count ?? 0;
        Logger.Diagnostic(() =>
            $"[BackpackPersistence] 0.4.7 player path resolved from {pathSource}; index={playerIndex}, playerDataCount={playerDataCount}, playerPathCount={playerPathCount}, isHost={__1}, playerCode='{requestedPlayerCode}', directory='{dataPath}'.");

        // In 0.4.7 the local owning player's normal load is the host request.
        // Restore locks here because Player.Load(PlayerData, string) no longer exists.
        if (__1)
        {
            if (loader.TryLoadFile(dataPath, UserSlotLockRegistry.SaveFileName, out var lockData))
                UserSlotLockRegistry.LoadPersistent(lockData);
            else
                UserSlotLockRegistry.LoadPersistent(null);
        }

        var backpackFilePath = Path.Combine(dataPath, "Backpack.json");
        if (!File.Exists(backpackFilePath))
        {
            Logger.Diagnostic(() =>
                $"[BackpackPersistence] No persisted 0.4.7 Backpack exists for player index {playerIndex}; fresh-save load continues without a payload. path='{backpackFilePath}'.");
            return;
        }

        if (!loader.TryLoadFile(dataPath, "Backpack", out var backpackString)
            || string.IsNullOrWhiteSpace(backpackString))
        {
            Logger.Warning(
                "[BackpackPersistence] 0.4.7 Backpack file exists but could not be read safely at '{0}'; no payload was appended.",
                backpackFilePath);
            return;
        }

        __2.InventoryString += "|||" + backpackString;
        Logger.Diagnostic(() =>
            $"[BackpackPersistence] 0.4.7 Backpack payload appended for player index {playerIndex}; path='{backpackFilePath}', payloadLength={backpackString.Length}.");
    }

    private static bool TryResolvePlayerDirectory(
        PlayerManager playerManager,
        int playerIndex,
        string expectedPlayerCode,
        out string playerDirectory,
        out string source,
        out string reason)
    {
        playerDirectory = null;
        source = null;

        var pathCount = playerManager.loadedPlayerDataPaths?.Count ?? 0;
        string indexedPath = playerIndex >= 0 && playerIndex < pathCount
            ? playerManager.loadedPlayerDataPaths[new Index(playerIndex)]
            : null;
        string indexedFileName = playerManager.loadedPlayerFileNames != null
                                 && playerIndex >= 0
                                 && playerIndex < playerManager.loadedPlayerFileNames.Count
            ? playerManager.loadedPlayerFileNames[new Index(playerIndex)]
            : null;
        var loadManager = Singleton<LoadManager>.Instance;
        var activeSaveRoot = loadManager?.LoadedGameFolderPath;
        string playersRoot;
        try
        {
            playersRoot = string.IsNullOrWhiteSpace(activeSaveRoot)
                ? null
                : Path.Combine(activeSaveRoot, "Players");
        }
        catch (Exception exception)
        {
            reason = $"active LoadManager save root could not be inspected safely: {exception.GetType().Name}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(playersRoot)
            && Directory.Exists(playersRoot)
            && BackpackPersistencePolicy.IsPathWithinRoot(indexedPath, playersRoot)
            && TryValidatePlayerDirectory(indexedPath, expectedPlayerCode, out _))
        {
            playerDirectory = Path.GetFullPath(indexedPath);
            source = "loadedPlayerDataPaths";
            reason = null;
            return true;
        }

        var directReason = DescribeDirectPathRejection(indexedPath, expectedPlayerCode, playersRoot);
        Logger.Diagnostic(() =>
            $"[BackpackPersistence] 0.4.7 indexed player path was transiently unusable; index={playerIndex}, indexedPath='{indexedPath ?? "<missing>"}', indexedFileName='{indexedFileName ?? "<missing>"}', directReason='{directReason}', loadedPaths={DescribeLoadedPaths(playerManager)}, activeSaveRoot='{activeSaveRoot ?? "<missing>"}'.");

        if (string.IsNullOrWhiteSpace(activeSaveRoot) || !Directory.Exists(activeSaveRoot))
        {
            reason = $"active LoadManager save root is unavailable ('{activeSaveRoot ?? "<missing>"}'); indexed path was rejected because {directReason}";
            return false;
        }

        playersRoot = Path.Combine(activeSaveRoot, "Players");
        if (!Directory.Exists(playersRoot))
        {
            reason = $"authoritative Players directory is unavailable ('{playersRoot}'); indexed path was rejected because {directReason}";
            return false;
        }

        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(playersRoot);
        }
        catch (Exception exception)
        {
            reason = $"authoritative Players directory could not be enumerated safely ('{playersRoot}'): {exception.GetType().Name}";
            return false;
        }

        var matches = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!BackpackPersistencePolicy.IsPathWithinRoot(candidate, playersRoot))
                continue;

            if (!string.IsNullOrWhiteSpace(indexedFileName)
                && !string.Equals(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(candidate)),
                    indexedFileName,
                    StringComparison.Ordinal))
                continue;

            if (TryValidatePlayerDirectory(candidate, expectedPlayerCode, out _))
                matches.Add(Path.GetFullPath(candidate));
        }

        if (matches.Count != 1)
        {
            var backpackCandidates = candidates.Count(candidate =>
                BackpackPersistencePolicy.IsPathWithinRoot(candidate, playersRoot)
                && File.Exists(Path.Combine(candidate, "Backpack.json")));
            reason = $"authoritative save root produced {matches.Count} exact PlayerCode matches among {candidates.Length} player director(ies), with {backpackCandidates} Backpack file(s); indexed path was rejected because {directReason}";
            return false;
        }

        playerDirectory = matches[0];
        source = "LoadManager.LoadedGameFolderPath + exact Player.json identity";
        reason = null;
        return true;
    }

    private static string DescribeDirectPathRejection(
        string indexedPath,
        string expectedPlayerCode,
        string playersRoot)
    {
        if (string.IsNullOrWhiteSpace(playersRoot) || !Directory.Exists(playersRoot))
            return $"the active Players root is unavailable ('{playersRoot ?? "<missing>"}')";

        if (!BackpackPersistencePolicy.IsPathWithinRoot(indexedPath, playersRoot))
            return $"the indexed path is not inside the active Players root ('{indexedPath ?? "<missing>"}')";

        return TryValidatePlayerDirectory(indexedPath, expectedPlayerCode, out var reason)
            ? "the indexed path was unexpectedly rejected"
            : reason;
    }

    private static bool TryValidatePlayerDirectory(
        string directory,
        string expectedPlayerCode,
        out string reason)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            reason = "the directory is empty";
            return false;
        }

        if (!Directory.Exists(directory))
        {
            reason = $"the directory does not exist ('{directory}')";
            return false;
        }

        var playerFile = Path.Combine(directory, "Player.json");
        if (!File.Exists(playerFile))
        {
            reason = $"Player.json is absent ('{playerFile}')";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(playerFile));
            if (!document.RootElement.TryGetProperty("PlayerCode", out var playerCodeElement))
            {
                reason = $"Player.json has no PlayerCode ('{playerFile}')";
                return false;
            }

            var actualPlayerCode = playerCodeElement.GetString();
            if (!BackpackPersistencePolicy.IsMatchingPlayerFileIdentity(expectedPlayerCode, actualPlayerCode))
            {
                reason = $"Player.json identity mismatch: expected '{expectedPlayerCode}', found '{actualPlayerCode ?? "<missing>"}' ('{playerFile}')";
                return false;
            }

            reason = null;
            return true;
        }
        catch (Exception exception)
        {
            reason = $"Player.json could not be inspected safely ('{playerFile}'): {exception.GetType().Name}";
            return false;
        }
    }

    private static string DescribeLoadedPaths(PlayerManager playerManager)
    {
        if (playerManager.loadedPlayerDataPaths == null)
            return "<null>";

        var paths = new List<string>();
        for (var index = 0; index < playerManager.loadedPlayerDataPaths.Count; index++)
        {
            var value = playerManager.loadedPlayerDataPaths[new Index(index)];
            paths.Add($"{index}:'{value ?? "<missing>"}'");
        }

        return "[" + string.Join(", ", paths) + "]";
    }
#else
    [HarmonyPatch(nameof(PlayerManager.TryGetPlayerData))]
    [HarmonyPostfix]
    public static void TryGetPlayerData(PlayerManager __instance, PlayerData data, ref string inventoryString)
    {
        if (data == null)
            return;

        var dataPath = __instance.loadedPlayerDataPaths[__instance.loadedPlayerData.IndexOf(data)];
        var loader = new PlayerLoader();
        if (!loader.TryLoadFile(dataPath, "Backpack", out var backpackString))
        {
            Logger.Warning("Failed to load player backpack under " + dataPath);
            return;
        }

        inventoryString += "|||" + backpackString;
    }
#endif
}
