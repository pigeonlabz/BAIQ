namespace Backpack.Cheats;

/// <summary>
/// Plans additional native Mk1 single-package transactions. The native call
/// still creates each ProductItemInstance and applies its PackagingDefinition;
/// this policy only limits how many complete native transactions may follow.
/// </summary>
internal static class PackagingProductionPolicy
{
    public static int GetTargetPackages(int multiplier, bool enabled)
    {
        return enabled && multiplier is 2 or 4 or 8 ? multiplier : 1;
    }

    public static int GetAdditionalPackages(
        int multiplier,
        bool enabled,
        int packagesAlreadyCommitted,
        int availablePackagingUnits,
        int availableLooseProduct,
        int looseProductPerPackage,
        int outputRoom)
    {
        if (packagesAlreadyCommitted < 0
            || availablePackagingUnits <= 0
            || availableLooseProduct <= 0
            || looseProductPerPackage <= 0
            || outputRoom <= 0)
        {
            return 0;
        }

        var remainingTarget = GetTargetPackages(multiplier, enabled) - packagesAlreadyCommitted;
        if (remainingTarget <= 0)
            return 0;

        var productLimited = availableLooseProduct / looseProductPerPackage;
        return Math.Min(remainingTarget,
            Math.Min(availablePackagingUnits, Math.Min(productLimited, outputRoom)));
    }

    public static bool IsOneNativePackageCommitted(
        int packagingBefore,
        int packagingAfter,
        int productBefore,
        int productAfter,
        int looseProductPerPackage,
        int outputBefore,
        int outputAfter)
    {
        if (packagingBefore <= 0 || productBefore < looseProductPerPackage || looseProductPerPackage <= 0)
            return false;

        return (long)packagingBefore - packagingAfter == 1
            && (long)productBefore - productAfter == looseProductPerPackage
            && (long)outputAfter - outputBefore == 1;
    }
}
