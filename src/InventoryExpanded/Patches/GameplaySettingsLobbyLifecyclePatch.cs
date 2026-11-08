using Backpack.Config;
using HarmonyLib;

#if IL2CPP
using Il2CppScheduleOne.Networking;
#elif MONO
using ScheduleOne.Networking;
#endif

namespace Backpack.Patches;

[HarmonyPatch(typeof(SteamLobbyService), "Initialize")]
internal static class SteamLobbyServiceInitializedForGameplayPolicyPatch
{
    [HarmonyPostfix]
    private static void Postfix(SteamLobbyService __instance) =>
        GameplaySettingsSyncManager.OnNativeLobbyServiceInitialized(__instance);
}

[HarmonyPatch(typeof(SteamLobbyService), "OnLobbyCreated")]
internal static class SteamLobbyCreatedForGameplayPolicyPatch
{
    [HarmonyPostfix]
    private static void Postfix(SteamLobbyService __instance) =>
        GameplaySettingsSyncManager.OnNativeLobbySessionStarted(__instance, "SteamLobbyService.OnLobbyCreated");
}

[HarmonyPatch(typeof(SteamLobbyService), "OnLobbyEntered")]
internal static class SteamLobbyEnteredForGameplayPolicyPatch
{
    [HarmonyPostfix]
    private static void Postfix(SteamLobbyService __instance) =>
        GameplaySettingsSyncManager.OnNativeLobbySessionStarted(__instance, "SteamLobbyService.OnLobbyEntered");
}

[HarmonyPatch(typeof(SteamLobbyService), "LeaveLobby")]
internal static class SteamLobbyServiceLeftForGameplayPolicyPatch
{
    [HarmonyPostfix]
    private static void Postfix() => GameplaySettingsSyncManager.OnNativeLobbySessionEnded("SteamLobbyService.LeaveLobby");
}
