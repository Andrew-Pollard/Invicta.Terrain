// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Visibility;

using SkiaSharp;

namespace Invicta.Rendering;

/// <summary>
/// Paints a <see cref="Viewshed"/> as a map centered on the viewpoint, with north up and every pixel the same
/// distance on the ground: shaded terrain with the visible ground highlighted.
/// </summary>
/// <remarks>
/// The map uses the azimuthal equidistant projection, in which distances and bearings from the center are true, as
/// they are along each ray of the viewshed.
/// </remarks>
public static class ViewshedPainter
{
    private const int CreditBandHeight = 22;

    private static readonly SKColor s_sea = new(170, 196, 220);
    private static readonly SKColor s_lowland = new(206, 214, 190);
    private static readonly SKColor s_upland = new(222, 208, 180);
    private static readonly SKColor s_visible = new(235, 110, 30);
    private static readonly SKColor s_outside = new(245, 245, 242);
    private static readonly SKColor s_ink = new(30, 34, 40);

    /// <summary>Paints a viewshed and saves it as a PNG file.</summary>
    /// <param name="viewshed">The viewshed.</param>
    /// <param name="terrain">The terrain the viewshed was computed from.</param>
    /// <param name="path">The path of the file to create.</param>
    public static void SavePng(Viewshed viewshed, IElevationModel terrain, string path)
    {
        ArgumentNullException.ThrowIfNull(viewshed);

        ArgumentNullException.ThrowIfNull(terrain);

        ArgumentException.ThrowIfNullOrEmpty(path);

        int size = (int)Math.Ceiling(2 * viewshed.Radius / viewshed.Resolution);
        float[] heights = SampleHeights(viewshed, terrain, size);

        SKColor[] colors = new SKColor[size * size];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                colors[(y * size) + x] = PixelColor(viewshed, heights, size, x, y);
            }
        });

        using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size + CreditBandHeight));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(s_outside);
        using (SKBitmap bitmap = new(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque) { Pixels = colors })
        using (SKImage map = SKImage.FromBitmap(bitmap))
        {
            canvas.DrawImage(map, 0, 0, SKSamplingOptions.Default, null);
        }

        PaintViewpointAndScale(canvas, viewshed, size);
        PaintCredits(canvas, size);

        PngFile.Save(surface, path);
    }

    /// <summary>Samples the terrain height at the center of every pixel inside the viewshed's radius.</summary>
    private static float[] SampleHeights(Viewshed viewshed, IElevationModel terrain, int size)
    {
        float[] heights = new float[size * size];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                (double distance, double azimuth) = ToPolar(viewshed, size, x, y);
                if (distance > viewshed.Radius)
                {
                    heights[(y * size) + x] = float.NaN;
                    continue;
                }

                GeodesicLine line = new(viewshed.Viewpoint.Location, azimuth);
                heights[(y * size) + x] = (float)terrain.GetElevation(line.GetPosition(distance).Coordinate);
            }
        });

        return heights;
    }

    /// <summary>Converts a pixel's center to its distance and azimuth from the map's center.</summary>
    private static (double Distance, double Azimuth) ToPolar(Viewshed viewshed, int size, int x, int y)
    {
        double east = (x + 0.5 - (size / 2.0)) * viewshed.Resolution;
        double north = ((size / 2.0) - y - 0.5) * viewshed.Resolution;

        return (Math.Sqrt((east * east) + (north * north)), Math.Atan2(east, north) * 180 / Math.PI);
    }

    private static SKColor PixelColor(Viewshed viewshed, float[] heights, int size, int x, int y)
    {
        float height = heights[(y * size) + x];
        if (float.IsNaN(height))
        {
            return s_outside;
        }

        SKColor ground = height <= 0 ? s_sea : ColorMath.Blend(s_lowland, s_upland, Math.Clamp(height / 1000, 0, 1));
        double light = Hillshade(heights, size, x, y, viewshed.Resolution);
        SKColor shaded = ColorMath.Shade(ground, light);

        (double distance, double azimuth) = ToPolar(viewshed, size, x, y);

        return viewshed.IsVisible(distance, azimuth) ? ColorMath.Blend(shaded, s_visible, 0.6) : shaded;
    }

    /// <summary>Lights the terrain from the north-west, from the heights of the neighboring pixels.</summary>
    private static double Hillshade(float[] heights, int size, int x, int y, double spacing)
    {
        double west = HeightAt(heights, size, x - 1, y, x, y);
        double east = HeightAt(heights, size, x + 1, y, x, y);
        double north = HeightAt(heights, size, x, y - 1, x, y);
        double south = HeightAt(heights, size, x, y + 1, x, y);

        double slopeEast = (east - west) / (2 * spacing);
        double slopeNorth = (north - south) / (2 * spacing);

        // Flat ground, lit at 45°, comes out at about 0.98, so sunlit slopes can brighten a little beyond it.
        return 0.45 + (0.75 * Elevation.Hillshade.Brightness(slopeEast, slopeNorth));
    }

    /// <summary>Gets a neighboring pixel's height, falling back to the center pixel's at the edges.</summary>
    private static double HeightAt(float[] heights, int size, int x, int y, int centerX, int centerY)
    {
        bool inside = x >= 0 && y >= 0 && x < size && y < size && !float.IsNaN(heights[(y * size) + x]);

        return inside ? heights[(y * size) + x] : heights[(centerY * size) + centerX];
    }

    private static void PaintViewpointAndScale(SKCanvas canvas, Viewshed viewshed, int size)
    {
        using SKPaint ink = new() { Color = s_ink, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using SKPaint text = new() { Color = s_ink, IsAntialias = true };
        using SKFont font = new(SKTypeface.Default, Math.Max(14, size / 80f));

        float center = size / 2f;
        canvas.DrawCircle(center, center, Math.Max(6, size / 200f), ink);

        double barMeters = NiceKilometers(viewshed.Radius / 4) * 1000;
        float barPixels = (float)(barMeters / viewshed.Resolution);
        float margin = size / 40f;
        float barY = size - margin;
        canvas.DrawLine(margin, barY, margin + barPixels, barY, ink);
        canvas.DrawLine(margin, barY - 6, margin, barY + 6, ink);
        canvas.DrawLine(margin + barPixels, barY - 6, margin + barPixels, barY + 6, ink);
        canvas.DrawText(
            string.Create(CultureInfo.InvariantCulture, $"{barMeters / 1000:0} km"),
            margin + (barPixels / 2),
            barY - 12,
            SKTextAlign.Center,
            font,
            text);
        canvas.DrawText("N", size - margin, margin + font.Size, SKTextAlign.Center, font, text);
    }

    private static double NiceKilometers(double meters)
    {
        double kilometers = meters / 1000;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(kilometers)));
        double normalized = kilometers / magnitude;

        return magnitude * normalized switch
        {
            < 2 => 1,
            < 5 => 2,
            < 10 => 5,
            _ => 10,
        };
    }

    private static void PaintCredits(SKCanvas canvas, int size)
    {
        using SKPaint ink = new() { Color = s_ink.WithAlpha(170), IsAntialias = true };
        using SKFont font = new(SKTypeface.Default, 11);

        canvas.DrawText(DataCredits.Copernicus, size - 8, size + CreditBandHeight - 7, SKTextAlign.Right, font, ink);
    }
}
