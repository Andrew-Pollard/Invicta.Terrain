// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Places;
using Invicta.Rendering;
using Invicta.Visibility;

using SkiaSharp;

namespace Invicta;

/// <summary>
/// Renders the frames of a flight from Chamonix to the Matterhorn, each the view ahead from a camera that follows the
/// terrain, with the summits in view named. The frames are JPEG files for a video tool to assemble.
/// </summary>
internal static class Program
{
    private static readonly GeoCoordinate s_start = new(45.9237, 6.8694);
    private static readonly GeoCoordinate s_end = new(45.9764, 7.6586);

    private const string StartName = "Chamonix";
    private const string EndName = "the Matterhorn";

    private const int FrameCount = 1200;
    private const int FrameWidth = 1280;
    private const int FrameHeight = 720;

    private const double FieldOfView = 60;
    private const double PixelAngle = FieldOfView / FrameWidth;

    // The camera looks down, so that the ground ahead fills most of the frame.
    private const double TopAngle = 7;
    private const double BottomAngle = TopAngle - (FrameHeight * PixelAngle);

    private const double ViewDistance = 60_000;

    // The flight stops short of the Matterhorn, which it would otherwise pass over and above without ever showing.
    private const double StopShortOf = 6000;

    // The camera climbs to pass this far above the highest ground within this distance ahead of it.
    private const double Clearance = 900;
    private const double LookAhead = 7000;
    private const double LookAheadSpacing = 250;

    // Half the width of the moving average that turns the clearance height into a smooth flight path.
    private const int SmoothingFrames = 45;

    // Summits are labeled by prominence where it is known, so only the highest few crowd the frame.
    private const int MaximumLabels = 8;
    private const float LabelFontSize = 17;
    private const float LabelGap = 10;
    private const float LabelMargin = 12;

    // The band at the foot of the frame that the caption and credits occupy.
    private const float CaptionHeight = 60;

    private static readonly SKColor s_paper = new(245, 245, 242);
    private static readonly SKColor s_ink = new(20, 24, 30);

    private static async Task Main(string[] args)
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDirectory = args.Length > 0 ? args[0] : Path.Combine(localData, "Invicta.Terrain");
        string frameDirectory = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "frames");
        Directory.CreateDirectory(frameDirectory);

        using HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
        Stopwatch stopwatch = Stopwatch.StartNew();

        GeodesicSolution route = Geodesic.Inverse(s_start, s_end);
        GeodesicLine line = new(s_start, route.InitialAzimuth);
        GeoCoordinate middle = line.GetPosition(route.Distance / 2);
        double radius = (route.Distance / 2) + ViewDistance;

        CopernicusTileStore tileStore = new(Path.Combine(cacheDirectory, "copernicus"), httpClient);
        LayeredTerrain terrain = await CopernicusElevationModel.LoadLayeredAsync(
            tileStore, middle, radius, PixelAngle * Math.PI / 180, CancellationToken.None);
        Report(stopwatch, $"Loaded the terrain along {route.Distance / 1000:F1} km of route.");

        OpenStreetMapSummitStore summitStore = new(Path.Combine(cacheDirectory, "openstreetmap"), httpClient);
        IReadOnlyList<Summit> summits =
            await summitStore.GetSummitsAsync(GeoBoundingBox.Around(middle, radius), CancellationToken.None);
        Report(stopwatch, $"Found {summits.Count} named summits.");

        Camera[] flight = PlanFlight(terrain, line, route.Distance);
        Report(stopwatch, $"Planned {FrameCount} frames, climbing to {flight.Max(camera => camera.Height):F0} m.");

        for (int frame = 0; frame < flight.Length; frame++)
        {
            string path = Path.Combine(
                frameDirectory, string.Create(CultureInfo.InvariantCulture, $"frame{frame:D5}.jpg"));
            RenderFrame(terrain, summits, flight[frame], path);

            if ((frame + 1) % 100 == 0)
            {
                Report(stopwatch, $"Rendered {frame + 1} of {flight.Length} frames.");
            }
        }

        Report(stopwatch, $"Wrote the frames to {frameDirectory}.");
    }

    /// <summary>
    /// Plans where the camera is, how high and which way it faces for each frame: evenly spaced along the route,
    /// facing the way ahead, and high enough to clear the ground in front of it.
    /// </summary>
    private static Camera[] PlanFlight(LayeredTerrain terrain, GeodesicLine line, double routeDistance)
    {
        IElevationModel model = terrain.GetModel(0);
        GeoCoordinate[] positions = new GeoCoordinate[FrameCount];
        double[] headings = new double[FrameCount];
        double[] remaining = new double[FrameCount];
        double[] clearanceHeights = new double[FrameCount];

        double flightDistance = routeDistance - StopShortOf;
        for (int frame = 0; frame < FrameCount; frame++)
        {
            double along = flightDistance * frame / (FrameCount - 1.0);
            positions[frame] = line.GetPosition(along);
            headings[frame] = Geodesic.Inverse(positions[frame], line.GetPosition(along + LookAhead)).InitialAzimuth;
            remaining[frame] = routeDistance - along;
            clearanceHeights[frame] = HighestGroundAhead(model, line, along) + Clearance;
        }

        double[] heights = Smooth(clearanceHeights);

        return [.. Enumerable.Range(0, FrameCount)
            .Select(frame => new Camera(positions[frame], heights[frame], headings[frame], remaining[frame]))];
    }

    private static double HighestGroundAhead(IElevationModel model, GeodesicLine line, double along)
    {
        double highest = double.NegativeInfinity;
        for (double ahead = 0; ahead <= LookAhead; ahead += LookAheadSpacing)
        {
            highest = Math.Max(highest, model.GetElevation(line.GetPosition(along + ahead)));
        }

        return highest;
    }

    /// <summary>Averages each value with its neighbors, so that the camera climbs and descends gradually.</summary>
    private static double[] Smooth(double[] values)
    {
        double[] smoothed = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            int first = Math.Max(0, i - SmoothingFrames);
            int last = Math.Min(values.Length - 1, i + SmoothingFrames);

            double total = 0;
            for (int j = first; j <= last; j++)
            {
                total += values[j];
            }

            smoothed[i] = total / (last - first + 1);
        }

        return smoothed;
    }

    private static void RenderFrame(
        LayeredTerrain terrain, IEnumerable<Summit> summits, Camera camera, string path)
    {
        Viewpoint viewpoint = new(camera.Location, camera.Height);
        PanoramaOptions options = new()
        {
            Width = FrameWidth,
            HorizontalFieldOfView = FieldOfView,
            LeftEdgeAzimuth = camera.Heading - (FieldOfView / 2),
            TopAngle = TopAngle,
            BottomAngle = BottomAngle,
            MaximumDistance = ViewDistance,
        };

        Panorama panorama = Panorama.Render(terrain, viewpoint, options, CancellationToken.None);
        IReadOnlyList<VisibleSummit> visible = SummitVisibility.FindVisible(panorama, terrain, summits);

        SaveFrame(panorama, visible, camera, path);
    }

    private static void SaveFrame(
        Panorama panorama, IEnumerable<VisibleSummit> summits, Camera camera, string path)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(FrameWidth, FrameHeight));
        SKCanvas canvas = surface.Canvas;

        using (SKBitmap terrainBitmap = PanoramaPainter.PaintTerrain(panorama))
        using (SKImage terrain = SKImage.FromBitmap(terrainBitmap))
        {
            canvas.DrawImage(terrain, 0, 0, SKSamplingOptions.Default, null);
        }

        using SKFont font = new(SKTypeface.Default, LabelFontSize);
        PaintLabels(canvas, font, summits);
        PaintCaption(canvas, camera);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }

    /// <summary>
    /// Names the most prominent summits in view, above the point where each appears, leaving out any that would
    /// overlap a name already placed or run off the edge of the frame.
    /// </summary>
    private static void PaintLabels(SKCanvas canvas, SKFont font, IEnumerable<VisibleSummit> summits)
    {
        using SKPaint text = new() { Color = s_paper, IsAntialias = true };
        using SKPaint halo = new()
        {
            Color = s_ink.WithAlpha(170),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true,
        };

        List<SKRect> placed = [];
        foreach (VisibleSummit summit in summits.OrderByDescending(Importance))
        {
            string label = Label(summit);
            float x = (float)summit.X + 0.5f;
            float y = (float)summit.Y;
            float halfWidth = font.MeasureText(label) / 2;
            SKRect box = new(x - halfWidth - LabelGap, y - 18 - LabelFontSize, x + halfWidth + LabelGap, y);

            bool room = box.Left >= LabelMargin
                && box.Right <= FrameWidth - LabelMargin
                && box.Top >= LabelMargin
                && box.Bottom <= FrameHeight - CaptionHeight
                && !placed.Exists(other => other.IntersectsWith(box));
            if (!room)
            {
                continue;
            }

            placed.Add(box);

            canvas.DrawLine(x, y - 4, x, y - 12, halo);
            canvas.DrawText(label, x, y - 18, SKTextAlign.Center, font, halo);
            canvas.DrawText(label, x, y - 18, SKTextAlign.Center, font, text);

            if (placed.Count == MaximumLabels)
            {
                return;
            }
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
        return summit.Summit.Elevation is double meters
            ? string.Create(CultureInfo.InvariantCulture, $"{summit.Summit.Name}  {meters:N0} m")
            : summit.Summit.Name;
    }

    /// <summary>Writes the camera's height and the distance still to fly, with the credits the data's licences
    /// require.</summary>
    private static void PaintCaption(SKCanvas canvas, Camera camera)
    {
        using SKFont font = new(SKTypeface.Default, 15);
        using SKFont creditFont = new(SKTypeface.Default, 10);
        using SKPaint text = new() { Color = s_paper, IsAntialias = true };
        using SKPaint halo = new()
        {
            Color = s_ink.WithAlpha(170),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true,
        };

        string caption = string.Create(
            CultureInfo.InvariantCulture,
            $"{StartName} to {EndName}    {camera.Height:N0} m    {camera.DistanceRemaining / 1000:F1} km to go");
        canvas.DrawText(caption, 20, FrameHeight - 42, SKTextAlign.Left, font, halo);
        canvas.DrawText(caption, 20, FrameHeight - 42, SKTextAlign.Left, font, text);

        canvas.DrawText(DataCredits.Copernicus, 20, FrameHeight - 22, SKTextAlign.Left, creditFont, halo);
        canvas.DrawText(DataCredits.Copernicus, 20, FrameHeight - 22, SKTextAlign.Left, creditFont, text);
        canvas.DrawText(DataCredits.OpenStreetMap, 20, FrameHeight - 10, SKTextAlign.Left, creditFont, halo);
        canvas.DrawText(DataCredits.OpenStreetMap, 20, FrameHeight - 10, SKTextAlign.Left, creditFont, text);
    }

    private static void Report(Stopwatch stopwatch, string message)
    {
        Console.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"[{stopwatch.Elapsed:mm\\:ss}] {message}"));
    }

    /// <summary>Describes where the camera is for one frame, and which way it faces.</summary>
    /// <param name="Location">The point below the camera.</param>
    /// <param name="Height">The camera's height in meters above sea level.</param>
    /// <param name="Heading">The azimuth in degrees the camera faces.</param>
    /// <param name="DistanceRemaining">The distance in meters still to fly.</param>
    private readonly record struct Camera(
        GeoCoordinate Location, double Height, double Heading, double DistanceRemaining);
}
