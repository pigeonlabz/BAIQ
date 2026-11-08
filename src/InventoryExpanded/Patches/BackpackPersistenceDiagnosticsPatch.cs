#if MULTIPLAYER_PERSISTENCE_DIAGNOSTICS
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;

namespace Backpack.Patches;

/// <summary>
/// Opt-in, read-only tracing for the MAIN multiplayer Backpack persistence investigation.
/// It observes ordinary save/load methods and never patches generated RPC handlers.
/// </summary>
internal static class BackpackPersistenceDiagnostics
{
    private static int _eventSequence;

    public static string IdentityKey(string playerCode)
    {
        if (string.IsNullOrWhiteSpace(playerCode))
            return "unavailable";

        try
        {
            using var sha = SHA256.Create();
            var normalizedCode = playerCode.Trim().ToUpperInvariant();
            var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedCode));
            return Convert.ToHexString(digest.AsSpan(0, 6));
        }
        catch
        {
            return "unavailable";
        }
    }

    public static string DescribePlayer(Player player)
    {
        if (player == null)
            return "player=none";

        try
        {
            var pointer = player.Pointer == IntPtr.Zero
                ? "none"
                : $"0x{player.Pointer.ToInt64():X}";
            var identity = IdentityKey(player.PlayerCode);
            var owner = player.IsOwner;
            return $"playerKey={identity},playerPtr={pointer},owner={owner}";
        }
        catch (Exception exception)
        {
            return $"playerKey=unavailable,playerDescribeError={exception.GetType().Name}";
        }
    }

    public static string DescribeRole()
    {
        try
        {
            var isServer = InstanceFinder.IsServer;
            var isClient = InstanceFinder.IsClient;
            var networkRole = isServer && isClient
                ? "host"
                : isServer
                    ? "server-only"
                    : isClient
                        ? "client-only"
                        : "offline-or-not-started";

            var lobby = Lobby.Instance;
            var lobbyRole = lobby == null || !lobby.IsInLobby
                ? "not-in-lobby"
                : lobby.IsHost
                    ? "host"
                    : "guest";

            return $"networkRole={networkRole},lobbyRole={lobbyRole}";
        }
        catch (Exception exception)
        {
            return $"networkRole=unavailable,lobbyRole=unavailable,roleError={exception.GetType().Name}";
        }
    }

    public static void LogPlayerEvent(string eventName, Player player, string details = "")
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        try
        {
            var sequence = Interlocked.Increment(ref _eventSequence);
            Logger.Diagnostic(
                $"[BackpackSaveTrace] seq={sequence} event={eventName} {DescribeRole()} {DescribePlayer(player)} {details}");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[BackpackSaveTrace] event={eventName} diagnostic failed safely: {exception.GetType().Name}.");
        }
    }

    public static void LogLifecycleEvent(string eventName, int sessionGeneration, string sceneName)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        try
        {
            var sequence = Interlocked.Increment(ref _eventSequence);
            Logger.Diagnostic(
                $"[BackpackSaveTrace] seq={sequence} event={eventName} {DescribeRole()} session={sessionGeneration} scene='{sceneName ?? "<missing>"}'.");
        }
        catch (Exception exception)
        {
            Logger.Warning($"[BackpackSaveTrace] event={eventName} diagnostic failed safely: {exception.GetType().Name}.");
        }
    }

    public static void LogPlayerDataLookup(
        string requestedPlayerCode,
        string returnedPlayerCode,
        bool isHostRequest,
        bool succeeded,
        bool hasBasicData)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        LogPlayerEvent(
            "player-data-lookup",
            null,
            $"requestedPlayerKey={IdentityKey(requestedPlayerCode)},returnedPlayerKey={IdentityKey(returnedPlayerCode)},managerIsHost={isHostRequest},success={succeeded},hasBasicData={hasBasicData}.");
    }

    public static void LogPlayerWriteBegin(Player player, string parentFolderPath)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        LogPlayerEvent(
            "player-write-begin",
            player,
            $"parentPath='{parentFolderPath ?? "<missing>"}',candidateBackpackPath='{ResolveCandidateBackpackPath(player, parentFolderPath)}',storageProbe=deferred-to-BAIQ-writer.");
    }

    public static void LogPlayerWriteException(Player player, Exception exception)
    {
        if (!Logger.DiagnosticLoggingEnabled || exception == null)
            return;

        LogPlayerEvent(
            "player-write-exception",
            player,
            $"exceptionType={exception.GetType().Name};nativeWriteCompletion=failed-or-threw.");
    }

    public static void LogBackpackWriteSkipped(
        Player player,
        string parentFolderPath,
        string reason,
        string storageProbeOverride = null)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        var storageProbe = storageProbeOverride ?? "not-performed-owner-guard";
        try
        {
            if (storageProbeOverride == null && player != null && player.IsOwner)
                storageProbe = "performed";
        }
        catch
        {
            storageProbe = "unavailable";
        }

        LogPlayerEvent(
            "backpack-write-skipped",
            player,
            $"reason='{Trim(reason)}',candidateBackpackPath='{ResolveCandidateBackpackPath(player, parentFolderPath)}',storageProbe={storageProbe}.");
    }

    public static void LogBackpackWriteResult(
        Player player,
        StorageEntity storage,
        string writerReturnedPath,
        string candidatePath,
        int slots,
        int nonEmptySlots,
        long totalQuantity)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        var fileState = DescribeFileState(candidatePath);
        LogPlayerEvent(
            "backpack-write-result",
            player,
            $"outcome={(writerReturnedPath == null ? "no-path-returned" : "write-subfile-returned")},writerReturnedPath='{writerReturnedPath ?? "<no-path-returned>"}',candidateBackpackPath='{candidatePath ?? "<unresolved>"}',{fileState},storagePtr={DescribeStorage(storage)},slots={slots},nonEmptySlots={nonEmptySlots},totalQuantity={totalQuantity},contentsLogged=false.");
    }

    public static void LogPlayerDataPath(
        string playerCode,
        bool isHostRequest,
        int playerIndex,
        string source,
        string playerDirectory,
        string backpackPath,
        bool backpackExists)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        string fileState = backpackExists ? DescribeFileState(backpackPath) : "fileExists=false";
        LogPlayerEvent(
            "player-data-path-resolved",
            null,
            $"playerKey={IdentityKey(playerCode)},managerIsHost={isHostRequest},playerIndex={playerIndex},pathSource='{source}',playerDirectory='{playerDirectory}',backpackPath='{backpackPath}',{fileState}.");
    }

    public static string ResolveCandidateBackpackPath(Player player, string parentFolderPath)
    {
        if (string.IsNullOrWhiteSpace(parentFolderPath))
            return "<missing-parent-path>";

        if (player == null)
            return "<missing-player>";

        try
        {
            var saveable = player.Cast<ISaveable>();
            var targetDirectory = parentFolderPath;
            if (saveable.ShouldSaveUnderFolder)
            {
                if (string.IsNullOrWhiteSpace(saveable.SaveFolderName))
                    return "<missing-save-folder-name>";

                targetDirectory = Path.Combine(targetDirectory, saveable.SaveFolderName);
            }

            return Path.Combine(targetDirectory, "Backpack.json");
        }
        catch (Exception exception)
        {
            return $"<unresolved:{exception.GetType().Name}>";
        }
    }

    private static string DescribeFileState(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "fileExists=unknown,fileBytes=unknown";

        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? $"fileExists=true,fileBytes={file.Length}"
                : "fileExists=false,fileBytes=unknown";
        }
        catch (Exception exception)
        {
            return $"fileExists=unknown,fileStatError={exception.GetType().Name}";
        }
    }

    private static string DescribeStorage(StorageEntity storage)
    {
        try
        {
            return storage == null || storage.Pointer == IntPtr.Zero
                ? "none"
                : $"0x{storage.Pointer.ToInt64():X}";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string Trim(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unspecified";

        const int maxLength = 180;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.RequestSavePlayer))]
internal static class PlayerRequestSaveDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance)
    {
        BackpackPersistenceDiagnostics.LogPlayerEvent(
            "request-save-call",
            __instance,
            "method=Player.RequestSavePlayer;rpcHandler=not-patched;acknowledgement=not-observed.");
    }

    [HarmonyPostfix]
    private static void Postfix(Player __instance)
    {
        BackpackPersistenceDiagnostics.LogPlayerEvent(
            "request-save-method-return",
            __instance,
            "method=Player.RequestSavePlayer;rpcHandler=not-patched;methodReturnDoesNotConfirmHostCommit.");
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Player __instance, Exception __exception)
    {
        if (__exception != null)
            BackpackPersistenceDiagnostics.LogPlayerEvent(
                "request-save-method-exception",
                __instance,
                $"exceptionType={__exception.GetType().Name};rpcHandler=not-patched.");

        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.SavePlayer), typeof(Player))]
internal static class PlayerManagerSaveDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player __0)
    {
        BackpackPersistenceDiagnostics.LogPlayerEvent(
            "manager-save-enter",
            __0,
            "method=PlayerManager.SavePlayer;boundary=save-method-entry.");
    }

    [HarmonyPostfix]
    private static void Postfix(Player __0)
    {
        BackpackPersistenceDiagnostics.LogPlayerEvent(
            "manager-save-return",
            __0,
            "method=PlayerManager.SavePlayer;boundary=method-return;diskCommitOrClientAcknowledgement=not-inferred.");
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Player __0, Exception __exception)
    {
        if (__exception != null)
            BackpackPersistenceDiagnostics.LogPlayerEvent(
                "manager-save-exception",
                __0,
                $"exceptionType={__exception.GetType().Name};boundary=save-method-threw.");

        return __exception;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.WriteData), typeof(string))]
internal static class PlayerWriteDataDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance, string __0)
    {
        BackpackPersistenceDiagnostics.LogPlayerWriteBegin(__instance, __0);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Player __instance, string __0, Exception __exception)
    {
        if (__exception != null)
            BackpackPersistenceDiagnostics.LogPlayerWriteException(__instance, __exception);

        return __exception;
    }
}
#endif
