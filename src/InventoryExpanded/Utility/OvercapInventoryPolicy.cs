using Backpack.Cheats;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.ItemFramework;
#elif MONO
using ScheduleOne.ItemFramework;
#endif

namespace Backpack;

internal static class StackCapacityPolicy
{
    // ItemInstance.Write serializes Quantity as an unsigned 16-bit integer.
    public const int SerializationSafeMaximum = ushort.MaxValue;

    public static bool TryGetConfiguredLimit(ItemInstance item, out int configuredLimit)
    {
        configuredLimit = 0;
        if (item == null || IsCash(item))
            return false;

        try
        {
#if IL2CPP
            var nativeLimit = item.Definition?.StackLimit ?? 0;
#elif MONO
            var nativeLimit = item.StackLimit;
#endif
            if (nativeLimit <= 1)
                return false;

            configuredLimit = (int)Math.Min(
                (long)nativeLimit * CheatSettings.StackSizeMultiplierValue,
                SerializationSafeMaximum);
            return configuredLimit > 1;
        }
        catch
        {
            configuredLimit = 0;
            return false;
        }
    }

    public static bool IsOversized(ItemInstance item, out int configuredLimit)
    {
        return TryGetConfiguredLimit(item, out configuredLimit) && item.Quantity > configuredLimit;
    }

    private static bool IsCash(ItemInstance item)
    {
#if IL2CPP
        try
        {
            return item.TryCast<CashInstance>() != null;
        }
        catch
        {
            return false;
        }
#elif MONO
        return item is CashInstance;
#endif
    }
}

internal static class OvercapTransferContext
{
    private static int _configuredLimit;

#if IL2CPP
    private static IntPtr _sourcePointer;
#elif MONO
    private static ItemInstance _sourceItem;
#endif

    public static bool IsActive { get; private set; }
    public static bool Begin(ItemSlot sourceSlot)
    {
        End();

        var item = sourceSlot?.ItemInstance;
        if (!StackCapacityPolicy.IsOversized(item, out var configuredLimit))
            return false;

        IsActive = true;
        _configuredLimit = configuredLimit;
#if IL2CPP
        _sourcePointer = item.Pointer;
#elif MONO
        _sourceItem = item;
#endif
        return true;
    }

#if IL2CPP
    public static bool TryGetNormalizingLimit(BaseItemInstance item, out int configuredLimit)
    {
        configuredLimit = _configuredLimit;
        return IsActive && item != null && item.Pointer == _sourcePointer;
    }

    public static bool TryApplySourceDecrease(BaseItemInstance item, int change)
    {
        if (change >= 0 || !TryGetNormalizingLimit(item, out var configuredLimit) || item.Quantity <= configuredLimit)
            return false;

        var nextQuantity = (long)item.Quantity + change;
        if (nextQuantity < 0 || nextQuantity > int.MaxValue)
            return false;

        item.SetQuantity((int)nextQuantity);
        return true;
    }
#endif

    public static void End()
    {
        IsActive = false;
        _configuredLimit = 0;
#if IL2CPP
        _sourcePointer = IntPtr.Zero;
#elif MONO
        _sourceItem = null;
#endif
    }
}
