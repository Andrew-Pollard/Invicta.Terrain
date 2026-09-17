// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.CommandLine;
using System.Globalization;

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Rendering;
using Invicta.Visibility;

namespace Invicta;

/// <summary>Creates the command that traces the line of sight between two points and draws its profile.</summary>
internal static class ProfileCommand
{
    // Enough to show the terrain's shape across the width of the image.
    private const int ProfilePointCount = 3000;

    // Half the terrain's 30 m spacing, so narrow ridges are not missed.
    private const double SampleSpacing = 15;

    /// <summary>Creates the command.</summary>
    public static Command Create()
    {
        Option<double> fromLatitude = new("--from-lat") { Description = "The eye's latitude.", Required = true };
        Option<double> fromLongitude = new("--from-lon") { Description = "The eye's longitude.", Required = true };
        Option<double> toLatitude = new("--to-lat") { Description = "The target's latitude.", Required = true };
        Option<double> toLongitude = new("--to-lon") { Description = "The target's longitude.", Required = true };
        Option<double> eyeHeight = CommonOptions.EyeHeight();
        Option<double> targetHeight = new("--target-height")
        {
            Description = "The height in meters above the ground of what to look for, such as 2 for a person.",
            DefaultValueFactory = _ => 2,
        };
        Option<double> summitSearch = new("--summit-search")
        {
            Description = "Moves each end to the highest terrain within this many meters, for mapped summits.",
            DefaultValueFactory = _ => 0,
        };
        Option<double> refraction = new("--refraction")
        {
            Description = "The refraction coefficient, from 0 for none to about 0.25 for strong.",
            DefaultValueFactory = _ => Viewpoint.StandardRefractionCoefficient,
        };
        Option<DirectoryInfo> cache = CommonOptions.Cache();
        Option<FileInfo> output = new("--output", "-o") { Description = "The PNG file to write.", Required = true };

        Command command = new("profile", "Traces the line of sight between two points and draws its profile.")
        {
            fromLatitude,
            fromLongitude,
            toLatitude,
            toLongitude,
            eyeHeight,
            targetHeight,
            summitSearch,
            refraction,
            cache,
            output,
        };

        command.SetAction(async (result, cancellationToken) =>
        {
            GeoCoordinate from = new(result.GetValue(fromLatitude), result.GetValue(fromLongitude));
            GeoCoordinate to = new(result.GetValue(toLatitude), result.GetValue(toLongitude));
            CopernicusTileStore store = CommonOptions.CreateTileStore(result.GetRequiredValue(cache));

            GeoBoundingBox region = new(
                Math.Min(from.Latitude, to.Latitude),
                Math.Min(from.Longitude, to.Longitude),
                Math.Max(from.Latitude, to.Latitude),
                Math.Max(from.Longitude, to.Longitude));
            CopernicusElevationModel terrain =
                await CopernicusElevationModel.LoadAsync(store, region, 0, cancellationToken);

            double searchHalfWidth = result.GetValue(summitSearch);
            (GeoCoordinate eye, double eyeGround) = terrain.FindHighestPoint(from, searchHalfWidth, 5);
            (GeoCoordinate target, double targetGround) = terrain.FindHighestPoint(to, searchHalfWidth, 5);

            Viewpoint viewpoint = new(eye, eyeGround + result.GetValue(eyeHeight), result.GetValue(refraction));
            double targetTop = targetGround + result.GetValue(targetHeight);
            SightLineProfile profile = SightLineProfile.Trace(
                terrain, viewpoint, target, targetTop, SampleSpacing, ProfilePointCount);

            string title = string.Create(
                CultureInfo.InvariantCulture, $"From {eye} ({eyeGround:0} m) to {target} ({targetGround:0} m)");
            ProfilePainter.SavePng(profile, title, result.GetRequiredValue(output).FullName);

            LineOfSightResult trace = profile.Result;
            string distance = string.Create(CultureInfo.InvariantCulture, $"{trace.Distance / 1000:0.00} km");
            string verdict = trace.IsVisible || trace.Obstruction is not { } obstruction
                ? "visible"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"hidden by {obstruction.Coordinate} ({obstruction.Height:0} m), "
                    + $"{obstruction.Distance / 1000:0.0} km out");
            Console.WriteLine($"{distance}, {verdict}.");

            return 0;
        });

        return command;
    }
}
