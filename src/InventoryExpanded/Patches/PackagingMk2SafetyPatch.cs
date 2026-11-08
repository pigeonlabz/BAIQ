#if IL2CPP
using System.Collections.Concurrent;
using System.Reflection;
using Backpack.Cheats;
using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Packaging;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;

namespace Backpack.Patches;

/// <summary>
/// Fail-closed Mk2 safeguards. The plan clamp keeps a task within the output
/// room visible at task start; the native single-package boundary is checked
/// again immediately before every input-consuming commit.
/// </summary>
internal static class PackagingMk2Safety
{
    private static readonly ConcurrentDictionary<IntPtr, byte> Warned = new();

    internal readonly struct Preflight
    {
        public Preflight(int outputRoom, int recipeQuantity, string reason)
        {
            OutputRoom = outputRoom;
            RecipeQuantity = recipeQuantity;
            Reason = reason;
        }

        public int OutputRoom { get; }
        public int RecipeQuantity { get; }
        public string Reason { get; }
        public bool IsSafe => string.IsNullOrEmpty(Reason);
    }

    internal static bool TryBuildPreflight(
        PackagingStation station,
        out Preflight preflight,
        out string reason)
    {
        preflight = default;
        reason = "unknown-station-state";
        try
        {
            var packageSlot = station?.PackagingSlot;
            var productSlot = station?.ProductSlot;
            var outputSlot = station?.OutputSlot;
            var packagingItem = packageSlot?.ItemInstance;
            var definition = packagingItem?.Definition?.TryCast<PackagingDefinition>();
            var product = productSlot?.ItemInstance?.TryCast<ProductItemInstance>();

            if (packageSlot == null || productSlot == null || outputSlot == null
                || packagingItem == null || definition == null || product == null)
            {
                reason = "missing-package-product-or-slot";
                return false;
            }

            if (outputSlot.Quantity > 0 && outputSlot.ItemInstance == null)
            {
                reason = "output-quantity-without-item-instance";
                return false;
            }

            if (definition.Quantity <= 0)
            {
                reason = "invalid-recipe-quantity";
                return false;
            }

            if (packageSlot.IsRemovalLocked || productSlot.IsRemovalLocked)
            {
                reason = "input-removal-locked";
                return false;
            }

            if (outputSlot.IsLocked || outputSlot.IsAddLocked)
            {
                reason = "output-addition-locked";
                return false;
            }

            // Build the same semantic output the native method will commit:
            // clone the authoritative loose product (preserving quality and
            // product state), then apply the station's PackagingDefinition.
            var candidate = product.GetCopy(1)?.TryCast<ProductItemInstance>();
            if (candidate == null)
            {
                reason = "packaged-output-copy-unavailable";
                return false;
            }

            candidate.SetPackaging(definition);
            var outputRoom = outputSlot.GetCapacityForItem(candidate, false);
            if (outputRoom < 0)
            {
                reason = "negative-output-capacity";
                return false;
            }

            preflight = new Preflight(outputRoom, definition.Quantity, string.Empty);
            reason = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            reason = $"preflight-exception:{exception.GetType().Name}";
            return false;
        }
    }

    internal static void WarnOnce(PackagingStation station, string reason, string context)
    {
        var key = station?.Pointer ?? IntPtr.Zero;
        if (!Warned.TryAdd(key, 0))
            return;

        var guid = "<unavailable>";
        try { if (station != null) guid = station.GUID.ToString("N"); }
        catch { }
        Logger.Warning($"[PackagingMk2Safety] station={guid}; context={context}; refused-or-unbounded={reason}.");
    }
}

internal static class PackagingMk2SafetyPatchStatus
{
    internal static void ReportPatchRegistration(HarmonyLib.Harmony harmony)
    {
        try
        {
            var initialize = typeof(PackagingTool).GetMethod(
                nameof(PackagingTool.Initialize), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(Il2CppScheduleOne.PlayerTasks.Task), typeof(FunctionalPackaging), typeof(int),
                    typeof(ProductItemInstance), typeof(int)
                },
                modifiers: null);
            var commit = typeof(PackagingStation).GetMethod(
                nameof(PackagingStation.PackSingleInstance), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            var initializeInstalled = HasPrefix(initialize, typeof(PackagingMk2PlanClampPatch), harmony);
            var commitInstalled = HasPrefix(commit, typeof(PackagingMk2CommitGuardPatch), harmony);
            Logger.Info(
                $"[PackagingMk2Safety] patch registration: planClamp={initializeInstalled} " +
                $"({Format(initialize)}); commitGuard={commitInstalled} ({Format(commit)}); fail-closed on unknown commit state.");
        }
        catch (Exception exception)
        {
            Logger.Error($"[PackagingMk2Safety] registration inspection failed: {exception}");
        }
    }

    private static bool HasPrefix(MethodBase method, Type patchType, HarmonyLib.Harmony harmony) =>
        HarmonyLib.Harmony.GetPatchInfo(method)?.Prefixes?.Any(patch =>
            patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == patchType) == true;

    private static string Format(MethodBase method) => method == null
        ? "<unresolved>"
        : $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
}

[HarmonyPatch]
internal static class PackagingMk2PlanClampPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        var signature = new[]
        {
            typeof(Il2CppScheduleOne.PlayerTasks.Task), typeof(FunctionalPackaging), typeof(int),
            typeof(ProductItemInstance), typeof(int)
        };
        return typeof(PackagingTool).GetMethod(
                   nameof(PackagingTool.Initialize),
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null, types: signature, modifiers: null)
               ?? throw new MissingMethodException(typeof(PackagingTool).FullName,
                   "Initialize(Task,FunctionalPackaging,Int32,ProductItemInstance,Int32)");
    }

    // Run after the read-only observer so its START record retains the native
    // plan. The native initializer still receives the bounded arguments.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(
        PackagingTool __instance,
        Il2CppScheduleOne.PlayerTasks.Task __0,
        ref int __2,
        ProductItemInstance __3,
        ref int __4)
    {
        try
        {
            if (__0?.TryCast<Il2CppScheduleOne.PlayerTasks.PackageProductTaskMk2>() is not { } task
                || task.station?.TryCast<PackagingStation>() is not { } station)
                return;

            if (!PackagingMk2Safety.TryBuildPreflight(station, out var preflight, out var reason))
            {
                PackagingMk2Safety.WarnOnce(station, reason, "task-plan");
                return; // Keep native task plan; the commit guard remains authoritative.
            }

            var packageSlot = station.PackagingSlot;
            var productSlot = station.ProductSlot;
            var safe = PackagingMk2ObservationPolicy.ClampPlanToSafeCapacity(
                __2, __4,
                packageSlot.Quantity,
                productSlot.Quantity,
                preflight.RecipeQuantity,
                preflight.OutputRoom);

            if (safe.PackageCount <= 0
                || (safe.PackageCount == __2 && safe.LooseProductCount == __4))
                return;

            // The station locks its stock visuals before this initializer, and
            // its visual refresh methods intentionally no-op while locked. Do
            // not bypass that native lock; the task screen and staged objects
            // both derive from these reduced native arguments.
            var oldPackages = __2;
            var oldLoose = __4;
            __2 = safe.PackageCount;
            __4 = safe.LooseProductCount;
            Logger.Info(
                $"[PackagingMk2Safety] plan clamped; station={station.GUID}; native={oldPackages}/{oldLoose}; " +
                $"safe={safe.PackageCount}/{safe.LooseProductCount}; Q={preflight.RecipeQuantity}; " +
                $"outputRoom={preflight.OutputRoom}; multiplier unchanged (Mk2 throughput scaling deferred).");
        }
        catch (Exception exception)
        {
            Logger.Error($"[PackagingMk2Safety] plan clamp failed; native plan retained and commit guard remains active: {exception}");
        }
    }
}

[HarmonyPatch]
internal static class PackagingMk2CommitGuardPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        return typeof(PackagingStation).GetMethod(
                   nameof(PackagingStation.PackSingleInstance),
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null, types: Type.EmptyTypes, modifiers: null)
               ?? throw new MissingMethodException(typeof(PackagingStation).FullName, "PackSingleInstance()");
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(PackagingStation __instance, ref bool __runOriginal)
    {
        if (__instance == null || __instance.TryCast<PackagingStationMk2>() == null)
            return;

        var safe = PackagingMk2Safety.TryBuildPreflight(__instance, out var preflight, out var reason)
            && PackagingMk2ObservationPolicy.CanCommitNativePackage(
                __instance.PackagingSlot?.Quantity ?? 0,
                __instance.ProductSlot?.Quantity ?? 0,
                preflight.RecipeQuantity,
                preflight.OutputRoom,
                inputRemovalAllowed: !(__instance.PackagingSlot?.IsRemovalLocked ?? true)
                    && !(__instance.ProductSlot?.IsRemovalLocked ?? true),
                outputAdditionAllowed: !(__instance.OutputSlot?.IsLocked ?? true)
                    && !(__instance.OutputSlot?.IsAddLocked ?? true));

        if (safe)
            return;

        __runOriginal = false;
        reason ??= preflight.Reason;
        reason = string.IsNullOrEmpty(reason) ? "no-complete-output-transaction-fits" : reason;
        PackagingMk2Diagnostics.RecordCommitRefused(__instance, reason);
        PackagingMk2Safety.WarnOnce(__instance, reason, "per-package-commit");
    }
}
#endif
