#if IL2CPP
using Il2CppScheduleOne.ItemFramework;
#elif MONO
using ScheduleOne.ItemFramework;
#endif

namespace Backpack;

// Quantity checks remain separate from Sort writes so failed plans never mutate inventory.
public static class DiagnosticTrace
{
    public static Dictionary<string, int> GetTotals(IEnumerable<ItemSlot> slots)
    {
        return GetTotals(slots.Select(slot => slot?.ItemInstance));
    }

    public static Dictionary<string, int> GetTotals(IEnumerable<ItemInstance> items)
    {
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item == null)
                continue;

            var id = item.ID ?? "<null-id>";
            totals[id] = totals.GetValueOrDefault(id) + item.Quantity;
        }

        return totals;
    }

    public static bool TotalsMatch(Dictionary<string, int> left, Dictionary<string, int> right)
    {
        return left.Count == right.Count
               && left.All(pair => right.TryGetValue(pair.Key, out var quantity) && quantity == pair.Value);
    }
}
