using System.Security.Cryptography;

namespace Backpack.Compatibility;

internal enum LegacyConflictConfidence
{
    None,
    Possible,
    Positive
}

internal static class LegacyConflictPolicy
{
    private static readonly string[] KnownLegacyNames =
    [
        "OG Backpack",
        "Backpack & Inventory QoL",
        "Backpack and Inventory QoL",
        "Inventory Expanded"
    ];

    private static readonly string[] ApparentBackpackDllNames =
    [
        "Backpack.dll",
        "BackpackMod.dll",
        "OGBackpack.dll",
        "BackpackInventoryQoL.dll",
        "InventoryExpanded.dll"
    ];

    public static LegacyConflictConfidence Classify(
        string assemblyName,
        string typeName,
        string modName,
        string author,
        string nexusModId)
    {
        if (EqualsIgnoreCase(assemblyName, "Backpack")
            || EqualsIgnoreCase(assemblyName, "BackpackInventoryQoL")
            || EqualsIgnoreCase(typeName, "Backpack.BackpackMod")
            || EqualsIgnoreCase(nexusModId, "818"))
        {
            return LegacyConflictConfidence.Positive;
        }

        if (!KnownLegacyNames.Any(name => EqualsIgnoreCase(modName, name)))
            return LegacyConflictConfidence.None;

        return author?.Contains("D-Kay", StringComparison.OrdinalIgnoreCase) == true
            ? LegacyConflictConfidence.Positive
            : LegacyConflictConfidence.Possible;
    }

    public static bool IsApparentBackpackDll(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fileName = Path.GetFileName(path);
        return ApparentBackpackDllNames.Any(name => EqualsIgnoreCase(fileName, name));
    }

    public static bool TryFingerprintActiveDll(
        string sourcePath,
        string modsDirectory,
        out string fingerprint,
        out string error)
    {
        fingerprint = null;
        if (!TryValidateActiveDllPath(sourcePath, modsDirectory, out var fullSourcePath, out error))
            return false;

        try
        {
            using var stream = File.OpenRead(fullSourcePath);
            using var sha256 = SHA256.Create();
            fingerprint = Convert.ToHexString(sha256.ComputeHash(stream));
            return true;
        }
        catch (Exception exception)
        {
            error = $"Could not verify the conflicting DLL: {exception.Message}";
            return false;
        }
    }

    public static bool TryDisable(
        string sourcePath,
        string modsDirectory,
        string expectedFingerprint,
        out string destinationPath,
        out string error)
    {
        destinationPath = null;
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            error = "The conflicting DLL was not positively fingerprinted, so it was not moved.";
            return false;
        }

        if (!TryValidateActiveDllPath(sourcePath, modsDirectory, out var fullSourcePath, out error))
            return false;

        if (!TryFingerprintActiveDll(fullSourcePath, modsDirectory, out var currentFingerprint, out error))
            return false;
        if (!string.Equals(expectedFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            error = "The conflicting DLL changed after detection, so it was not moved.";
            return false;
        }

        try
        {
            var disabledDirectory = Path.Combine(Path.GetFullPath(modsDirectory), "Disabled");
            Directory.CreateDirectory(disabledDirectory);
            destinationPath = ChooseAvailableDestination(disabledDirectory, Path.GetFileName(fullSourcePath));
            File.Move(fullSourcePath, destinationPath, false);
            return true;
        }
        catch (Exception exception)
        {
            destinationPath = null;
            error = $"Could not move the conflicting DLL: {exception.Message}";
            return false;
        }
    }

    internal static string ChooseAvailableDestination(string disabledDirectory, string fileName)
    {
        var destination = Path.Combine(disabledDirectory, fileName);
        if (!File.Exists(destination))
            return destination;

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 1; suffix < 10000; suffix++)
        {
            destination = Path.Combine(disabledDirectory, $"{baseName} ({suffix}){extension}");
            if (!File.Exists(destination))
                return destination;
        }

        throw new IOException("No safe unused filename was available in Mods\\Disabled.");
    }

    private static bool TryValidateActiveDllPath(
        string sourcePath,
        string modsDirectory,
        out string fullSourcePath,
        out string error)
    {
        fullSourcePath = null;
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(modsDirectory))
        {
            error = "The conflicting DLL path could not be verified.";
            return false;
        }

        try
        {
            fullSourcePath = Path.GetFullPath(sourcePath);
            var fullModsDirectory = Path.GetFullPath(modsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sourceDirectory = Path.GetDirectoryName(fullSourcePath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(sourceDirectory, fullModsDirectory, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(fullSourcePath), ".dll", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullSourcePath))
            {
                error = "The detected file is not a verified active DLL directly inside the Mods folder.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = $"The conflicting DLL path could not be verified: {exception.Message}";
            return false;
        }
    }

    private static bool EqualsIgnoreCase(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
