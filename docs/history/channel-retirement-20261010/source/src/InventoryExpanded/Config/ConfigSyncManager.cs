using System.Collections;
using System.Text;
using MelonLoader;
using UnityEngine;

#if IL2CPP
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
#elif MONO
using ScheduleOne.Levelling;
using ScheduleOne.Networking;
using Steamworks;
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
        var isHost = Lobby.Instance.IsHost;
        var isClient = !isHost && Lobby.Instance.IsInLobby;
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
        var payload = new StringBuilder();
        payload.Append($"{ModVersion}[");
        payload.Append($"{Configuration.Instance.UnlockLevel.Rank}:{Configuration.Instance.UnlockLevel.Tier},");
        payload.Append($"{Configuration.Instance.EnableSearch},");
        payload.Append($"{Configuration.Instance.StorageSlots},");
        payload.Append(']');

        Logger.Diagnostic("Shared configuration with the current lobby.");
        Lobby.Instance.SetLobbyData(Prefix, payload.ToString());
    }

    private static IEnumerator WaitForPayload(int generation)
    {
        const int maxAttempts = 10;
        const float waitTime = 1f;
        for (var i = 0; i < maxAttempts; ++i)
        {
            if (generation != _syncGeneration)
                yield break;

            var payload = SteamMatchmaking.GetLobbyData((CSteamID) Lobby.Instance.LobbyID, Prefix);
            if (string.IsNullOrEmpty(payload))
            {
                yield return new WaitForSeconds(waitTime);
                continue;
            }

            Logger.Diagnostic("Applying configuration received from the lobby host.");
            try
            {
                SyncFromHost(payload);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Error while parsing payload: {payload}");
            }

            yield break;
        }
    }

    private static void SyncFromHost(string payload)
    {
        var parts = payload.Split(['[', ']', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            Logger.Warning($"Invalid payload format: {payload}");
            return;
        }

        if (parts[0] != ModVersion)
        {
            Logger.Warning($"Mod version mismatch: {parts[0]} != {ModVersion}");
            return;
        }

        var unlockLevel = parts[1].Split(':');
        if (unlockLevel.Length != 2 || !Enum.TryParse(unlockLevel[0], out ERank rank) || !int.TryParse(unlockLevel[1], out var tier))
        {
            Logger.Warning($"Invalid unlock level format: {parts[1]}");
            return;
        }

        Configuration.Instance.UnlockLevel = new FullRank(rank, tier);
        Configuration.Instance.EnableSearch = bool.Parse(parts[2]);
        Configuration.Instance.StorageSlots = int.Parse(parts[3]);
    }
}
