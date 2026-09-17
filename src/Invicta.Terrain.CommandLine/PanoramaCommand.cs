// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.CommandLine;
using System.Diagnostics;

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Places;
using Invicta.Rendering;
using Invicta.Visibility;

namespace Invicta;

/// <summary>Creates the command that renders the 360° view from a point.</summary>
internal static class PanoramaCommand
{
    /// <summary>Creates the command.</summary>
    public static Command Create()
    {
        Option<double> latitude = CommonOptions.Latitude();
        Option<double> longitude = CommonOptions.Longitude();
        Option<double> eyeHeight = CommonOptions.EyeHeight();
        Option<DirectoryInfo> cache = CommonOptions.Cache();
        Option<int> width = new("--width")
        {
            Description = "The width of the image in pixels, which spans 360°.",
            DefaultValueFactory = _ => 7200,
        };
        Option<double> maximumDistance = new("--max-distance")
        {
            Description = "The distance in kilometers beyond which terrain is ignored.",
            DefaultValueFactory = _ => 450,
        };
        Option<FileInfo> output = new("--output", "-o")
        {
            Description = "The PNG file to write.",
            Required = true,
        };

        Command command = new("panorama", "Renders the 360° view from a point as a PNG image.")
        {
            latitude, longitude, eyeHeight, width, maximumDistance, cache, output,
        };

        command.SetAction(async (result, cancellationToken) =>
        {
            GeoCoordinate location = new(result.GetValue(latitude), result.GetValue(longitude));
            PanoramaOptions options = new()
            {
                Width = result.GetValue(width),
                MaximumDistance = result.GetValue(maximumDistance) * 1000,
            };
            CopernicusTileStore store = CommonOptions.CreateTileStore(result.GetRequiredValue(cache));

            Stopwatch stopwatch = Stopwatch.StartNew();
            LayeredTerrain terrain = await CopernicusElevationModel.LoadLayeredAsync(
                store, location, options.MaximumDistance, 360.0 / options.Width * Math.PI / 180, cancellationToken);
            Console.WriteLine($"Loaded terrain in {stopwatch.Elapsed.TotalSeconds:F1} s.");

            stopwatch.Restart();
            Viewpoint viewpoint = Viewpoint.AboveTerrain(terrain.GetModel(0), location, result.GetValue(eyeHeight));
            Panorama panorama = Panorama.Render(terrain, viewpoint, options, cancellationToken);
            Console.WriteLine(
                $"Rendered {panorama.Width} x {panorama.Height} pixels in {stopwatch.Elapsed.TotalSeconds:F1} s.");

            stopwatch.Restart();
            OpenStreetMapSummitStore summitStore = CommonOptions.CreateSummitStore(result.GetRequiredValue(cache));
            GeoBoundingBox region = GeoBoundingBox.Around(location, options.MaximumDistance);
            IReadOnlyList<Summit> summits = await summitStore.GetSummitsAsync(region, cancellationToken);
            IReadOnlyList<VisibleSummit> visible = SummitVisibility.FindVisible(panorama, terrain, summits);
            Console.WriteLine(
                $"Found {visible.Count} of {summits.Count} summits visible in {stopwatch.Elapsed.TotalSeconds:F1} s.");

            PanoramaPainter.SavePng(panorama, visible, result.GetRequiredValue(output).FullName);

            return 0;
        });

        return command;
    }
}
