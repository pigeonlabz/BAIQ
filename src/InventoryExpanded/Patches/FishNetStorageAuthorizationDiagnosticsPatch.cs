#if FISHNET_AUTH_DIAGNOSTICS
using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;

namespace Backpack.Patches;

/// <summary>
/// Read-only observation at Schedule I's generated server RPC readers and the
/// storage setters they dispatch. The reader's final NetworkConnection is the
/// FishNet-supplied sender; the stored-instance setter's connection argument is
/// logged separately and is never treated as authenticated identity.
/// </summary>
internal static class FishNetStorageAuthorizationTrace
{
    internal sealed class RpcScope
    {
        public RpcScope Previous;
        public StorageEntity Storage;
        public IntPtr StoragePointer;
        public string RpcName;
        public NetworkConnection ReaderSender;
        public string TargetDescription;
        public int MutationCalls;
        public string LastMutationOutcome = "none";
    }

    internal sealed class MutationScope
    {
        public StorageEntity Storage;
        public string Method;
        public int SlotIndex;
        public SlotSummary Before;
        public RpcScope Rpc;
        public bool SameTarget;
    }

    internal readonly struct SlotSummary
    {
        public SlotSummary(bool valid, bool occupied, int quantity)
        {
            Valid = valid;
            Occupied = occupied;
            Quantity = quantity;
        }

        public bool Valid { get; }
        public bool Occupied { get; }
        public int Quantity { get; }

        public string Describe() => Valid
            ? $"valid=true,occupied={Occupied},quantity={Quantity}"
            : "valid=false,occupied=unknown,quantity=unknown";

        public bool SamePresenceAndQuantity(SlotSummary other) =>
            Valid == other.Valid && (!Valid || (Occupied == other.Occupied && Quantity == other.Quantity));
    }

    [ThreadStatic]
    private static RpcScope _currentRpc;

    private static int _sequence;

    public static RpcScope BeginReader(string rpcName, StorageEntity storage, NetworkConnection readerSender)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return null;

        try
        {
            var scope = new RpcScope
            {
                Previous = _currentRpc,
                Storage = storage,
                StoragePointer = storage?.Pointer ?? IntPtr.Zero,
                RpcName = rpcName,
                ReaderSender = readerSender,
                TargetDescription = DescribeTarget(storage, readerSender)
            };
            _currentRpc = scope;
            Log(scope, "rpc-reader-enter",
                $"senderSource=FishNet-reader-final-argument;{scope.TargetDescription}");
            return scope;
        }
        catch (Exception exception)
        {
            Logger.Warning("[FishNetAuthTrace] Reader-entry diagnostics failed safely: {0}.", exception.GetType().Name);
            return null;
        }
    }

    public static Exception EndReader(RpcScope scope, Exception exception)
    {
        if (scope == null)
            return exception;

        try
        {
            Log(scope, "rpc-reader-exit",
                $"readerOutcome={(exception == null ? "returned" : "threw:" + exception.GetType().Name)},mutationSetterCalls={scope.MutationCalls},lastMutationOutcome={scope.LastMutationOutcome};{scope.TargetDescription}");
        }
        catch (Exception diagnosticException)
        {
            Logger.Warning("[FishNetAuthTrace] Reader-exit diagnostics failed safely: {0}.", diagnosticException.GetType().Name);
        }
        finally
        {
            if (ReferenceEquals(_currentRpc, scope))
                _currentRpc = scope.Previous;
        }

        return exception;
    }

    public static MutationScope BeginMutation(
        string method,
        StorageEntity storage,
        int slotIndex,
        NetworkConnection connectionArgument = null)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return null;

        try
        {
            var rpc = _currentRpc;
            var sameTarget = rpc != null && rpc.StoragePointer != IntPtr.Zero
                && storage != null && storage.Pointer == rpc.StoragePointer;
            var scope = new MutationScope
            {
                Storage = storage,
                Method = method,
                SlotIndex = slotIndex,
                Before = ReadSlot(storage, slotIndex),
                Rpc = rpc,
                SameTarget = sameTarget
            };

            if (sameTarget)
            {
                rpc.MutationCalls++;
                rpc.LastMutationOutcome = method + ":entered";
            }

            var connectionArgumentDetails = connectionArgument == null
                ? "setStoredInstanceConnectionArgument=not-present"
                : $"setStoredInstanceConnectionArgumentClientId={ConnectionClientId(connectionArgument)},connectionArgumentMatchesReaderSender={CompareConnections(connectionArgument, rpc?.ReaderSender)}";
            Log(rpc, "storage-setter-enter",
                $"setter={method},slotIndex={slotIndex},readerContext={(rpc == null ? "none" : sameTarget ? "same-storage" : "different-storage")},senderSource={(rpc == null ? "unavailable-outside-reader" : "FishNet-reader-final-argument")},readerSenderClientId={ConnectionClientId(rpc?.ReaderSender)},slotBefore=({scope.Before.Describe()}),{connectionArgumentDetails}");
            return scope;
        }
        catch (Exception exception)
        {
            Logger.Warning("[FishNetAuthTrace] Storage-setter entry diagnostics failed safely: {0}.", exception.GetType().Name);
            return null;
        }
    }

    public static Exception EndMutation(MutationScope scope, Exception exception)
    {
        if (scope == null)
            return exception;

        try
        {
            var after = ReadSlot(scope.Storage, scope.SlotIndex);
            var summaryChanged = !scope.Before.SamePresenceAndQuantity(after);
            var outcome = exception == null
                ? summaryChanged ? "setter-returned-slot-summary-changed" : "setter-returned-no-presence-or-quantity-delta"
                : "setter-threw:" + exception.GetType().Name;

            if (scope.SameTarget && scope.Rpc != null)
                scope.Rpc.LastMutationOutcome = outcome;

            Log(scope.Rpc, "storage-setter-exit",
                $"setter={scope.Method},slotIndex={scope.SlotIndex},setterOutcome={(exception == null ? "returned" : "threw:" + exception.GetType().Name)},slotBefore=({scope.Before.Describe()}),slotAfter=({after.Describe()}),presenceOrQuantityChanged={summaryChanged},exactItemInstanceChangeObserved=false");
        }
        catch (Exception diagnosticException)
        {
            Logger.Warning("[FishNetAuthTrace] Storage-setter exit diagnostics failed safely: {0}.", diagnosticException.GetType().Name);
        }

        return exception;
    }

    private static SlotSummary ReadSlot(StorageEntity storage, int slotIndex)
    {
        try
        {
            if (storage?.ItemSlots == null || slotIndex < 0 || slotIndex >= storage.ItemSlots.Count)
                return new SlotSummary(false, false, 0);

            var slot = storage.ItemSlots[new Index(slotIndex)];
            if (slot == null || slot.ItemInstance == null)
                return new SlotSummary(true, false, 0);

            return new SlotSummary(true, true, Math.Max(0, slot.Quantity));
        }
        catch
        {
            return new SlotSummary(false, false, 0);
        }
    }

    private static string DescribeTarget(StorageEntity storage, NetworkConnection readerSender)
    {
        try
        {
            if (storage == null)
                return $"storage=none,readerSenderClientId={ConnectionClientId(readerSender)}";

            var targetPlayer = storage.gameObject?.GetComponent<Player>();
            var storageNetworkObject = storage.NetworkObject;
            var playerNetworkObject = targetPlayer?.NetworkObject;
            var storageOwner = storageNetworkObject?.Owner;
            var playerOwner = playerNetworkObject?.Owner;
            var storageNameIsBackpack = string.Equals(
                storage.StorageEntityName,
                PlayerBackpack.StorageName,
                StringComparison.Ordinal);

            return $"role=({BackpackPersistenceDiagnostics.DescribeRole()}),storageNetworkObjectId={NetworkObjectId(storageNetworkObject)},storageOwnerClientId={ConnectionClientId(storageOwner)},targetPlayerKey={(targetPlayer == null ? "none" : BackpackPersistenceDiagnostics.IdentityKey(targetPlayer.PlayerCode))},playerNetworkObjectId={NetworkObjectId(playerNetworkObject)},playerOwnerClientId={ConnectionClientId(playerOwner)},storageIsAttachedToPlayer={(targetPlayer != null)},storageNameIsBackpack={storageNameIsBackpack},senderClientId={ConnectionClientId(readerSender)},senderMatchesStorageOwner={CompareConnections(readerSender, storageOwner)},senderMatchesPlayerOwner={CompareConnections(readerSender, playerOwner)},storageOwnerMatchesPlayerOwner={CompareConnections(storageOwner, playerOwner)}";
        }
        catch (Exception exception)
        {
            return $"targetDescription=unavailable,error={exception.GetType().Name},senderClientId={ConnectionClientId(readerSender)}";
        }
    }

    private static int NetworkObjectId(Il2CppFishNet.Object.NetworkObject networkObject)
    {
        try { return networkObject == null ? -1 : networkObject.ObjectId; }
        catch { return -1; }
    }

    private static string ConnectionClientId(NetworkConnection connection)
    {
        try { return connection == null || connection.Pointer == IntPtr.Zero ? "none" : connection.ClientId.ToString(); }
        catch { return "unavailable"; }
    }

    private static string CompareConnections(NetworkConnection first, NetworkConnection second)
    {
        try
        {
            if (first == null || second == null || first.Pointer == IntPtr.Zero || second.Pointer == IntPtr.Zero)
                return "unknown";
            return first.Pointer == second.Pointer ? "yes" : "no";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void Log(RpcScope scope, string eventName, string details)
    {
        if (!Logger.DiagnosticLoggingEnabled)
            return;

        var sequence = Interlocked.Increment(ref _sequence);
        var rpc = scope?.RpcName ?? "outside-reader";
        Logger.Diagnostic($"[FishNetAuthTrace] seq={sequence} event={eventName} rpc={rpc} {details};itemPayloadContentsLogged=false;steamIdsLogged=false.");
    }
}

[HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetStoredInstance_2652194801", typeof(PooledReader), typeof(Channel), typeof(NetworkConnection))]
internal static class FishNetSetStoredInstanceReaderDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(StorageEntity __instance, NetworkConnection conn, out FishNetStorageAuthorizationTrace.RpcScope __state)
    {
        __state = FishNetStorageAuthorizationTrace.BeginReader("SetStoredInstance", __instance, conn);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, FishNetStorageAuthorizationTrace.RpcScope __state)
    {
        return FishNetStorageAuthorizationTrace.EndReader(__state, __exception);
    }
}

[HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetItemSlotQuantity_1692629761", typeof(PooledReader), typeof(Channel), typeof(NetworkConnection))]
internal static class FishNetSetItemSlotQuantityReaderDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(StorageEntity __instance, NetworkConnection conn, out FishNetStorageAuthorizationTrace.RpcScope __state)
    {
        __state = FishNetStorageAuthorizationTrace.BeginReader("SetItemSlotQuantity", __instance, conn);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, FishNetStorageAuthorizationTrace.RpcScope __state)
    {
        return FishNetStorageAuthorizationTrace.EndReader(__state, __exception);
    }
}

[HarmonyPatch(typeof(StorageEntity), "SetStoredInstance_Internal", typeof(NetworkConnection), typeof(int), typeof(ItemInstance))]
internal static class FishNetStoredInstanceMutationDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(StorageEntity __instance, NetworkConnection conn, int itemSlotIndex, out FishNetStorageAuthorizationTrace.MutationScope __state)
    {
        __state = FishNetStorageAuthorizationTrace.BeginMutation("SetStoredInstance_Internal", __instance, itemSlotIndex, conn);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, FishNetStorageAuthorizationTrace.MutationScope __state)
    {
        return FishNetStorageAuthorizationTrace.EndMutation(__state, __exception);
    }
}

[HarmonyPatch(typeof(StorageEntity), "SetItemSlotQuantity_Internal", typeof(int), typeof(int))]
internal static class FishNetQuantityMutationDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(StorageEntity __instance, int itemSlotIndex, out FishNetStorageAuthorizationTrace.MutationScope __state)
    {
        __state = FishNetStorageAuthorizationTrace.BeginMutation("SetItemSlotQuantity_Internal", __instance, itemSlotIndex);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, FishNetStorageAuthorizationTrace.MutationScope __state)
    {
        return FishNetStorageAuthorizationTrace.EndMutation(__state, __exception);
    }
}
#endif
