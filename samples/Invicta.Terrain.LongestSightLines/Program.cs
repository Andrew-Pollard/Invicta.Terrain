// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Places;
using Invicta.Rendering;
using Invicta.Visibility;

namespace Invicta;

/// <summary>
/// Searches Great Britain, Ireland and the Isle of Man for the longest lines of sight between named summits, and
/// writes the results as Markdown tables with profiles of the longest.
/// </summary>
internal static class Program
{
    // Latitude and longitude bounds that take in the islands but not France or Norway.
    private static readonly GeoBoundingBox s_region = new(49.8, -10.7, 60.9, 1.8);

    // Lower summits cannot see far enough to matter: even with strong refraction, two 300 m summits' horizons meet
    // at only 143 km.
    private const double MinimumSummitHeight = 300;
    private const double MinimumDistance = 150_000;

    // Mapped summits are moved to the highest terrain within this distance.
    private const double SummitSearchHalfWidth = 60;

    // Someone stands on each summit. A target exactly on a rounded summit is often hidden by the summit itself,
    // which the Earth's curvature makes appear higher just in front of it.
    private const double EyeHeight = 2;
    private const double SampleSpacing = 15;

    // Pairs that cannot see each other even with this much refraction are left out.
    private const double MaximumRefractionCoefficient = 0.25;

    private const double StandardRefraction = Viewpoint.StandardRefractionCoefficient;

    private const double MeanEarthRadius = 6_371_000;
    private const int ResultCount = 15;
    private const int ProfileCount = 3;
    private const int ProfilePointCount = 3000;

    private static async Task Main(string[] args)
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDirectory = args.Length > 0 ? args[0] : Path.Combine(localData, "Invicta.Terrain");
        string outputDirectory = args.Length > 1 ? args[1] : Environment.CurrentDirectory;
        Directory.CreateDirectory(outputDirectory);

        using HttpClient httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
        Stopwatch stopwatch = Stopwatch.StartNew();

        OpenStreetMapSummitStore summitStore = new(Path.Combine(cacheDirectory, "openstreetmap"), httpClient);
        IReadOnlyList<Summit> summits = await summitStore.GetSummitsAsync(s_region, CancellationToken.None);
        Report(stopwatch, $"Found {summits.Count} named summits.");

        CopernicusTileStore tileStore = new(Path.Combine(cacheDirectory, "copernicus"), httpClient);
        CopernicusElevationModel terrain =
            await CopernicusElevationModel.LoadAsync(tileStore, s_region, 0, CancellationToken.None);
        Report(stopwatch, "Loaded the terrain at full resolution.");

        List<SearchSummit> candidates = PlaceOnTerrain(terrain, summits);
        Report(stopwatch, $"Kept {candidates.Count} summits at least {MinimumSummitHeight} m high.");

        List<(SearchSummit From, SearchSummit To, double Distance)> pairs = PairsWithinHorizons(candidates);
        Report(stopwatch, $"Kept {pairs.Count} pairs over {MinimumDistance / 1000} km apart whose horizons meet.");

        List<SightLine> sightLines = TracePairs(terrain, pairs);
        Report(stopwatch, $"Found {sightLines.Count} pairs that can see each other.");

        ILookup<bool, SightLine> byRefraction =
            sightLines.ToLookup(line => line.LeastRefractionCoefficient <= StandardRefraction);
        List<SightLine> standard = Longest(byRefraction[true]);
        List<SightLine> strong = Longest(byRefraction[false]);
        string report = $"""
            ## Visible with standard refraction

            {FormatTable(standard)}
            ## Visible only with stronger refraction

            {FormatTable(strong)}
            """;
        string reportPath = Path.Combine(outputDirectory, "longest-sight-lines.md");
        await File.WriteAllTextAsync(reportPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        DrawProfiles(terrain, standard, outputDirectory, "standard", StandardRefraction);
        DrawProfiles(terrain, strong, outputDirectory, "strong", MaximumRefractionCoefficient);
        Report(stopwatch, $"Wrote the results to {Path.GetFullPath(outputDirectory)}.");
    }

    /// <summary>Draws the profiles of the first few sight lines, with the refraction that shows them visible.</summary>
    private static void DrawProfiles(
        IElevationModel terrain, List<SightLine> sightLines, string directory, string name, double refraction)
    {
        for (int i = 0; i < int.Min(ProfileCount, sightLines.Count); i++)
        {
            SightLine line = sightLines[i];
            Viewpoint viewpoint = new(line.From.Coordinate, line.From.Height + EyeHeight, refraction);
            SightLineProfile profile = SightLineProfile.Trace(
                terrain, viewpoint, line.To.Coordinate, line.To.Height + EyeHeight, SampleSpacing, ProfilePointCount);

            string title = string.Create(
                CultureInfo.InvariantCulture,
                $"{Describe(line.From)} to {Describe(line.To)}, with refraction coefficient {refraction:0.00}");
            ProfilePainter.SavePng(profile, title, Path.Combine(directory, $"profile-{name}-{i + 1}.png"));
        }
    }

    private static void Report(Stopwatch stopwatch, string message)
    {
        Console.WriteLine($"[{stopwatch.Elapsed:mm\\:ss}] {message}");
    }

    /// <summary>Moves each summit to the highest terrain near its mapped position, and drops low ones.</summary>
    private static List<SearchSummit> PlaceOnTerrain(IElevationModel terrain, IReadOnlyList<Summit> summits)
    {
        ConcurrentBag<SearchSummit> placed = [];
        Parallel.ForEach(summits, summit =>
        {
            (GeoCoordinate top, double height) =
                terrain.FindHighestPoint(summit.Coordinate, SummitSearchHalfWidth, SampleSpacing);
            if (height >= MinimumSummitHeight)
            {
                placed.Add(new SearchSummit(summit.Name, top, height));
            }
        });

        // Sort, so that the search does not depend on the order the parallel loop finished in.
        return [.. placed
            .OrderBy(summit => summit.Name, StringComparer.Ordinal)
            .ThenBy(summit => summit.Coordinate.Latitude)
            .ThenBy(summit => summit.Coordinate.Longitude)];
    }

    /// <summary>
    /// Finds the pairs far enough apart to be interesting whose horizons over a smooth Earth, with the most
    /// refraction considered, reach each other.
    /// </summary>
    private static List<(SearchSummit From, SearchSummit To, double Distance)> PairsWithinHorizons(
        List<SearchSummit> summits)
    {
        ConcurrentBag<(SearchSummit, SearchSummit, double)> pairs = [];
        Parallel.For(0, summits.Count, i =>
        {
            SearchSummit from = summits[i];
            for (int j = i + 1; j < summits.Count; j++)
            {
                SearchSummit to = summits[j];
                double reach = HorizonDistance(from.Height + EyeHeight) + HorizonDistance(to.Height + EyeHeight);
                if (ApproximateDistance(from.Coordinate, to.Coordinate) > reach * 1.01)
                {
                    continue;
                }

                double distance = Geodesic.Inverse(from.Coordinate, to.Coordinate).Distance;
                if (distance >= MinimumDistance && distance <= reach)
                {
                    pairs.Add((from, to, distance));
                }
            }
        });

        return [.. pairs];
    }

    /// <summary>Gets the distance to the horizon over a smooth Earth from a height, with the most refraction.</summary>
    private static double HorizonDistance(double height)
    {
        return double.Sqrt(2 * MeanEarthRadius * height / (1 - MaximumRefractionCoefficient));
    }

    /// <summary>Gets a quick distance on a sphere, good to about half a percent, to rule out distant pairs.</summary>
    private static double ApproximateDistance(GeoCoordinate first, GeoCoordinate second)
    {
        double meanLatitude = (first.Latitude + second.Latitude) / 2 * double.Pi / 180;
        double north = (second.Latitude - first.Latitude) * double.Pi / 180;
        double east = (second.Longitude - first.Longitude) * double.Pi / 180 * double.Cos(meanLatitude);

        return MeanEarthRadius * double.Sqrt((north * north) + (east * east));
    }

    /// <summary>
    /// Traces each pair with the most refraction considered, and for those that see each other, finds the least
    /// refraction that still allows it. Someone stands on each summit, with their eyes at the same height, so the line
    /// of sight is the same whichever way it is traced.
    /// </summary>
    private static List<SightLine> TracePairs(
        IElevationModel terrain, List<(SearchSummit From, SearchSummit To, double Distance)> pairs)
    {
        ConcurrentBag<SightLine> sightLines = [];
        int traced = 0;
        Parallel.ForEach(pairs, pair =>
        {
            if (LeastRefractionForVisibility(terrain, pair.From, pair.To) is double leastRefraction)
            {
                sightLines.Add(new SightLine(pair.From, pair.To, pair.Distance, leastRefraction));
            }

            int count = Interlocked.Increment(ref traced);
            if (count % 250_000 == 0)
            {
                Console.WriteLine($"  traced {count} of {pairs.Count} pairs");
            }
        });

        return [.. sightLines];
    }

    private static bool IsVisible(IElevationModel terrain, SearchSummit from, SearchSummit to, double refraction)
    {
        Viewpoint viewpoint = new(from.Coordinate, from.Height + EyeHeight, refraction);

        return LineOfSight.Trace(terrain, viewpoint, to.Coordinate, to.Height + EyeHeight, SampleSpacing).IsVisible;
    }

    /// <summary>
    /// Finds the least refraction coefficient at which one summit can see another, to 0.005, by bisection. More
    /// refraction only lifts distant terrain more than near terrain, so visibility never goes away as it grows.
    /// </summary>
    /// <returns>The coefficient, or <see langword="null"/> if the most refraction considered falls short.</returns>
    private static double? LeastRefractionForVisibility(IElevationModel terrain, SearchSummit from, SearchSummit to)
    {
        if (!IsVisible(terrain, from, to, MaximumRefractionCoefficient))
        {
            return null;
        }

        double hidden = -0.1;
        double visible = MaximumRefractionCoefficient;
        while (visible - hidden > 0.005)
        {
            double middle = (hidden + visible) / 2;
            if (IsVisible(terrain, from, to, middle))
            {
                visible = middle;
            }
            else
            {
                hidden = middle;
            }
        }

        return visible;
    }

    /// <summary>
    /// Takes the longest sight lines, skipping any whose ends are both within 10 km of a longer one's ends, which is
    /// usually the same view between neighboring tops.
    /// </summary>
    private static List<SightLine> Longest(IEnumerable<SightLine> sightLines)
    {
        List<SightLine> longest = [];
        IEnumerable<SightLine> byDistance = sightLines
            .OrderByDescending(line => line.Distance)
            .ThenBy(line => line.From.Name, StringComparer.Ordinal);
        foreach (SightLine sightLine in byDistance)
        {
            if (!longest.Exists(kept => IsNear(kept, sightLine)))
            {
                longest.Add(sightLine);
                if (longest.Count == ResultCount)
                {
                    break;
                }
            }
        }

        return longest;
    }

    private static bool IsNear(SightLine first, SightLine second)
    {
        const double Nearby = 10_000;

        bool sameWay = ApproximateDistance(first.From.Coordinate, second.From.Coordinate) < Nearby
            && ApproximateDistance(first.To.Coordinate, second.To.Coordinate) < Nearby;
        bool oppositeWay = ApproximateDistance(first.From.Coordinate, second.To.Coordinate) < Nearby
            && ApproximateDistance(first.To.Coordinate, second.From.Coordinate) < Nearby;

        return sameWay || oppositeWay;
    }

    private static string FormatTable(List<SightLine> sightLines)
    {
        StringBuilder table = new();
        table.AppendLine("| Distance | From | To | Least refraction coefficient |");
        table.AppendLine("|---:|---|---|---:|");
        foreach (SightLine line in sightLines)
        {
            string distance = string.Create(CultureInfo.InvariantCulture, $"{line.Distance / 1000:0.0} km");
            string refraction = line.LeastRefractionCoefficient.ToString("0.000", CultureInfo.InvariantCulture);
            table.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {distance} | {Describe(line.From)} | {Describe(line.To)} | {refraction} |");
        }

        return table.ToString();
    }

    private static string Describe(SearchSummit summit)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{summit.Name} ({summit.Height:0} m)");
    }

    /// <summary>Describes a summit placed on the terrain.</summary>
    private sealed record SearchSummit(string Name, GeoCoordinate Coordinate, double Height);

    /// <summary>Describes a pair of summits that can see each other.</summary>
    private sealed record SightLine(
        SearchSummit From, SearchSummit To, double Distance, double LeastRefractionCoefficient);
}
