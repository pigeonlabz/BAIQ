namespace Backpack;

internal enum CashStorageKind
{
    Unsupported,
    GeneralPlaceableStorage,
    EmployeeLocker,
    DeadDrop,
    SupplierStash,
    SpecialPlaceableStorage
}

internal static class CashRoutingPolicy
{
    public static bool AllowsExternalStorage(CashStorageKind kind) => kind is
        CashStorageKind.GeneralPlaceableStorage or
        CashStorageKind.EmployeeLocker or
        CashStorageKind.DeadDrop or
        CashStorageKind.SupplierStash;

    public static float GetTransferAmount(
        float sourceBalance,
        float destinationBalance,
        float destinationLimit,
        bool destinationAllowed)
    {
        if (!destinationAllowed || !float.IsFinite(sourceBalance) || !float.IsFinite(destinationBalance)
            || !float.IsFinite(destinationLimit) || sourceBalance <= 0f || destinationBalance < 0f
            || destinationLimit <= destinationBalance)
            return 0f;

        return Math.Min(sourceBalance, destinationLimit - destinationBalance);
    }
}
