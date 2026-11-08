namespace Backpack;

internal static class BackpackPersistencePolicy
{
    private const string PayloadDelimiter = "|||";

    internal static bool CanWriteBackpack(bool isOwner, bool storageAvailable, bool storageBoundToPlayer)
    {
        return isOwner && storageAvailable && storageBoundToPlayer;
    }

    internal static bool IsValidPlayerPathIndex(int index, int playerDataCount, int playerPathCount)
    {
        return index >= 0 && index < playerDataCount && index < playerPathCount;
    }

#if SCHEDULE_I_047
    internal static bool IsValidPlayerDataIndex(int index, int playerDataCount)
    {
        return index >= 0 && index < playerDataCount;
    }
#endif

    internal static bool IsMatchingPlayerIdentity(string requestedCode, string returnedCode, string indexedCode)
    {
        return !string.IsNullOrWhiteSpace(requestedCode)
               && !string.IsNullOrWhiteSpace(returnedCode)
               && string.Equals(requestedCode, returnedCode, StringComparison.Ordinal)
               && string.Equals(returnedCode, indexedCode, StringComparison.Ordinal);
    }

#if SCHEDULE_I_047
    internal static bool IsMatchingPlayerFileIdentity(string expectedCode, string actualCode)
    {
        return !string.IsNullOrWhiteSpace(expectedCode)
               && !string.IsNullOrWhiteSpace(actualCode)
               && string.Equals(expectedCode, actualCode, StringComparison.Ordinal);
    }

    internal static bool IsPathWithinRoot(string candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
            return false;

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var candidate = Path.GetFullPath(candidatePath);
            return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
#endif

    internal static bool TryExtractBackpackPayload(
        string contents,
        out string nativeInventory,
        out string backpackPayload)
    {
        nativeInventory = contents;
        backpackPayload = null;

        if (string.IsNullOrEmpty(contents))
            return false;

        var delimiterIndex = contents.IndexOf(PayloadDelimiter, StringComparison.Ordinal);
        if (delimiterIndex < 0)
            return false;

        nativeInventory = contents[..delimiterIndex];
        backpackPayload = contents[(delimiterIndex + PayloadDelimiter.Length)..];
        return true;
    }
}
