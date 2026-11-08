#if IL2CPP
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Backpack.Cheats;
using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Packaging;
using Il2CppScheduleOne.PlayerTasks;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;
using Task = Il2CppScheduleOne.PlayerTasks.Task;

namespace Backpack.Patches;

/// <summary>
/// Read-only Mk2 packaging trace. This records the native task plan, display,
/// physical-object transitions and per-package station deltas. It deliberately
/// does not alter task quantities, resources, timing, or save state.
/// </summary>
internal static class PackagingMk2Diagnostics
{
    private const int MaxDetailedEvents = 8;

    private static readonly ConcurrentDictionary<IntPtr, Session> ByTool = new();
    private static readonly ConcurrentDictionary<IntPtr, Session> ByStation = new();
    private static readonly ConcurrentDictionary<IntPtr, int> SuppressedCommitFrames = new();
    private static int _nextSessionId;

    internal sealed class Session
    {
        public int Id;
        public IntPtr ToolPointer;
        public IntPtr TaskPointer;
        public IntPtr StationPointer;
        public PackagingStation Station;
        public string StationGuid;
        public string StationType;
        public int NativePlanPackages;
        public int NativePlanLooseProduct;
        public int RecipeQuantity;
        public int NominalMultiplier;
        public bool SettingEnabled;
        public int StackMultiplier;
        public int DropProductCalls;
        public int DeployPackagingCalls;
        public int InsertIntoHopperCalls;
        public int FinalizeRequests;
        public int NativeCommitCalls;
        public int ValidNativeCommits;
        public int CommitMismatches;
        public int Exceptions;
        public int SafetyRefusedCommits;
        public int DetailedEventsLogged;
        public bool Closed;
    }

    internal sealed class EventFrame
    {
        public Session Session;
        public string Method;
        public int PackagingObjectsBefore;
        public int ProductObjectsBefore;
        public int ConcealedPackagingBefore;
        public int ProductInHopperBefore;
        public string DisplayBefore;
    }

    internal sealed class CommitFrame
    {
        public Session Session;
        public StationSnapshot Before;
    }

    internal readonly struct StationSnapshot
    {
        public StationSnapshot(PackagingStation station)
        {
            var packageSlot = station?.PackagingSlot;
            var productSlot = station?.ProductSlot;
            var outputSlot = station?.OutputSlot;
            var packageItem = packageSlot?.ItemInstance;
            var productItem = productSlot?.ItemInstance?.TryCast<ProductItemInstance>();
            var outputItem = outputSlot?.ItemInstance?.TryCast<ProductItemInstance>();
            var packagingDefinition = packageItem?.Definition?.TryCast<PackagingDefinition>();

            Packaging = packageSlot?.Quantity ?? -1;
            LooseProduct = productSlot?.Quantity ?? -1;
            Output = outputSlot?.Quantity ?? -1;
            RecipeQuantity = packagingDefinition?.Quantity ?? 0;
            PackagingId = packageItem?.ID ?? "<empty>";
            ProductId = productItem?.ID ?? "<empty>";
            ProductQuality = productItem == null ? "<unknown>" : productItem.Quality.ToString();
            OutputId = outputItem?.ID ?? outputSlot?.ItemInstance?.ID ?? "<empty>";
            OutputQuality = outputItem == null ? "<unknown>" : outputItem.Quality.ToString();
            OutputPackaging = outputItem?.AppliedPackaging?.ID ?? "<unknown>";
            OutputAtCapacity = outputSlot?.IsAtCapacity ?? false;
        }

        public int Packaging { get; }
        public int LooseProduct { get; }
        public int Output { get; }
        public int RecipeQuantity { get; }
        public string PackagingId { get; }
        public string ProductId { get; }
        public string ProductQuality { get; }
        public string OutputId { get; }
        public string OutputQuality { get; }
        public string OutputPackaging { get; }
        public bool OutputAtCapacity { get; }

        public bool IsOneCommitTo(StationSnapshot after) => PackagingMk2ObservationPolicy.IsOneNativePackageCommitted(
            Packaging, after.Packaging,
            LooseProduct, after.LooseProduct,
            Output, after.Output,
            RecipeQuantity > 0 ? RecipeQuantity : after.RecipeQuantity);

        public override string ToString() =>
            $"packaging={Packaging}({PackagingId}),loose={LooseProduct}({ProductId},quality={ProductQuality})," +
            $"Q={RecipeQuantity},output={Output}({OutputId},quality={OutputQuality},packaging={OutputPackaging},atCapacity={OutputAtCapacity})";
    }

    internal static void Begin(PackagingTool tool, PackageProductTaskMk2 task, int nativePlanPackages, int nativePlanLooseProduct)
    {
        var station = task?.station;
        if (tool == null || station == null)
            return;

        var stationBase = station.TryCast<PackagingStation>();
        if (stationBase == null)
            return;

        try
        {
            var before = new StationSnapshot(stationBase);
            var enabled = ProductionCheats.PackagingStationsEnabled;
            var stackMultiplier = CheatSettings.StackSizeMultiplierValue;
            var effectiveMultiplier = PackagingProductionPolicy.GetTargetPackages(stackMultiplier, enabled);
            var modeledPlan = PackagingMk2ObservationPolicy.EstimateNativePlan(
                before.Packaging, before.LooseProduct, before.RecipeQuantity);
            var session = new Session
            {
                Id = Interlocked.Increment(ref _nextSessionId),
                ToolPointer = tool.Pointer,
                TaskPointer = task.Pointer,
                StationPointer = station.Pointer,
                Station = stationBase,
                StationGuid = station.GUID.ToString("N"),
                StationType = station.GetType().FullName ?? station.GetType().Name,
                NativePlanPackages = nativePlanPackages,
                NativePlanLooseProduct = nativePlanLooseProduct,
                RecipeQuantity = before.RecipeQuantity,
                NominalMultiplier = effectiveMultiplier,
                SettingEnabled = enabled,
                StackMultiplier = stackMultiplier
            };

            ByTool[session.ToolPointer] = session;
            ByStation[session.StationPointer] = session;

            var requestedPackages = (long)nativePlanPackages * effectiveMultiplier;
            var requestedLoose = (long)nativePlanLooseProduct * effectiveMultiplier;
            Logger.Info(
                $"[PackagingMk2] START session={session.Id}; stationType={session.StationType}; guid={session.StationGuid}; " +
                $"setting={enabled}; stackMultiplier={stackMultiplier}; nominalMultiplier={effectiveMultiplier}; " +
                $"nativePlanPackages={nativePlanPackages}; nativePlanLooseProduct={nativePlanLooseProduct}; Q={before.RecipeQuantity}; " +
                $"requestedScaledPlanPackages={requestedPackages}; requestedScaledLooseProduct={requestedLoose}; appliedScaledPlan=false; " +
                $"readOnlyPlanModel={modeledPlan.PackageCount}/{modeledPlan.LooseProductCount}; " +
                $"planMatchesModel={modeledPlan.PackageCount == nativePlanPackages && modeledPlan.LooseProductCount == nativePlanLooseProduct}; " +
                $"availableBefore=({before}); outputRoom={DescribeOutputRoom(stationBase)}.");
        }
        catch (Exception exception)
        {
            Logger.Error($"[PackagingMk2] read-only task-start capture failed; native Mk2 behavior remains unchanged: {exception}");
        }
    }

    internal static Session GetSession(PackagingTool tool)
    {
        if (tool == null)
            return null;
        return ByTool.TryGetValue(tool.Pointer, out var session) && !session.Closed ? session : null;
    }

    internal static CommitFrame BeginCommit(PackagingStation station)
    {
        if (station == null || station.TryCast<PackagingStationMk2>() == null)
            return null;

        if (TakeSuppressedCommitFrame(station.Pointer)
            || !ByStation.TryGetValue(station.Pointer, out var session) || session.Closed)
            return null;

        try
        {
            return new CommitFrame { Session = session, Before = new StationSnapshot(station) };
        }
        catch (Exception exception)
        {
            Logger.Error($"[PackagingMk2] session={session.Id} pre-commit snapshot failed: {exception}");
            return null;
        }
    }

    internal static void RecordCommitRefused(PackagingStation station, string reason)
    {
        if (station == null)
            return;

        var pointer = station.Pointer;
        SuppressedCommitFrames.AddOrUpdate(pointer, 1, (_, count) => count + 1);
        if (ByStation.TryGetValue(pointer, out var session) && !session.Closed)
        {
            session.SafetyRefusedCommits++;
            WriteBoundedEvent(session, $"nativeCommit=refused-before-input-mutation; reason={reason}; station quantities unchanged.");
        }
    }

    private static bool TakeSuppressedCommitFrame(IntPtr pointer)
    {
        while (SuppressedCommitFrames.TryGetValue(pointer, out var count))
        {
            if (count <= 1)
            {
                if (SuppressedCommitFrames.TryRemove(pointer, out _))
                    return true;
            }
            else if (SuppressedCommitFrames.TryUpdate(pointer, count - 1, count))
            {
                return true;
            }
        }

        return false;
    }

    internal static void EndCommit(PackagingStation station, CommitFrame frame)
    {
        if (frame?.Session == null || station == null)
            return;

        try
        {
            var after = new StationSnapshot(station);
            var valid = frame.Before.IsOneCommitTo(after);
            var session = frame.Session;
            session.NativeCommitCalls++;
            if (valid)
                session.ValidNativeCommits++;
            else
                session.CommitMismatches++;

            WriteBoundedEvent(
                session,
                $"commit={session.NativeCommitCalls}; deltaValid={valid}; before=({frame.Before}); after=({after}).");
        }
        catch (Exception exception)
        {
            frame.Session.Exceptions++;
            Logger.Error($"[PackagingMk2] session={frame.Session.Id} post-commit capture failed: {exception}");
        }
    }

    internal static void ObserveToolEvent(PackagingTool tool, string method, EventFrame frame)
    {
        if (frame?.Session == null || tool == null)
            return;

        var session = frame.Session;
        try
        {
            switch (method)
            {
                case "DropProduct": session.DropProductCalls++; break;
                case "DeployPackaging": session.DeployPackagingCalls++; break;
                case "InsertIntoHopper": session.InsertIntoHopperCalls++; break;
                case "Finalize": session.FinalizeRequests++; break;
            }

            if (method == "Initialize")
            {
                WriteBoundedEvent(
                    session,
                    $"event=Initialize; displayedRemainingProduct={ReadDisplay(tool)}; " +
                    $"plannedLooseProduct={session.NativePlanLooseProduct}; " +
                    $"packagingObjects={Count(() => tool.PackagingInstances?.Count ?? -1)}; " +
                    $"productObjects={Count(() => tool.ProductInstances?.Count ?? -1)}.");
            }
            else
            {
                var detail =
                    $"event={method}; displayBefore={frame.DisplayBefore}; displayAfter={ReadDisplay(tool)}; " +
                    $"packagingRemaining={Safe(() => tool.ConcealedPackaging)}; productInHopper={Safe(() => tool.ProductInHopper)}; " +
                    $"packagingObjects={Count(() => tool.PackagingInstances?.Count ?? -1)}; " +
                    $"productObjects={Count(() => tool.ProductInstances?.Count ?? -1)}; " +
                    $"finalizedObjects={Count(() => tool.FinalizedPackaging?.Count ?? -1)}.";
                WriteBoundedEvent(session, detail);
            }

            if (method == "Deinitialize")
                Finish(session, tool, "native-task-stop", frame);
        }
        catch (Exception exception)
        {
            session.Exceptions++;
            Logger.Error($"[PackagingMk2] session={session.Id} read-only {method} observer failed; native behavior was not changed: {exception}");
            if (method == "Deinitialize")
                CloseAfterObserverFailure(session, "deinitialize-observer-failure");
        }
    }

    internal static EventFrame BeginToolEvent(PackagingTool tool, string method)
    {
        var session = GetSession(tool);
        if (session == null)
            return null;

        return new EventFrame
        {
            Session = session,
            Method = method,
            PackagingObjectsBefore = Count(() => tool.PackagingInstances?.Count ?? -1),
            ProductObjectsBefore = Count(() => tool.ProductInstances?.Count ?? -1),
            ConcealedPackagingBefore = Safe(() => tool.ConcealedPackaging),
            ProductInHopperBefore = Safe(() => tool.ProductInHopper),
            DisplayBefore = ReadDisplay(tool)
        };
    }

    internal static void RecordToolException(EventFrame frame, Exception exception)
    {
        if (frame?.Session == null || exception == null)
            return;

        frame.Session.Exceptions++;
        Logger.Error($"[PackagingMk2] session={frame.Session.Id} native tool method={frame.Method} threw: {exception}");
    }

    internal static void RecordCommitException(CommitFrame frame, Exception exception)
    {
        if (frame?.Session == null || exception == null)
            return;

        frame.Session.Exceptions++;
        Logger.Error($"[PackagingMk2] session={frame.Session.Id} PackSingleInstance threw: {exception}");
    }

    internal static void AbandonInitialization(Session session, Exception exception)
    {
        if (session == null || session.Closed)
            return;

        session.Exceptions++;
        Logger.Error($"[PackagingMk2] session={session.Id} native Initialize threw; observation session closed: {exception}");
        CloseAfterObserverFailure(session, "native-initialize-exception");
    }

    private static void Finish(Session session, PackagingTool tool, string stopPath, EventFrame frame)
    {
        if (session.Closed)
            return;

        session.Closed = true;
        try
        {
            var final = session.Station == null ? "station=<unavailable>" : new StationSnapshot(session.Station).ToString();
            var disposition = session.ValidNativeCommits >= session.NativePlanPackages
                ? "planned-native-commits-reached"
                : "stopped-with-uncommitted-native-plan";
            Logger.Info(
                $"[PackagingMk2] STOP session={session.Id}; path={stopPath}; disposition={disposition}; " +
                $"plannedPackages={session.NativePlanPackages}; plannedLooseProduct={session.NativePlanLooseProduct}; " +
                $"dropProductCalls={session.DropProductCalls}; deployPackagingCalls={session.DeployPackagingCalls}; " +
                $"insertIntoHopperCalls={session.InsertIntoHopperCalls}; finalizeRequests={session.FinalizeRequests}; " +
                $"nativeCommitCalls={session.NativeCommitCalls}; validNativeCommits={session.ValidNativeCommits}; " +
                $"commitMismatches={session.CommitMismatches}; safetyRefusedCommits={session.SafetyRefusedCommits}; exceptions={session.Exceptions}; " +
                $"objectsBeforeDeinitialize=packaging:{frame?.PackagingObjectsBefore ?? -1},products:{frame?.ProductObjectsBefore ?? -1}; " +
                $"toolBeforeDeinitialize=packagingRemaining:{frame?.ConcealedPackagingBefore ?? -1},productInHopper:{frame?.ProductInHopperBefore ?? -1}; " +
                $"finalStation=({final}); completionVsCancellation=not-exposed-by-native-stop-method.");
        }
        catch (Exception exception)
        {
            session.Exceptions++;
            Logger.Error($"[PackagingMk2] session={session.Id} stop summary capture failed; native behavior was not changed: {exception}");
        }
        finally
        {
            ByTool.TryRemove(session.ToolPointer, out _);
            ByStation.TryRemove(session.StationPointer, out _);
        }
    }

    private static void CloseAfterObserverFailure(Session session, string reason)
    {
        if (session.Closed)
            return;

        session.Closed = true;
        Logger.Error($"[PackagingMk2] session={session.Id} observer session closed; reason={reason}; capturedCommits={session.ValidNativeCommits}/{session.NativePlanPackages}.");
        ByTool.TryRemove(session.ToolPointer, out _);
        ByStation.TryRemove(session.StationPointer, out _);
    }

    private static void WriteBoundedEvent(Session session, string message)
    {
        if (session.DetailedEventsLogged >= MaxDetailedEvents)
            return;

        session.DetailedEventsLogged++;
        Logger.Info($"[PackagingMk2] session={session.Id}; {message}");
    }

    private static string ReadDisplay(PackagingTool tool)
    {
        try { return tool?.ProductCountText?.text ?? "<unavailable>"; }
        catch { return "<unavailable>"; }
    }

    private static string DescribeOutputRoom(PackagingStation station)
    {
        try
        {
            if (!PackagingMk2Safety.TryBuildPreflight(station, out var preflight, out var reason))
                return $"unknown(reason={reason},capacityProbe=packaged-output-copy)";

            return $"{preflight.OutputRoom}(capacityProbe=packaged-output-copy,Q={preflight.RecipeQuantity})";
        }
        catch (Exception exception)
        {
            return $"<unavailable:{exception.GetType().Name}>";
        }
    }

    private static int Count(Func<int> count)
    {
        try { return count(); }
        catch { return -1; }
    }

    private static int Safe(Func<int> value)
    {
        try { return value(); }
        catch { return -1; }
    }
}

internal static class PackagingMk2DiagnosticsPatch
{
    internal static void ReportPatchRegistration(HarmonyLib.Harmony harmony)
    {
        try
        {
            var initSignature = new[]
            {
                typeof(Task), typeof(FunctionalPackaging), typeof(int), typeof(ProductItemInstance), typeof(int)
            };
            var initialize = typeof(PackagingTool).GetMethod(
                nameof(PackagingTool.Initialize), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: initSignature, modifiers: null);
            var pack = typeof(PackagingStation).GetMethod(
                nameof(PackagingStation.PackSingleInstance), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            var transitions = new[] { "DropProduct", "DeployPackaging", "InsertIntoHopper", "Finalize", "Deinitialize" }
                .Select(name => typeof(PackagingTool).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .SingleOrDefault(method => method.Name == name))
                .Where(method => method != null)
                .ToArray();
            Logger.Info(
                $"[PackagingMk2] patch registration: initialize={IsPatched(initialize, harmony)} " +
                $"({Format(initialize)}); physicalTransitions={string.Join("|", transitions.Select(method => Format(method)))}; " +
                $"nativeCommit={IsPatched(pack, harmony)} ({Format(pack)}). Read-only observation only.");
        }
        catch (Exception exception)
        {
            Logger.Error($"[PackagingMk2] patch registration inspection failed: {exception}");
        }
    }

    private static bool IsPatched(MethodBase method, HarmonyLib.Harmony harmony)
    {
        var patches = method == null ? null : HarmonyLib.Harmony.GetPatchInfo(method);
        return patches?.Prefixes?.Any(patch => patch.owner == harmony.Id) == true
            && patches?.Postfixes?.Any(patch => patch.owner == harmony.Id) == true
            && patches?.Finalizers?.Any(patch => patch.owner == harmony.Id) == true;
    }

    private static string Format(MethodBase method) => method == null
        ? "<unresolved>"
        : $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
}

[HarmonyPatch]
internal static class PackagingMk2InitializeDiagnosticsPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        var signature = new[]
        {
            typeof(Task), typeof(FunctionalPackaging), typeof(int), typeof(ProductItemInstance), typeof(int)
        };
        return typeof(PackagingTool).GetMethod(
                   nameof(PackagingTool.Initialize),
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   types: signature,
                   modifiers: null)
               ?? throw new MissingMethodException(typeof(PackagingTool).FullName, "Initialize(Task,FunctionalPackaging,Int32,ProductItemInstance,Int32)");
    }

    [HarmonyPrefix]
    private static void Prefix(
        PackagingTool __instance,
        Task __0,
        FunctionalPackaging __1,
        int __2,
        ProductItemInstance __3,
        int __4,
        out PackagingMk2Diagnostics.Session __state)
    {
        __state = null;
        try
        {
            var task = __0?.TryCast<PackageProductTaskMk2>();
            if (task == null)
                return;

            PackagingMk2Diagnostics.Begin(__instance, task, __2, __4);
            __state = PackagingMk2Diagnostics.GetSession(__instance);
        }
        catch (Exception exception)
        {
            Logger.Error($"[PackagingMk2] initialize observer failed before native tool initialization: {exception}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(PackagingTool __instance, PackagingMk2Diagnostics.Session __state)
    {
        if (__state == null)
            return;
        try
        {
            var frame = PackagingMk2Diagnostics.BeginToolEvent(__instance, "Initialize");
            if (frame != null)
                PackagingMk2Diagnostics.ObserveToolEvent(__instance, "Initialize", frame);
        }
        catch (Exception exception)
        {
            PackagingMk2Diagnostics.AbandonInitialization(__state, exception);
        }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(PackagingMk2Diagnostics.Session __state, Exception __exception)
    {
        if (__exception != null && __state != null)
            PackagingMk2Diagnostics.AbandonInitialization(__state, __exception);
        return __exception;
    }
}

[HarmonyPatch]
internal static class PackagingMk2ToolTransitionDiagnosticsPatch
{
    private static readonly string[] ObservedMethods =
    {
        "DropProduct", "DeployPackaging", "InsertIntoHopper", "Finalize", "Deinitialize"
    };

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var name in ObservedMethods)
        {
            var method = typeof(PackagingTool).GetMethods(flags).SingleOrDefault(candidate => candidate.Name == name);
            if (method != null)
                yield return method;
        }
    }

    [HarmonyPrefix]
    private static void Prefix(PackagingTool __instance, MethodBase __originalMethod, out PackagingMk2Diagnostics.EventFrame __state)
    {
        __state = null;
        try { __state = PackagingMk2Diagnostics.BeginToolEvent(__instance, __originalMethod.Name); }
        catch (Exception exception) { Logger.Error($"[PackagingMk2] {__originalMethod.Name} pre-observer failed; native behavior was not changed: {exception}"); }
    }

    [HarmonyPostfix]
    private static void Postfix(PackagingTool __instance, MethodBase __originalMethod, PackagingMk2Diagnostics.EventFrame __state)
    {
        try { PackagingMk2Diagnostics.ObserveToolEvent(__instance, __originalMethod.Name, __state); }
        catch (Exception exception) { Logger.Error($"[PackagingMk2] {__originalMethod.Name} post-observer failed; native behavior was not changed: {exception}"); }
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(PackagingMk2Diagnostics.EventFrame __state, Exception __exception)
    {
        PackagingMk2Diagnostics.RecordToolException(__state, __exception);
        return __exception;
    }
}

[HarmonyPatch]
internal static class PackagingMk2NativeCommitDiagnosticsPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        return typeof(PackagingStation).GetMethod(
                   nameof(PackagingStation.PackSingleInstance),
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   types: Type.EmptyTypes,
                   modifiers: null)
               ?? throw new MissingMethodException(typeof(PackagingStation).FullName, "PackSingleInstance()");
    }

    [HarmonyPrefix]
    private static void Prefix(PackagingStation __instance, out PackagingMk2Diagnostics.CommitFrame __state)
    {
        __state = PackagingMk2Diagnostics.BeginCommit(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(PackagingStation __instance, PackagingMk2Diagnostics.CommitFrame __state)
    {
        PackagingMk2Diagnostics.EndCommit(__instance, __state);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(PackagingMk2Diagnostics.CommitFrame __state, Exception __exception)
    {
        PackagingMk2Diagnostics.RecordCommitException(__state, __exception);
        return __exception;
    }
}
#endif
