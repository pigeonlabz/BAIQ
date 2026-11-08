using UnityEngine;

namespace Backpack;

public static class ResourceUtils
{
    public static bool TryLoadTexture(string resourcePath, out Texture2D texture)
    {
        texture = null;
        using var resourceStream = typeof(ResourceUtils).Assembly.GetManifestResourceStream(resourcePath);
        if (resourceStream == null)
        {
            Logger.Error($"Failed to load resource: {resourcePath}");
            return false;
        }

        var buffer = new byte[resourceStream.Length];
        resourceStream.Read(buffer, 0, buffer.Length);
        texture = new Texture2D(0, 0);
        texture.filterMode = FilterMode.Point;
        texture.LoadImage(buffer);
        return true;
    }
}