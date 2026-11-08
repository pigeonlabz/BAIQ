using System.Collections;
using MelonLoader;
using UnityEngine;

#if IL2CPP
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Networking;
#elif MONO
using ScheduleOne.Levelling;
using ScheduleOne.Networking;
#endif

namespace Backpack.Config;

public static class ConfigSyncManager
{
    private const string Prefix = "Backpack_Config";
    private static readonly string ModVersion = typeof(BackpackMod).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    private static int _syncGeneration;

    public static void StartSync()
    {
        var generation = ++_syncGeneration;
        var lobby = Lobby.Instance;
        var lobbyService = GameplaySettingsSyncManager.ActiveLobbyService;
        if (lobby == null || !lobby.IsInLobby || lobbyService == null || !lobbyService.IsInLobby)
            return;

        var isHost = lobby.IsHost && lobbyService.IsHost;
        var isClient = !isHost;
        if (isHost)
        {
            SyncToClients();
        }
        else if (isClient)
        {
            MelonCoroutines.Start(WaitForPayload(generation));
        }
    }

    public static void ResetSession()
    {
        _syncGeneration++;
    }

    private static void SyncToClients()
    {
        var configuration = Configuration.Instance;
        var lobby = Lobby.Instance;
        var lobbyService = GameplaySettingsSyncManager.ActiveLobbyService;
        if (lobby == null || !lobby.IsInLobby || lobbyService == null || !lobbyService.IsInLobby)
            return;

        var payload = ConfigSyncPayloadPolicy.Create(
            ModVersion,
            configuration.UnlockLevel.Rank.ToString(),
            configuration.UnlockLevel.Tier,
            configuration.EnableSearch);

        Logger.Diagnostic("Attempting to write the supported legacy lobby settings to lobby metadata.");
        try
        {
            lobbyService.SetLobbyData(Prefix, payload);
            var readback = lobbyService.GetLobbyData(Prefix);
            if (string.Equals(readback, payload, StringComparison.Ordinal))
                Logger.Diagnostic("Verified the supported legacy lobby settings by exact metadata readback.");
            else
                Logger.Warning("Legacy lobby settings write was not verified by exact metadata readback.");
        }
        catch (Exception exception)
        {
            Logger.Warning($"Legacy lobby settings write/readback failed safely: {exception.GetType().Name}.");
        }
    }

    private static IEnumerator WaitForPayload(int generation)
    {
        var attempts = 0;
        while (generation == _syncGeneration)
        {
            var lobby = Lobby.Instance;
            var lobbyService = GameplaySettingsSyncManager.ActiveLobbyService;
            if (lobby == null || lobbyService == null)
            {
                attempts++;
                if (attempts == 10)
                    Logger.Warning("Legacy host settings are waiting for the lobby wrapper; slow lobby-scoped retries continue.");
                yield return new WaitForSeconds(attempts < 10 ? 1f : 2f);
                continue;
            }

            if (!lobby.IsInLobby)
                yield break;

            if (!lobbyService.IsInLobby)
            {
                attempts++;
                if (attempts == 10)
                    Logger.Warning("Legacy host settings are waiting for the active Steam lobby service; slow lobby-scoped retries continue.");
                yield return new WaitForSeconds(attempts < 10 ? 1f : 2f);
                continue;
            }

            var payload = lobbyService.GetLobbyData(Prefix);
            if (string.IsNullOrEmpty(payload))
            {
                attempts++;
                if (attempts == 10)
                    Logger.Warning("Legacy host settings metadata is still unavailable; slow lobby-scoped retries continue.");
                yield return new WaitForSeconds(attempts < 10 ? 1f : 2f);
                continue;
            }

            SyncFromHost(payload);
            yield break;
        }
    }

    private static void SyncFromHost(string payload)
    {
        if (!ConfigSyncPayloadPolicy.TryParse(payload, ModVersion, out var parsed))
        {
            Logger.Warning("[ConfigSync] Ignored missing, malformed, unsupported, or version-mismatched host settings metadata; local preferences remain active.");
            return;
        }

        if (!Enum.TryParse(parsed.UnlockRank, out ERank rank)
            || !Enum.IsDefined(typeof(ERank), rank))
        {
            Logger.Warning("[ConfigSync] Ignored host settings metadata with an unsupported rank value.");
            return;
        }

        Logger.Diagnostic("Applying version-matched host settings metadata. Backpack capacity remains local to protect occupied slots.");
        Configuration.Instance.UnlockLevel = new FullRank(rank, parsed.UnlockTier);
        Configuration.Instance.EnableSearch = parsed.EnableSearch;
    }
}
