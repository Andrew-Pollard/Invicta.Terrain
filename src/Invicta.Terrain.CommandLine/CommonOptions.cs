// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.CommandLine;

using Invicta.Elevation;
using Invicta.Places;

namespace Invicta;

/// <summary>Creates the options that several commands share, and acts on their values.</summary>
internal static class CommonOptions
{
    // Overpass API queries for large regions can take several minutes.
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>Creates an option for a latitude in decimal degrees.</summary>
    public static Option<double> Latitude()
    {
        return new Option<double>("--latitude", "--lat")
        {
            Description = "The latitude in decimal degrees, positive to the north.",
            Required = true,
        };
    }

    /// <summary>Creates an option for a longitude in decimal degrees.</summary>
    public static Option<double> Longitude()
    {
        return new Option<double>("--longitude", "--lon")
        {
            Description = "The longitude in decimal degrees, positive to the east.",
            Required = true,
        };
    }

    /// <summary>Creates an option for the height of the eye above the ground.</summary>
    public static Option<double> EyeHeight()
    {
        return new Option<double>("--eye-height")
        {
            Description = "The height of the eye in meters above the ground.",
            DefaultValueFactory = _ => 2,
        };
    }

    /// <summary>Creates an option for the folder that downloaded elevation tiles and summits are kept in.</summary>
    public static Option<DirectoryInfo> Cache()
    {
        return new Option<DirectoryInfo>("--cache")
        {
            Description = "The folder to keep downloaded elevation tiles and summits in.",
            DefaultValueFactory = _ => new DirectoryInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Invicta.Terrain")),
        };
    }

    /// <summary>Creates a store that keeps elevation tiles in the cache folder.</summary>
    public static CopernicusTileStore CreateTileStore(DirectoryInfo cache)
    {
        return new CopernicusTileStore(Path.Combine(cache.FullName, "copernicus"), s_httpClient);
    }

    /// <summary>Creates a store that keeps summits in the cache folder.</summary>
    public static OpenStreetMapSummitStore CreateSummitStore(DirectoryInfo cache)
    {
        return new OpenStreetMapSummitStore(Path.Combine(cache.FullName, "openstreetmap"), s_httpClient);
    }
}
