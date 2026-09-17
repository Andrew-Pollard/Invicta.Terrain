// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.CommandLine;
using System.Diagnostics;

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Rendering;
using Invicta.Visibility;

namespace Invicta;

/// <summary>Creates the command that maps the ground visible from a point.</summary>
internal static class ViewshedCommand
{
    /// <summary>Creates the command.</summary>
    public static Command Create()
    {
        Option<double> latitude = CommonOptions.Latitude();
        Option<double> longitude = CommonOptions.Longitude();
        Option<double> eyeHeight = CommonOptions.EyeHeight();
        Option<double> radius = new("--radius")
        {
            Description = "The distance in kilometers to map.",
            DefaultValueFactory = _ => 50,
        };
        Option<double> resolution = new("--resolution")
        {
            Description = "The size of each map pixel in meters.",
            DefaultValueFactory = _ => 50,
        };
        Option<double> targetHeight = new("--target-height")
        {
            Description = "The height in meters above the ground of what to look for, such as 2 for a person.",
            DefaultValueFactory = _ => 0,
        };
        Option<DirectoryInfo> cache = CommonOptions.Cache();
        Option<FileInfo> output = new("--output", "-o") { Description = "The PNG file to write.", Required = true };

        Command command = new("viewshed", "Maps the ground visible from a point as a PNG image.")
        {
            latitude, longitude, eyeHeight, radius, resolution, targetHeight, cache, output,
        };

        command.SetAction(async (result, cancellationToken) =>
        {
            GeoCoordinate location = new(result.GetValue(latitude), result.GetValue(longitude));
            double radiusMeters = result.GetValue(radius) * 1000;
            double pixelSize = result.GetValue(resolution);
            CopernicusTileStore store = CommonOptions.CreateTileStore(result.GetRequiredValue(cache));

            // Rays are sampled every half pixel, so use the coarsest overview whose samples are that close.
            Stopwatch stopwatch = Stopwatch.StartNew();
            int level = OverviewLevelFor(pixelSize / 2);
            GeoBoundingBox region = GeoBoundingBox.Around(location, radiusMeters);
            CopernicusElevationModel model =
                await CopernicusElevationModel.LoadAsync(store, region, level, cancellationToken);
            Console.WriteLine($"Loaded terrain at overview level {level} in {stopwatch.Elapsed.TotalSeconds:F1} s.");

            stopwatch.Restart();
            Viewpoint viewpoint = Viewpoint.AboveTerrain(model, location, result.GetValue(eyeHeight));
            Viewshed viewshed = Viewshed.Compute(
                LayeredTerrain.FromModel(model, radiusMeters),
                viewpoint,
                radiusMeters,
                pixelSize,
                result.GetValue(targetHeight),
                cancellationToken);
            Console.WriteLine($"Computed the viewshed in {stopwatch.Elapsed.TotalSeconds:F1} s.");

            stopwatch.Restart();
            ViewshedPainter.SavePng(viewshed, model, result.GetRequiredValue(output).FullName);
            Console.WriteLine($"Painted the map in {stopwatch.Elapsed.TotalSeconds:F1} s.");

            return 0;
        });

        return command;
    }

    private static int OverviewLevelFor(double sampleSpacing)
    {
        // Each level doubles the full resolution's spacing of about 31 m, and there are three overviews.
        const double FullResolutionSpacing = 31;
        const int CoarsestLevel = 3;

        int level = 0;
        while (level < CoarsestLevel && FullResolutionSpacing * (2 << level) <= sampleSpacing)
        {
            level++;
        }

        return level;
    }
}
