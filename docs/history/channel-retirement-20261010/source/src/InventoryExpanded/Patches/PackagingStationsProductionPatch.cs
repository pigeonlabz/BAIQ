#if IL2CPP
using System.Reflection;
using System.Threading;
using Backpack.Cheats;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Packaging;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;

namespace Backpack.Patches;

/// <summary>
/// Mk1 packaging is a sequence of native one-package transactions. The native
/// PackSingleInstance path retains item identity, quality, packaging state and
/// slot/network updates. Before each additional unit this adapter re-runs the
/// native Pack preflight and stops unless the preceding unit committed exactly
/// one package, one packaging unit and the recipe's loose-product quantity.
/// Mk2 is explicitly excluded because it stages physical packages through a
/// separate task/tool lifecycle.
/// </summary>
[HarmonyPatch]
internal static class PackagingStationsProductionPatch
{
    private const int MaximumTargetPackages = 8;

    private sealed class Invocation
    {
        public PackagingTransactionGuard<IntPtr>.Lease Lease;
        public SlotSnapshot Before;
        public int StackMultiplier;
        public int TargetPackages;
        public int CommittedPackages;
        public int Depth;
        public int InteractionId;
        public int PreviousInteractionId;
        public IntPtr StationPointer;
        public bool IsReentrant;
        public string StopReason;
    }

    private readonly struct SlotSnapshot
    {
        public SlotSnapshot(int packages, int product, int output, int productPerPackage)
        {
            PackagingUnits = packages;
            LooseProduct = product;
            OutputPackages = output;
            ProductPerPackage = productPerPackage;
        }

        public int PackagingUnits { get; }
        public int LooseProduct { get; }
        public int OutputPackages { get; }
        public int ProductPerPackage { get; }

        public bool RepresentsCommittedUnit(SlotSnapshot after)
        {
            return ProductPerPackage == after.ProductPerPackage
                && PackagingProductionPolicy.IsOneNativePackageCommitted(
                    PackagingUnits,
                    after.PackagingUnits,
                    LooseProduct,
                    after.LooseProduct,
                    ProductPerPackage,
                    OutputPackages,
                    after.OutputPackages);
        }

        public override string ToString() =>
            $"packaging={PackagingUnits},loose={LooseProduct},output={OutputPackages},Q={ProductPerPackage}";
    }

    [ThreadStatic]
    private static PackagingTransactionGuard<IntPtr> _guard;

    [ThreadStatic]
    private static int _callDepth;

    [ThreadStatic]
    private static int _activeInteractionId;

    private static int _nextInteractionId;
    private static int _loggedSettingOff;
    private static int _loggedVanilla;
    private static int _loggedMk2;
    private static int _loggedNullInstance;

    private static PackagingTransactionGuard<IntPtr> Guard =>
        _guard ??= new PackagingTransactionGuard<IntPtr>();

    private static readonly Lazy<MethodInfo> NativeGetState = new(FindGetState);
    private static readonly Lazy<object> PackMode = new(() =>
    {
        var method = NativeGetState.Value;
        return Enum.ToObject(method.GetParameters()[0].ParameterType, 0); // EMode.Package in both reference profiles.
    });

    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        var method = typeof(PackagingStation).GetMethod(
            nameof(PackagingStation.PackSingleInstance),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        return method ?? throw new MissingMethodException(
            typeof(PackagingStation).FullName,
            nameof(PackagingStation.PackSingleInstance) + "()");
    }

    internal static void ReportPatchRegistration(HarmonyLib.Harmony harmony)
    {
        try
        {
            var target = TargetMethod();
            var patches = HarmonyLib.Harmony.GetPatchInfo(target);
            var prefixInstalled = patches?.Prefixes?.Any(patch =>
                patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == typeof(PackagingStationsProductionPatch)) == true;
            var postfixInstalled = patches?.Postfixes?.Any(patch =>
                patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == typeof(PackagingStationsProductionPatch)) == true;
            var finalizerInstalled = patches?.Finalizers?.Any(patch =>
                patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == typeof(PackagingStationsProductionPatch)) == true;
            Logger.Info(
                $"[PackagingProduction] patch registration: prefix={prefixInstalled}, postfix={postfixInstalled}, " +
                $"finalizer={finalizerInstalled}; resolved method={FormatMethod(target)}; harmony={harmony.Id}.");
        }
        catch (Exception exception)
        {
            Logger.Error($"[PackagingProduction] patch registration inspection failed: {exception}");
        }
    }

    [HarmonyPrefix]
    private static void Prefix(PackagingStation __instance, out Invocation __state)
    {
        var depth = ++_callDepth;
        __state = new Invocation
        {
            Depth = depth,
            PreviousInteractionId = _activeInteractionId,
            InteractionId = _activeInteractionId
        };

        if (__instance == null)
        {
            __state.StopReason = "null-station";
            if (Interlocked.Exchange(ref _loggedNullInstance, 1) == 0)
                Logger.Warning("[PackagingProduction] PackSingleInstance entered with a null station instance.");
            return;
        }

        var runtimeType = __instance.GetType().FullName ?? __instance.GetType().Name;
        __state.StationPointer = __instance.Pointer;

        if (Guard.IsHeld(__state.StationPointer))
        {
            __state.IsReentrant = true;
            __state.InteractionId = _activeInteractionId;
            Logger.Info(
                $"[PackagingProduction] EXTRA CALL ENTER classification=reentrant; interaction={__state.InteractionId}, " +
                $"depth={depth}, guardHeld=true, stationType={runtimeType}; nested batch suppressed.");
            return;
        }

        var isMk2 = __instance.TryCast<PackagingStationMk2>() != null;
        var enabled = ProductionCheats.PackagingStationsEnabled;
        var stackMultiplier = CheatSettings.StackSizeMultiplierValue;
        var effectiveMultiplier = ProductionCheats.GetMultiplier(enabled);
        var targetPackages = PackagingProductionPolicy.GetTargetPackages(stackMultiplier, enabled);

        if (isMk2)
        {
            __state.StopReason = "Mk2-native";
            if (Interlocked.Exchange(ref _loggedMk2, 1) == 0)
                Logger.Info($"[PackagingProduction] skipped Mk2 native path; stationType={runtimeType}; Mk2 scaling is intentionally unsupported.");
            return;
        }

        if (!enabled)
        {
            __state.StopReason = "setting-off";
            if (Interlocked.Exchange(ref _loggedSettingOff, 1) == 0)
                Logger.Info($"[PackagingProduction] skipped Mk1 scaling; setting=false, stackMultiplier={stackMultiplier}, stationType={runtimeType}.");
            return;
        }

        if (effectiveMultiplier <= 1 || targetPackages <= 1)
        {
            __state.StopReason = "vanilla-multiplier";
            if (Interlocked.Exchange(ref _loggedVanilla, 1) == 0)
                Logger.Info($"[PackagingProduction] skipped Mk1 scaling; setting=true, stackMultiplier={stackMultiplier}, effectiveMultiplier={effectiveMultiplier}, stationType={runtimeType}.");
            return;
        }

        if (!Guard.TryEnter(__state.StationPointer, out var lease))
        {
            __state.IsReentrant = true;
            __state.InteractionId = _activeInteractionId;
            Logger.Info(
                $"[PackagingProduction] EXTRA CALL ENTER classification=reentrant; interaction={__state.InteractionId}, " +
                $"depth={depth}, guardHeld={Guard.IsHeld(__state.StationPointer)}, stationType={runtimeType}; nested batch suppressed.");
            return;
        }

        __state.Lease = lease;
        __state.InteractionId = Interlocked.Increment(ref _nextInteractionId);
        __state.StackMultiplier = stackMultiplier;
        __state.TargetPackages = targetPackages;
        Logger.Info(
            $"[PackagingProduction] ROOT ENTER; interaction={__state.InteractionId}, depth={depth}, guard=owned, " +
            $"stationType={runtimeType}, setting=true, stackMultiplier={stackMultiplier}, " +
            $"effectiveMultiplier={effectiveMultiplier}, targetPackages={targetPackages}, patchRoot=true.");

        try
        {
            if (!TryCapture(__instance, out var before, requireInputs: true))
            {
                __state.StopReason = "root-snapshot-unavailable";
                lease.Dispose();
                __state.Lease = null;
                Logger.Warning($"[PackagingProduction] interaction={__state.InteractionId} skipped batching: pre-call root snapshot unavailable.");
                return;
            }

            // Harmony Prefix runs before the game's native body. Carry this
            // immutable baseline to the root Postfix for the first delta check.
            __state.Before = before;
            _activeInteractionId = __state.InteractionId;
            Logger.Info($"[PackagingProduction] interaction={__state.InteractionId} root baseline captured before native call: {before}.");
        }
        catch (Exception exception)
        {
            __state.StopReason = $"root-snapshot-exception:{exception.GetType().Name}";
            lease.Dispose();
            __state.Lease = null;
            Logger.Error($"[PackagingProduction] interaction={__state.InteractionId} root snapshot exception; native call remains vanilla: {exception}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(PackagingStation __instance, Invocation __state)
    {
        if (__state?.Lease == null)
            return;

        Logger.Info($"[PackagingProduction] NATIVE ROOT RETURN; interaction={__state.InteractionId}, depth={__state.Depth}.");
        try
        {
            if (!TryCapture(__instance, out var afterFirst, __state.Before.ProductPerPackage))
            {
                Stop(__state, "root-post-snapshot-unavailable");
                return;
            }

            var rootDeltaValid = __state.Before.RepresentsCommittedUnit(afterFirst);
            Logger.Info(
                $"[PackagingProduction] interaction={__state.InteractionId} root delta valid={rootDeltaValid}; " +
                $"before=({__state.Before}), after=({afterFirst}).");
            if (!rootDeltaValid)
            {
                Stop(__state, "root-delta-mismatch");
                return;
            }

            __state.CommittedPackages = 1;
            while (__state.CommittedPackages < __state.TargetPackages)
            {
                if (!TryGetNativePackPreflight(__instance, out var canPack, out var nativeState))
                {
                    Stop(__state, "native-preflight-exception");
                    return;
                }
                if (!canPack)
                {
                    Stop(__state, $"native-preflight:{nativeState}");
                    return;
                }

                if (!TryCapture(__instance, out var beforeNext, requireInputs: true))
                {
                    Stop(__state, "extra-pre-snapshot-unavailable");
                    return;
                }

                var extraBudget = PackagingProductionPolicy.GetAdditionalPackages(
                    __state.StackMultiplier,
                    enabled: true,
                    packagesAlreadyCommitted: __state.CommittedPackages,
                    availablePackagingUnits: beforeNext.PackagingUnits,
                    availableLooseProduct: beforeNext.LooseProduct,
                    looseProductPerPackage: beforeNext.ProductPerPackage,
                    outputRoom: 1);
                if (extraBudget <= 0)
                {
                    Stop(__state, "no-complete-recipe-unit-or-output-room");
                    return;
                }

                var packageNumber = __state.CommittedPackages + 1;
                Logger.Info(
                    $"[PackagingProduction] EXTRA CALL ENTER #{packageNumber}; interaction={__state.InteractionId}, " +
                    $"depth={__state.Depth + 1}, guardOwner=interaction-{__state.InteractionId}, " +
                    $"reentrant=true expected=nested-batch-suppressed, before=({beforeNext}).");

                // The nested Prefix observes the owner's guard and declines to
                // open another batch. Its original native method body still runs.
                __instance.PackSingleInstance();

                if (!TryCapture(__instance, out var afterNext, beforeNext.ProductPerPackage))
                {
                    Stop(__state, $"extra-{packageNumber}-post-snapshot-unavailable");
                    return;
                }

                var extraDeltaValid = beforeNext.RepresentsCommittedUnit(afterNext);
                Logger.Info(
                    $"[PackagingProduction] EXTRA CALL RETURN #{packageNumber}; interaction={__state.InteractionId}, " +
                    $"committed={extraDeltaValid}, before=({beforeNext}), after=({afterNext}).");
                if (!extraDeltaValid)
                {
                    Stop(__state, $"extra-{packageNumber}-delta-mismatch");
                    return;
                }

                __state.CommittedPackages++;
            }

            __state.StopReason = "target-reached";
        }
        catch (Exception exception)
        {
            __state.StopReason = $"additional-call-exception:{exception.GetType().Name}";
            // Do not hide caught failures behind DiagnosticLogging. This is
            // temporary high-signal instrumentation for the live regression.
            Logger.Error($"[PackagingProduction] interaction={__state.InteractionId} stopped after caught exception: {exception}");
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Invocation __state, Exception __exception)
    {
        if (__state == null)
            return __exception;

        if (__state.Lease != null)
        {
            var owner = __state.Lease.Owner;
            __state.Lease.Dispose();
            var released = !Guard.IsHeld(owner);
            _activeInteractionId = __state.PreviousInteractionId;
            Logger.Info(
                $"[PackagingProduction] ROOT EXIT / FINALIZER; interaction={__state.InteractionId}, depth={__state.Depth}, " +
                $"committed={__state.CommittedPackages}/{__state.TargetPackages}, guardReleased={released}, " +
                $"exception={( __exception == null ? "none" : __exception.GetType().FullName + ": " + __exception.Message )}, " +
                $"stop={__state.StopReason ?? "native-exception-or-postfix-not-run"}.");
        }
        else if (__state.IsReentrant)
        {
            Logger.Info(
                $"[PackagingProduction] EXTRA CALL FINALIZER; interaction={__state.InteractionId}, depth={__state.Depth}, " +
                $"guardStillOwned={Guard.IsHeld(__state.StationPointer)}, exception={( __exception == null ? "none" : __exception.GetType().FullName + ": " + __exception.Message )}; nested batch remained suppressed.");
        }

        _callDepth = Math.Max(0, _callDepth - 1);
        if (__exception != null)
            Logger.Error($"[PackagingProduction] native PackSingleInstance exception at depth={__state.Depth}: {__exception}");
        return __exception;
    }

    private static void Stop(Invocation invocation, string reason)
    {
        invocation.StopReason = reason;
        Logger.Info($"[PackagingProduction] interaction={invocation.InteractionId} stopped: {reason}; committed={invocation.CommittedPackages}/{invocation.TargetPackages}.");
    }

    private static bool TryCapture(
        PackagingStation station,
        out SlotSnapshot snapshot,
        int fallbackRecipeQuantity = 0,
        bool requireInputs = false)
    {
        snapshot = default;
        var packagingSlot = station?.PackagingSlot;
        var productSlot = station?.ProductSlot;
        var outputSlot = station?.OutputSlot;
        var packagingItem = packagingSlot?.ItemInstance;
        var productItem = productSlot?.ItemInstance?.TryCast<ProductItemInstance>();
        var packagingDefinition = packagingItem?.Definition?.TryCast<PackagingDefinition>();
        var recipeQuantity = packagingDefinition?.Quantity ?? fallbackRecipeQuantity;
        if (packagingSlot == null || productSlot == null || outputSlot == null
            || (requireInputs && (packagingDefinition == null || productItem == null))
            || recipeQuantity <= 0)
        {
            return false;
        }

        snapshot = new SlotSnapshot(
            packagingSlot.Quantity,
            productSlot.Quantity,
            outputSlot.Quantity,
            recipeQuantity);
        return true;
    }

    private static bool TryGetNativePackPreflight(PackagingStation station, out bool canPack, out string stateDescription)
    {
        canPack = false;
        stateDescription = "unavailable";
        try
        {
            var state = NativeGetState.Value.Invoke(station, new[] { PackMode.Value });
            if (state == null)
            {
                stateDescription = "null";
                return true;
            }

            var stateValue = Convert.ToInt32(state);
            stateDescription = $"{state}({stateValue})";
            canPack = stateValue == 0; // EState.CanBegin in both reference profiles.
            return true;
        }
        catch (Exception exception)
        {
            stateDescription = $"exception:{exception.GetType().Name}";
            Logger.Error($"[PackagingProduction] native Mk1 Pack preflight exception; no extra unit attempted: {exception}");
            return false;
        }
    }

    private static MethodInfo FindGetState()
    {
        var method = typeof(PackagingStation)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "GetState" && candidate.GetParameters().Length == 1);
        if (!method.GetParameters()[0].ParameterType.IsEnum)
            throw new InvalidOperationException("PackagingStation.GetState mode parameter is not an enum.");
        return method;
    }

    private static string FormatMethod(MethodBase method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
}
#endif
