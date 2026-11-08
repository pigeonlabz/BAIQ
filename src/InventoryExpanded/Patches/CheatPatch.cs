#if IL2CPP
using Backpack.Cheats;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.ItemFramework;

namespace Backpack.Patches;

[HarmonyPatch(typeof(BaseItemInstance), "get_StackLimit")]
public static class StackSizeMultiplierPatch
{
    [HarmonyPostfix]
    private static void ApplyMultiplier(BaseItemInstance __instance, ref int __result)
    {
        if (__result <= 1 || __instance == null)
            return;

        if (__instance.TryCast<CashInstance>() != null)
            return;

        // DeliveryInstance splits ordered quantities using the definition's
        // native StackLimit. Keep the nested vehicle-storage insertion on that
        // same limit so it never treats native-full payload chunks as mergeable.
        if (DeliveryVehicleLoadContext.IsActive)
            return;

        var multiplier = CheatSettings.StackSizeMultiplierValue;

        var multipliedLimit = Math.Min((long)__result * multiplier, StackCapacityPolicy.SerializationSafeMaximum);
        var effectiveLimit = (int)multipliedLimit;

        if (OvercapTransferContext.TryGetNormalizingLimit(__instance, out var normalizingLimit))
        {
            __result = normalizingLimit;
            return;
        }

        // An existing oversized stack gets no spare capacity, but can be reduced
        // through the game's normal quantity operations without being clamped.
        __result = Math.Max(effectiveLimit, __instance.Quantity);
    }
}

[HarmonyPatch(typeof(BaseItemInstance), nameof(BaseItemInstance.ChangeQuantity))]
public static class OvercapQuantityReductionPatch
{
    [HarmonyPrefix]
    private static bool ApplyNormalizingDecrease(BaseItemInstance __instance, int __0)
    {
        return !OvercapTransferContext.TryApplySourceDecrease(__instance, __0);
    }
}

[HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.UnlockAchievement))]
public static class AchievementSuppressionPatch
{
    [HarmonyPrefix]
    private static bool SuppressWhileCheating(AchievementManager.EAchievement __0)
    {
        if (!CheatState.IsAnyCheatActive)
            return true;

        Logger.Diagnostic(
            $"Suppressed achievement '{__0}' because these cheats are active: {CheatState.ActiveCheatSummary}.");
        return false;
    }
}
#endif
