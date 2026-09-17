// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Places;
using Invicta.Rendering;
using Invicta.Visibility;

using SkiaSharp;

namespace Invicta;

/// <summary>
/// Renders the frames of a flight along a route, each the view ahead from a camera that follows the terrain, with the
/// summits in view named. The frames are JPEG files for a video tool to assemble.
/// </summary>
internal static class Program
{
    private const int FrameRate = 60;
    private const int FrameWidth = 1280;
    private const int FrameHeight = 720;

    private const double FieldOfView = 60;
    private const double PixelAngle = FieldOfView / FrameWidth;

    // The camera faces the point on the route this far ahead of it, which rounds off the turns at the waypoints.
    private const double HeadingLookAhead = 1500;

    private const double LookAheadSpacing = 200;

    // Half the width of the moving average that turns the heights and headings into a smooth flight path.
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

    private static async Task<int> Main(string[] args)
    {
        string routeDirectory = Path.Combine(AppContext.BaseDirectory, "Routes");
        string routeFile = args.Length > 0 ? args[0] : Path.Combine(routeDirectory, "alps.json");
        if (!File.Exists(routeFile))
        {
            string[] shipped = Directory.GetFiles(routeDirectory, "*.json").Select(Path.GetFileName).ToArray()!;
            await Console.Error.WriteLineAsync(
                $"No route file at {routeFile}. The routes in {routeDirectory} are {string.Join(", ", shipped)}.");

            return 1;
        }

        Route route;
        try
        {
            route = await Route.LoadAsync(routeFile, CancellationToken.None);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            await Console.Error.WriteLineAsync(error.Message);

            return 1;
        }

        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDirectory = args.Length > 1 ? args[1] : Path.Combine(localData, "Invicta.Terrain");
        string frameDirectory = args.Length > 2 ? args[2] : Path.Combine(Environment.CurrentDirectory, "frames");
        Directory.CreateDirectory(frameDirectory);

        using HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
        Stopwatch stopwatch = Stopwatch.StartNew();

        RoutePath path = new(route.Waypoints);
        if (path.Distance <= route.StopShortOf)
        {
            await Console.Error.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"The route in {routeFile} is {path.Distance / 1000:F1} km long, too short to stop "
                + $"{route.StopShortOf / 1000:F1} km before its end."));

            return 1;
        }

        int removed = RemoveFramesOfEarlierFlight(frameDirectory);
        if (removed > 0)
        {
            Report(stopwatch, $"Removed {removed} frames of an earlier flight.");
        }

        GeoCoordinate middle = path.GetPosition(path.Distance / 2);
        double radius = route.Waypoints.Max(waypoint => Geodesic.Inverse(middle, waypoint).Distance)
            + route.ViewDistance;

        CopernicusTileStore tileStore = new(Path.Combine(cacheDirectory, "copernicus"), httpClient);
        LayeredTerrain terrain = await CopernicusElevationModel.LoadLayeredAsync(
            tileStore, middle, radius, PixelAngle * Math.PI / 180, CancellationToken.None);
        Report(stopwatch, $"Loaded the terrain along {path.Distance / 1000:F1} km of route.");

        OpenStreetMapSummitStore summitStore = new(Path.Combine(cacheDirectory, "openstreetmap"), httpClient);
        IReadOnlyList<Summit> summits =
            await summitStore.GetSummitsAsync(GeoBoundingBox.Around(middle, radius), CancellationToken.None);
        Report(stopwatch, $"Found {summits.Count} named summits.");

        Camera[] flight = PlanFlight(terrain, route, path);
        Report(stopwatch, $"Planned {flight.Length} frames, climbing to {flight.Max(camera => camera.Height):F0} m.");

        for (int frame = 0; frame < flight.Length; frame++)
        {
            string file = Path.Combine(
                frameDirectory, string.Create(CultureInfo.InvariantCulture, $"frame{frame:D5}.jpg"));
            RenderFrame(terrain, route, summits, flight[frame], file);

            if ((frame + 1) % 100 == 0)
            {
                Report(stopwatch, $"Rendered {frame + 1} of {flight.Length} frames.");
            }
        }

        Report(stopwatch, $"Wrote the frames to {frameDirectory}.");

        return 0;
    }

    /// <summary>
    /// Deletes the frames this sample wrote before, so that a shorter flight does not leave the tail of a longer one
    /// for a video tool to read as its own.
    /// </summary>
    /// <param name="frameDirectory">The directory the frames are written to.</param>
    /// <returns>How many frames were deleted.</returns>
    private static int RemoveFramesOfEarlierFlight(string frameDirectory)
    {
        int removed = 0;
        foreach (string file in Directory.GetFiles(frameDirectory, "frame*.jpg"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            bool ours = name.Length == 10
                && int.TryParse(name.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out _);
            if (ours)
            {
                File.Delete(file);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Plans where the camera is, how high and which way it faces for each frame: evenly spaced along the route at
    /// its speed, facing the way ahead, and high enough to clear the ground in front of it.
    /// </summary>
    private static Camera[] PlanFlight(LayeredTerrain terrain, Route route, RoutePath path)
    {
        IElevationModel model = terrain.GetModel(0);
        double flightDistance = path.Distance - route.StopShortOf;

        // Two frames are the fewest that can be spaced along the flight, however short it is.
        int frameCount = Math.Max(2, (int)Math.Round(flightDistance / route.Speed * FrameRate));

        GeoCoordinate[] positions = new GeoCoordinate[frameCount];
        double[] headings = new double[frameCount];
        double[] remaining = new double[frameCount];
        double[] clearanceHeights = new double[frameCount];

        for (int frame = 0; frame < frameCount; frame++)
        {
            double along = flightDistance * frame / (frameCount - 1.0);
            positions[frame] = path.GetPosition(along);
            headings[frame] =
                Geodesic.Inverse(positions[frame], path.GetPosition(along + HeadingLookAhead)).InitialAzimuth;
            remaining[frame] = path.Distance - along;
            clearanceHeights[frame] = HighestGroundAhead(model, path, along, route.LookAhead) + route.Clearance;
        }

        double[] heights = Smooth(clearanceHeights);
        double[] smoothedHeadings = Smooth(Unwind(headings));

        return [.. Enumerable.Range(0, frameCount).Select(frame =>
            new Camera(positions[frame], heights[frame], smoothedHeadings[frame], remaining[frame]))];
    }

    private static double HighestGroundAhead(IElevationModel model, RoutePath path, double along, double lookAhead)
    {
        double highest = double.NegativeInfinity;
        for (double ahead = 0; ahead <= lookAhead; ahead += LookAheadSpacing)
        {
            highest = Math.Max(highest, model.GetElevation(path.GetPosition(along + ahead)));
        }

        return highest;
    }

    /// <summary>
    /// Follows a sequence of azimuths around the compass rather than letting it jump between 360° and 0°, so that
    /// neighboring headings can be averaged.
    /// </summary>
    private static double[] Unwind(double[] azimuths)
    {
        double[] unwound = new double[azimuths.Length];
        unwound[0] = azimuths[0];
        for (int i = 1; i < azimuths.Length; i++)
        {
            unwound[i] = unwound[i - 1] + Math.IEEERemainder(azimuths[i] - unwound[i - 1], 360);
        }

        return unwound;
    }

    /// <summary>Averages each value with its neighbors, so that the camera climbs and turns gradually.</summary>
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
        LayeredTerrain terrain, Route route, IEnumerable<Summit> summits, Camera camera, string path)
    {
        Viewpoint viewpoint = new(camera.Location, camera.Height);
        PanoramaOptions options = new()
        {
            Width = FrameWidth,
            HorizontalFieldOfView = FieldOfView,
            LeftEdgeAzimuth = camera.Heading - (FieldOfView / 2),
            TopAngle = route.TopAngle,
            BottomAngle = route.TopAngle - (FrameHeight * PixelAngle),
            MaximumDistance = route.ViewDistance,
        };

        Panorama panorama = Panorama.Render(terrain, viewpoint, options, CancellationToken.None);
        IReadOnlyList<VisibleSummit> visible = SummitVisibility.FindVisible(panorama, terrain, summits);

        SaveFrame(panorama, route, visible, camera, path);
    }

    private static void SaveFrame(
        Panorama panorama, Route route, IEnumerable<VisibleSummit> summits, Camera camera, string path)
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
        PaintCaption(canvas, route, camera);

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

    /// <summary>
    /// Writes the route's name, the camera's height and the distance still to fly, with the credits the data's
    /// licences require.
    /// </summary>
    private static void PaintCaption(SKCanvas canvas, Route route, Camera camera)
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
            $"{route.Name}    {camera.Height:N0} m    {camera.DistanceRemaining / 1000:F1} km to go");
        PaintText(canvas, caption, FrameHeight - 42, font, halo, text);
        PaintText(canvas, DataCredits.Copernicus, FrameHeight - 22, creditFont, halo, text);
        PaintText(canvas, route.Credit, FrameHeight - 10, creditFont, halo, text);
    }

    private static void PaintText(SKCanvas canvas, string text, float y, SKFont font, SKPaint halo, SKPaint fill)
    {
        canvas.DrawText(text, 20, y, SKTextAlign.Left, font, halo);
        canvas.DrawText(text, 20, y, SKTextAlign.Left, font, fill);
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
