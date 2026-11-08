#if IL2CPP
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
#elif MONO
using ScheduleOne.PlayerScripts;
using ScheduleOne.Storage;
#endif

namespace Backpack;

public static class PlayerExtensions
{
    public static StorageEntity GetBackpackStorage(this Player player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        var backpackStorage = player.gameObject.GetComponent<StorageEntity>();
        if (backpackStorage == null)
            throw new InvalidOperationException("Player does not have a BackpackStorage component.");

        return backpackStorage;
    }
}