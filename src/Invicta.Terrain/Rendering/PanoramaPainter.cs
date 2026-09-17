// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Visibility;

using SkiaSharp;

namespace Invicta.Rendering;

/// <summary>Paints a <see cref="Panorama"/> as a picture: shaded terrain fading into haze, under a sky.</summary>
public static class PanoramaPainter
{
    // Aerial perspective: terrain fades halfway to the haze color about every 40 km.
    private const double HazeDistance = 60_000;

    // A pixel in front of terrain at least this much further away is drawn as a ridge line.
    private const double RidgeDistanceRatio = 1.25;

    private static readonly SKColor s_skyTop = new(96, 150, 210);
    private static readonly SKColor s_skyHorizon = new(214, 228, 240);
    private static readonly SKColor s_haze = new(190, 205, 222);
    private static readonly SKColor s_ridgeLine = new(35, 40, 45);

    // Terrain colors by height, from lowland greens to bare summit rock.
    private static readonly (double Height, SKColor Color)[] s_heightColors =
    [
        (0, new SKColor(62, 92, 56)),
        (300, new SKColor(96, 112, 64)),
        (700, new SKColor(128, 116, 86)),
        (1100, new SKColor(150, 144, 136)),
        (1400, new SKColor(190, 188, 186)),
    ];

    /// <summary>Paints a panorama into a new bitmap.</summary>
    /// <param name="panorama">The panorama.</param>
    /// <returns>A bitmap the size of the panorama, which the caller must dispose.</returns>
    public static SKBitmap Paint(Panorama panorama)
    {
        ArgumentNullException.ThrowIfNull(panorama);

        SKColor[] colors = new SKColor[panorama.Width * panorama.Height];
        Parallel.For(0, panorama.Height, y =>
        {
            for (int x = 0; x < panorama.Width; x++)
            {
                colors[(y * panorama.Width) + x] = PixelColor(panorama, x, y);
            }
        });

        return new SKBitmap(panorama.Width, panorama.Height, SKColorType.Rgba8888, SKAlphaType.Opaque)
        {
            Pixels = colors,
        };
    }

    /// <summary>Paints a panorama and saves it as a PNG file.</summary>
    /// <param name="panorama">The panorama.</param>
    /// <param name="path">The path of the file to create.</param>
    public static void SavePng(Panorama panorama, string path)
    {
        using SKBitmap bitmap = Paint(panorama);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }

    private static SKColor PixelColor(Panorama panorama, int x, int y)
    {
        double distance = panorama.GetDistance(x, y);
        if (double.IsNaN(distance))
        {
            return SkyColor(panorama.ElevationAngleAt(y));
        }

        if (IsRidgeLine(panorama, x, y, distance))
        {
            double nearness = Math.Exp(-distance / HazeDistance);
            return Blend(s_haze, s_ridgeLine, nearness);
        }

        SKColor ground = HeightColor(panorama.GetTerrainHeight(x, y));
        double light = 0.35 + (0.65 * panorama.GetShading(x, y));
        SKColor lit = new(Scale(ground.Red, light), Scale(ground.Green, light), Scale(ground.Blue, light));

        return Blend(s_haze, lit, Math.Exp(-distance / HazeDistance));
    }

    /// <summary>
    /// Determines whether a pixel is on the outline of terrain standing in front of more distant terrain or the sky.
    /// </summary>
    private static bool IsRidgeLine(Panorama panorama, int x, int y, double distance)
    {
        if (y == 0)
        {
            return false;
        }

        double above = panorama.GetDistance(x, y - 1);

        return double.IsNaN(above) || above > distance * RidgeDistanceRatio;
    }

    private static SKColor SkyColor(double elevationAngle)
    {
        double height = Math.Clamp(elevationAngle / 10, 0, 1);

        return Blend(s_skyHorizon, s_skyTop, Math.Sqrt(height));
    }

    private static SKColor HeightColor(double height)
    {
        for (int i = 1; i < s_heightColors.Length; i++)
        {
            (double upperHeight, SKColor upperColor) = s_heightColors[i];
            if (height < upperHeight)
            {
                (double lowerHeight, SKColor lowerColor) = s_heightColors[i - 1];
                return Blend(lowerColor, upperColor, Math.Max(0, (height - lowerHeight) / (upperHeight - lowerHeight)));
            }
        }

        return s_heightColors[^1].Color;
    }

    /// <summary>Mixes two colors, from all of the first at zero to all of the second at one.</summary>
    private static SKColor Blend(SKColor first, SKColor second, double amount)
    {
        return new SKColor(
            Mix(first.Red, second.Red, amount),
            Mix(first.Green, second.Green, amount),
            Mix(first.Blue, second.Blue, amount));
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
