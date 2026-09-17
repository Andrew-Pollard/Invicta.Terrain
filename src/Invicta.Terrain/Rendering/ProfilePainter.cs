// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

using Invicta.Visibility;

using SkiaSharp;

namespace Invicta.Rendering;

/// <summary>
/// Paints a <see cref="SightLineProfile"/> as a cross-section: the terrain and sea level falling away with the
/// Earth's curvature, the straight line of sight above them, and what blocks it.
/// </summary>
public static class ProfilePainter
{
    private const int Width = 1600;
    private const int Height = 640;
    private const float LeftMargin = 80;
    private const float RightMargin = 30;
    private const float TopMargin = 70;
    private const float BottomMargin = 70;

    private static readonly SKColor s_background = new(245, 245, 242);
    private static readonly SKColor s_sky = new(222, 234, 245);
    private static readonly SKColor s_sea = new(120, 160, 200);
    private static readonly SKColor s_terrain = new(120, 128, 92);
    private static readonly SKColor s_ink = new(30, 34, 40);
    private static readonly SKColor s_visible = new(40, 140, 60);
    private static readonly SKColor s_hidden = new(200, 40, 40);

    /// <summary>Paints a profile and saves it as a PNG file.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="title">The title, such as the names of the two ends.</param>
    /// <param name="path">The path of the file to create.</param>
    public static void SavePng(SightLineProfile profile, string title, string path)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ArgumentNullException.ThrowIfNull(title);

        ArgumentException.ThrowIfNullOrEmpty(path);

        using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(s_background);

        Scale scale = Scale.Fit(profile);
        using SKFont font = new(SKTypeface.Default, 15);
        using SKFont titleFont = new(SKTypeface.Default, 20);

        PaintPlot(canvas, profile, scale);
        PaintAxes(canvas, font, profile, scale);
        PaintTitle(canvas, titleFont, font, profile, title);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }

    private static void PaintPlot(SKCanvas canvas, SightLineProfile profile, Scale scale)
    {
        using SKPaint sky = new() { Color = s_sky };
        SKRect plot = new(LeftMargin, TopMargin, Width - RightMargin, Height - BottomMargin);
        canvas.DrawRect(plot, sky);

        canvas.Save();
        canvas.ClipRect(plot);

        using SKPaint terrain = new() { Color = s_terrain, IsAntialias = true };
        IEnumerable<(double, double)> surface = profile.Points
            .Select(point => (point.Distance, Math.Max(point.ApparentHeight, point.SeaLevelApparentHeight)));
        using SKPath terrainPath = FilledBelow(surface, scale);
        canvas.DrawPath(terrainPath, terrain);

        PaintSea(canvas, profile, scale);

        LineOfSightResult result = profile.Result;
        using SKPaint line = new()
        {
            Color = result.IsVisible ? s_visible : s_hidden,
            StrokeWidth = 2,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        SKPoint eye = new(scale.X(0), scale.Y(profile.EyeHeight));
        SKPoint target = new(scale.X(result.Distance), scale.Y(profile.TargetApparentHeight));
        canvas.DrawLine(eye, target, line);

        if (!result.IsVisible && result.Obstruction is { } obstruction)
        {
            double apparentHeight = profile.EyeHeight + (obstruction.Distance * Math.Tan(obstruction.ElevationAngle));
            canvas.DrawCircle(scale.X(obstruction.Distance), scale.Y(apparentHeight), 6, line);
        }

        canvas.Restore();
    }

    /// <summary>Paints the sea as columns wherever the terrain is at sea level, which marks sea in the data.</summary>
    private static void PaintSea(SKCanvas canvas, SightLineProfile profile, Scale scale)
    {
        const double SeaLevelTolerance = 0.5;

        IReadOnlyList<ProfilePoint> points = profile.Points;
        float columnWidth = scale.X(points[^1].Distance) - scale.X(points[^2].Distance) + 1;
        using SKPaint sea = new() { Color = s_sea, StrokeWidth = columnWidth };
        foreach (ProfilePoint point in points)
        {
            if (Math.Abs(point.TerrainHeight) <= SeaLevelTolerance)
            {
                float x = scale.X(point.Distance);
                canvas.DrawLine(x, scale.Y(point.SeaLevelApparentHeight), x, Height - BottomMargin, sea);
            }
        }
    }

    /// <summary>Makes a closed path from a curve down to the bottom of the plot.</summary>
    private static SKPath FilledBelow(IEnumerable<(double Distance, double Height)> curve, Scale scale)
    {
        using SKPathBuilder path = new();
        path.MoveTo(LeftMargin, Height - BottomMargin);
        foreach ((double distance, double height) in curve)
        {
            path.LineTo(scale.X(distance), scale.Y(height));
        }

        path.LineTo(Width - RightMargin, Height - BottomMargin);
        path.Close();

        return path.Detach();
    }

    private static void PaintAxes(SKCanvas canvas, SKFont font, SightLineProfile profile, Scale scale)
    {
        using SKPaint ink = new() { Color = s_ink, StrokeWidth = 1, IsAntialias = true };
        float bottom = Height - BottomMargin;

        double distanceStep = NiceStep(profile.Result.Distance / 1000 / 8) * 1000;
        for (double distance = 0; distance <= profile.Result.Distance; distance += distanceStep)
        {
            float x = scale.X(distance);
            canvas.DrawLine(x, bottom, x, bottom + 6, ink);
            canvas.DrawText(Format($"{distance / 1000:0} km"), x, bottom + 24, SKTextAlign.Center, font, ink);
        }

        double heightStep = NiceStep((scale.Top - scale.Bottom) / 6);
        double firstHeight = Math.Ceiling(scale.Bottom / heightStep) * heightStep;
        for (double height = firstHeight; height <= scale.Top; height += heightStep)
        {
            float y = scale.Y(height);
            canvas.DrawLine(LeftMargin - 6, y, LeftMargin, y, ink);
            canvas.DrawText(Format($"{height:0} m"), LeftMargin - 10, y + 5, SKTextAlign.Right, font, ink);
        }

        canvas.DrawText(
            "Apparent height: terrain lowered by the Earth's curvature, less refraction",
            LeftMargin,
            bottom + 50,
            SKTextAlign.Left,
            font,
            ink);
    }

    /// <summary>Rounds a rough step up to 1, 2 or 5 times a power of ten.</summary>
    private static double NiceStep(double rough)
    {
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double normalized = rough / magnitude;

        return magnitude * normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10,
        };
    }

    private static void PaintTitle(
        SKCanvas canvas, SKFont titleFont, SKFont font, SightLineProfile profile, string title)
    {
        using SKPaint ink = new() { Color = s_ink, IsAntialias = true };
        using SKPaint faint = new() { Color = s_ink.WithAlpha(150), IsAntialias = true };
        LineOfSightResult result = profile.Result;

        // A visible target on a summit only just clears its own near slope, so how far it clears says little.
        double shortfallSeconds = -result.Clearance * 180 / Math.PI * 3600;
        string verdict = result.IsVisible
            ? "visible"
            : Format($"hidden {result.Obstruction?.Distance / 1000:0.0} km out, by {shortfallSeconds:0.0}″");
        string subtitle = Format($"{result.Distance / 1000:0.0} km, {verdict}");
        canvas.DrawText(title, LeftMargin, 32, SKTextAlign.Left, titleFont, ink);
        canvas.DrawText(subtitle, LeftMargin, 56, SKTextAlign.Left, font, ink);

        using SKFont creditsFont = new(SKTypeface.Default, 11);
        canvas.DrawText(DataCredits.Copernicus, Width - RightMargin, Height - 6, SKTextAlign.Right, creditsFont, faint);
    }

    private static string Format(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Maps distances and apparent heights onto the plot.</summary>
    private readonly record struct Scale(double Distance, double Bottom, double Top)
    {
        public static Scale Fit(SightLineProfile profile)
        {
            double top = Math.Max(profile.EyeHeight, profile.TargetApparentHeight);
            double bottom = Math.Min(profile.EyeHeight, profile.TargetApparentHeight);
            foreach (ProfilePoint point in profile.Points)
            {
                top = Math.Max(top, point.ApparentHeight);
                bottom = Math.Min(bottom, point.SeaLevelApparentHeight);
            }

            double margin = (top - bottom) * 0.08;

            return new Scale(profile.Result.Distance, bottom - margin, top + margin);
        }

        public float X(double distance)
        {
            return LeftMargin + (float)(distance / Distance * (Width - LeftMargin - RightMargin));
        }

        public float Y(double height)
        {
            double fraction = (height - Bottom) / (Top - Bottom);

            return Height - BottomMargin - (float)(fraction * (Height - TopMargin - BottomMargin));
        }
    }
}
