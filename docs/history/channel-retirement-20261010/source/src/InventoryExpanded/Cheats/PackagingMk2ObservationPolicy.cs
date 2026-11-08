namespace Backpack.Cheats;

/// <summary>
/// Read-only model of the Mk2 plan computed by PackageProductTaskMk2. It is
/// used by diagnostics and managed fixtures only; it never changes native task
/// quantities or station state.
/// </summary>
internal readonly record struct PackagingMk2Plan(int PackageCount, int LooseProductCount);

internal static class PackagingMk2ObservationPolicy
{
    public static PackagingMk2Plan EstimateNativePlan(
        int availablePackagingUnits,
        int availableLooseProduct,
        int looseProductPerPackage)
    {
        if (availablePackagingUnits <= 0 || availableLooseProduct <= 0 || looseProductPerPackage <= 0)
            return default;

        var completeProductLimitedPackages = availableLooseProduct / looseProductPerPackage;
        var packageCount = Math.Min(availablePackagingUnits, completeProductLimitedPackages);
        return new PackagingMk2Plan(packageCount, checked(packageCount * looseProductPerPackage));
    }

    public static bool IsOneNativePackageCommitted(
        int packagingBefore,
        int packagingAfter,
        int looseProductBefore,
        int looseProductAfter,
        int outputBefore,
        int outputAfter,
        int looseProductPerPackage)
    {
        return looseProductPerPackage > 0
            && packagingAfter == packagingBefore - 1
            && looseProductAfter == looseProductBefore - looseProductPerPackage
            && outputAfter == outputBefore + 1;
    }

    /// <summary>
    /// Bounds an existing native Mk2 task plan to complete, available packages
    /// that can fit in the destination. A zero plan is never passed to native
    /// initialization; callers leave the native plan intact and rely on the
    /// per-commit guard if the live state cannot be modeled safely.
    /// </summary>
    public static PackagingMk2Plan ClampPlanToSafeCapacity(
        int nativePackageCount,
        int nativeLooseProductCount,
        int availablePackaging,
        int availableLooseProduct,
        int looseProductPerPackage,
        int outputRoom)
    {
        if (nativePackageCount <= 0 || nativeLooseProductCount <= 0
            || availablePackaging <= 0 || availableLooseProduct <= 0
            || looseProductPerPackage <= 0 || outputRoom <= 0)
            return default;

        var completeNativeProductPackages = nativeLooseProductCount / looseProductPerPackage;
        var resourceProductPackages = availableLooseProduct / looseProductPerPackage;
        var packages = Math.Min(nativePackageCount,
            Math.Min(availablePackaging,
                Math.Min(completeNativeProductPackages,
                    Math.Min(resourceProductPackages, outputRoom))));
        return packages <= 0
            ? default
            : new PackagingMk2Plan(packages, checked(packages * looseProductPerPackage));
    }

    public static bool CanCommitNativePackage(
        int availablePackaging,
        int availableLooseProduct,
        int looseProductPerPackage,
        int outputRoom,
        bool inputRemovalAllowed,
        bool outputAdditionAllowed) =>
        inputRemovalAllowed
        && outputAdditionAllowed
        && availablePackaging >= 1
        && looseProductPerPackage > 0
        && availableLooseProduct >= looseProductPerPackage
        && outputRoom >= 1;
}
