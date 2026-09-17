// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

using Invicta.Visibility;

using SkiaSharp;

namespace Invicta.Rendering;

/// <summary>
/// Paints a <see cref="Panorama"/> as a picture: shaded terrain fading into haze under a sky, with visible summits
/// labeled above it, a compass scale below it, and the credits the data's licences require.
/// </summary>
public static class PanoramaPainter
{
    private const int LabelBandHeight = 300;
    private const int CompassBandHeight = 34;
    private const int CreditBandHeight = 22;

    // Aerial perspective: terrain fades halfway to the haze color about every 40 km.
    private const double HazeDistance = 60_000;

    // A pixel in front of terrain at least this much further away is drawn as a ridge line.
    private const double RidgeDistanceRatio = 1.25;

    private const float LabelFontSize = 15;
    private const float LabelSpacing = 22;

    // The sky kept above the highest terrain, in pixels.
    private const int SkyMargin = 40;

    private static readonly SKColor s_skyTop = new(96, 150, 210);
    private static readonly SKColor s_skyHorizon = new(214, 228, 240);
    private static readonly SKColor s_haze = new(190, 205, 222);
    private static readonly SKColor s_ridgeLine = new(35, 40, 45);
    private static readonly SKColor s_ink = new(30, 34, 40);
    private static readonly SKColor s_paper = new(245, 245, 242);

    // Terrain colors by height, from lowland greens to bare summit rock.
    private static readonly (double Height, SKColor Color)[] s_heightColors =
    [
        (0, new SKColor(62, 92, 56)),
        (300, new SKColor(96, 112, 64)),
        (700, new SKColor(128, 116, 86)),
        (1100, new SKColor(150, 144, 136)),
        (1400, new SKColor(190, 188, 186)),
    ];

    /// <summary>Paints a panorama with its summit labels, and saves it as a PNG file.</summary>
    /// <param name="panorama">The panorama.</param>
    /// <param name="summits">The visible summits to label, which may be empty.</param>
    /// <param name="path">The path of the file to create.</param>
    public static void SavePng(Panorama panorama, IEnumerable<VisibleSummit> summits, string path)
    {
        ArgumentNullException.ThrowIfNull(panorama);

        ArgumentNullException.ThrowIfNull(summits);

        ArgumentException.ThrowIfNullOrEmpty(path);

        // Leave out the empty sky above the highest terrain, keeping a margin, so the labels sit close to the summits.
        int firstRow = Math.Max(0, HighestTerrainRow(panorama) - SkyMargin);
        int shownRows = panorama.Height - firstRow;

        int height = LabelBandHeight + shownRows + CompassBandHeight + CreditBandHeight;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(panorama.Width, height));
        SKCanvas canvas = surface.Canvas;

        PaintSkyBand(canvas, panorama.Width);
        using (SKBitmap terrainBitmap = PaintTerrain(panorama))
        using (SKImage terrain = SKImage.FromBitmap(terrainBitmap))
        {
            SKRect source = new(0, firstRow, panorama.Width, panorama.Height);
            SKRect destination = new(0, LabelBandHeight, panorama.Width, LabelBandHeight + shownRows);
            canvas.DrawImage(terrain, source, destination, SKSamplingOptions.Default, null);
        }

        using SKFont font = new(SKTypeface.Default, LabelFontSize);
        PaintLabels(canvas, font, summits, LabelBandHeight - firstRow);
        PaintCompass(canvas, font, panorama, LabelBandHeight + shownRows);
        PaintCredits(canvas, panorama.Width, height);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }

    /// <summary>Paints the terrain and sky of a panorama into a new bitmap the same size.</summary>
    /// <param name="panorama">The panorama.</param>
    /// <returns>The bitmap, which the caller must dispose.</returns>
    public static SKBitmap PaintTerrain(Panorama panorama)
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

    private static int HighestTerrainRow(Panorama panorama)
    {
        for (int y = 0; y < panorama.Height; y++)
        {
            for (int x = 0; x < panorama.Width; x++)
            {
                if (!double.IsNaN(panorama.GetDistance(x, y)))
                {
                    return y;
                }
            }
        }

        return panorama.Height;
    }

    private static void PaintSkyBand(SKCanvas canvas, int width)
    {
        using SKPaint paint = new() { Color = s_skyTop };
        canvas.DrawRect(0, 0, width, LabelBandHeight, paint);
    }

    private static SKColor PixelColor(Panorama panorama, int x, int y)
    {
        double distance = panorama.GetDistance(x, y);
        if (double.IsNaN(distance))
        {
            return SkyColor(panorama.ElevationAngleAt(y), panorama.TopAngle);
        }

        if (IsRidgeLine(panorama, x, y, distance))
        {
            double nearness = Math.Exp(-distance / HazeDistance);
            return ColorMath.Blend(s_haze, s_ridgeLine, nearness);
        }

        SKColor ground = HeightColor(panorama.GetTerrainHeight(x, y));
        double light = 0.35 + (0.65 * panorama.GetShading(x, y));
        SKColor lit = ColorMath.Shade(ground, light);

        return ColorMath.Blend(s_haze, lit, Math.Exp(-distance / HazeDistance));
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

    /// <summary>Gets the sky's color, fading from the horizon up to the top of the panorama.</summary>
    private static SKColor SkyColor(double elevationAngle, double topAngle)
    {
        double height = Math.Clamp(elevationAngle / Math.Max(1, topAngle), 0, 1);

        return ColorMath.Blend(s_skyHorizon, s_skyTop, Math.Sqrt(height));
    }

    private static SKColor HeightColor(double height)
    {
        for (int i = 1; i < s_heightColors.Length; i++)
        {
            (double upperHeight, SKColor upperColor) = s_heightColors[i];
            if (height < upperHeight)
            {
                (double lowerHeight, SKColor lowerColor) = s_heightColors[i - 1];
                double amount = Math.Max(0, (height - lowerHeight) / (upperHeight - lowerHeight));

                return ColorMath.Blend(lowerColor, upperColor, amount);
            }
        }

        return s_heightColors[^1].Color;
    }

    /// <summary>
    /// Labels summits with vertical text above the panorama, joined to each summit by a leader line. The most
    /// prominent summits are placed first, and a label that would overlap one already placed is left out.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="font">The font for the labels.</param>
    /// <param name="summits">The summits.</param>
    /// <param name="panoramaTop">Where the panorama's first row would be on the canvas, which may be above it.</param>
    private static void PaintLabels(SKCanvas canvas, SKFont font, IEnumerable<VisibleSummit> summits, float panoramaTop)
    {
        using SKPaint line = new() { Color = s_paper.WithAlpha(200), StrokeWidth = 1, IsAntialias = true };
        using SKPaint text = new() { Color = s_paper, IsAntialias = true };
        using SKPaint halo = new()
        {
            Color = s_ink.WithAlpha(150),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true,
        };

        List<float> placed = [];
        foreach (VisibleSummit summit in summits.OrderByDescending(Importance))
        {
            float x = (float)summit.X + 0.5f;
            if (placed.Exists(other => Math.Abs(other - x) < LabelSpacing))
            {
                continue;
            }

            placed.Add(x);

            float summitY = panoramaTop + (float)summit.Y;
            canvas.DrawLine(x, summitY - 3, x, LabelBandHeight - 8, line);

            string label = Label(summit);
            canvas.Save();
            canvas.Translate(x + (LabelFontSize / 3), LabelBandHeight - 12);
            canvas.RotateDegrees(-90);
            canvas.DrawText(label, 0, 0, SKTextAlign.Left, font, halo);
            canvas.DrawText(label, 0, 0, SKTextAlign.Left, font, text);
            canvas.Restore();
        }
    }

    /// <summary>
    /// Ranks a summit for labeling by its prominence where known, otherwise by a quarter of its height, which is
    /// typical of the prominence of a summit that has been surveyed but not ranked.
    /// </summary>
    private static double Importance(VisibleSummit summit)
    {
        return summit.Summit.Prominence ?? (0.25 * (summit.Summit.Elevation ?? 0));
    }

    private static string Label(VisibleSummit summit)
    {
        string elevation = summit.Summit.Elevation is double meters
            ? string.Create(CultureInfo.InvariantCulture, $"  {meters:0} m")
            : string.Empty;
        double kilometers = summit.Distance / 1000;
        string distance = kilometers < 10
            ? string.Create(CultureInfo.InvariantCulture, $"{kilometers:0.0} km")
            : string.Create(CultureInfo.InvariantCulture, $"{kilometers:0} km");

        return $"{summit.Summit.Name}{elevation}  {distance}";
    }

    /// <summary>Paints a scale of azimuths below the panorama, with a tick every 5° and the compass points.</summary>
    private static void PaintCompass(SKCanvas canvas, SKFont font, Panorama panorama, float top)
    {
        using SKPaint background = new() { Color = s_paper };
        using SKPaint ink = new() { Color = s_ink, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRect(0, top, panorama.Width, CompassBandHeight, background);

        string[] points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        for (int azimuth = 0; azimuth < 360; azimuth += 5)
        {
            float x = (float)(azimuth / panorama.PixelAngle);
            bool major = azimuth % 45 == 0;
            canvas.DrawLine(x, top, x, top + (major ? 12 : azimuth % 15 == 0 ? 8 : 4), ink);

            if (azimuth % 15 == 0)
            {
                string label = major ? points[azimuth / 45] : azimuth.ToString(CultureInfo.InvariantCulture) + "°";
                canvas.DrawText(label, x, top + 28, SKTextAlign.Center, font, ink);
            }
        }
    }

    private static void PaintCredits(SKCanvas canvas, int width, int height)
    {
        using SKPaint background = new() { Color = s_paper };
        using SKPaint ink = new() { Color = s_ink.WithAlpha(170), IsAntialias = true };
        using SKFont font = new(SKTypeface.Default, 12);

        canvas.DrawRect(0, height - CreditBandHeight, width, CreditBandHeight, background);
        string credits = $"{DataCredits.Copernicus} {DataCredits.OpenStreetMap}";
        canvas.DrawText(credits, width - 8, height - 7, SKTextAlign.Right, font, ink);
    }
}
