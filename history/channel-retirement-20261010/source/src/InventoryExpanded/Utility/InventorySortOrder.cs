namespace Backpack;

internal enum InventorySortGroup
{
    PackagedProduct,
    UnpackagedProduct,
    Other
}

internal static class InventorySortOrder
{
    public static int Compare(
        InventorySortGroup leftGroup,
        string leftId,
        string leftName,
        int leftSourceIndex,
        InventorySortGroup rightGroup,
        string rightId,
        string rightName,
        int rightSourceIndex)
    {
        var groupComparison = leftGroup.CompareTo(rightGroup);
        if (groupComparison != 0)
            return groupComparison;

        var idComparison = string.Compare(leftId, rightId, StringComparison.OrdinalIgnoreCase);
        if (idComparison != 0)
            return idComparison;

        var nameComparison = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        return nameComparison != 0 ? nameComparison : leftSourceIndex.CompareTo(rightSourceIndex);
    }
}
