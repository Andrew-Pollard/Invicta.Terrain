// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using SkiaSharp;

namespace Invicta.Rendering;

/// <summary>Mixes and shades opaque colors.</summary>
internal static class ColorMath
{
    /// <summary>Mixes two colors, from all of the first at zero to all of the second at one.</summary>
    public static SKColor Blend(SKColor first, SKColor second, double amount)
    {
        return new SKColor(
            Mix(first.Red, second.Red, amount),
            Mix(first.Green, second.Green, amount),
            Mix(first.Blue, second.Blue, amount));
    }

    /// <summary>Darkens or brightens a color by a factor, clamping each channel.</summary>
    public static SKColor Shade(SKColor color, double factor)
    {
        return new SKColor(Scale(color.Red, factor), Scale(color.Green, factor), Scale(color.Blue, factor));
    }

    private static byte Mix(byte first, byte second, double amount)
    {
        return (byte)Math.Round(first + ((second - first) * amount));
    }

    private static byte Scale(byte channel, double factor)
    {
        return (byte)Math.Clamp(Math.Round(channel * factor), 0, 255);
    }
}
