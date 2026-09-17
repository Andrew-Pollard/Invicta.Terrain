// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

namespace Invicta.Visibility;

/// <summary>Determines whether a target can be seen from a viewpoint over the terrain between them.</summary>
public static class LineOfSight
{
    /// <summary>Traces the line of sight from a viewpoint to a target.</summary>
    /// <param name="terrain">The terrain between them.</param>
    /// <param name="viewpoint">The viewpoint.</param>
    /// <param name="target">The point below the target.</param>
    /// <param name="targetHeight">The height of the target in meters above sea level.</param>
    /// <param name="sampleSpacing">
    /// The distance in meters between terrain samples. Half the terrain's grid spacing avoids missing narrow ridges.
    /// </param>
    /// <returns>The target's elevation angle and the highest obstruction before it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="targetHeight"/> is not finite, or <paramref name="sampleSpacing"/> is not positive and finite.
    /// </exception>
    /// <remarks>
    /// Samples stop half a spacing short of the target, so a target on the ground can be seen unless the ground in
    /// front of it rises above the line of sight. Beyond a few kilometers that includes level ground, which the
    /// Earth's curvature makes appear higher the nearer it is, so a target exactly on a rounded summit is often hidden
    /// by the summit itself. To ask whether a summit can be seen, look for a target a little above it, such as a
    /// person standing there.
    /// </remarks>
    public static LineOfSightResult Trace(
        IElevationModel terrain, Viewpoint viewpoint, GeoCoordinate target, double targetHeight, double sampleSpacing)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        ArgumentNullException.ThrowIfNull(viewpoint);

        ArgumentChecks.ThrowIfNotFinite(targetHeight);

        ArgumentChecks.ThrowIfNotPositiveAndFinite(sampleSpacing);

        GeodesicSolution path = Geodesic.Inverse(viewpoint.Location, target);
        double targetAngle = viewpoint.ApparentElevationAngle(target, targetHeight, path.Distance);

        TerrainRay ray = new(viewpoint, LayeredTerrain.FromModel(terrain, path.Distance), path.InitialAzimuth);
        TerrainSample? obstruction = null;
        for (double distance = sampleSpacing; distance < path.Distance - (sampleSpacing / 2); distance += sampleSpacing)
        {
            TerrainSample sample = ray.Sample(distance);
            if (obstruction is null || sample.ElevationAngle > obstruction.Value.ElevationAngle)
            {
                obstruction = sample;
            }
        }

        return new LineOfSightResult(path.Distance, path.InitialAzimuth, targetAngle, obstruction);
    }
}
