namespace Backpack;

internal static class QuickMoveDestinationPriorityPolicy
{
    public static bool IsPreferredDestination(bool destinationIsBackpack, bool backpackFirst)
    {
        return destinationIsBackpack == backpackFirst;
    }

    public static bool ShouldDeferFallback(int preferredRemainingCapacity)
    {
        return preferredRemainingCapacity > 0;
    }
}
