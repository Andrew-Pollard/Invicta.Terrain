// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using SkiaSharp;

namespace Invicta.Rendering;

/// <summary>Saves what has been painted as PNG files.</summary>
internal static class PngFile
{
    /// <summary>Saves a surface's current contents as a PNG file.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="path">The path of the file to create.</param>
    public static void Save(SKSurface surface, string path)
    {
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }
}
