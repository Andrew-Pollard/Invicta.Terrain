// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

namespace Invicta.Visibility;

/// <summary>
/// Represents the terrain along a line of sight as it appears from the viewpoint, for drawing as a cross-section.
/// </summary>
/// <remarks>
/// Each point's apparent height is the height at which it would have to stand on a flat Earth without refraction to
/// appear at the same angle. In those terms the line of sight is straight and the terrain drops away with the Earth's
/// curvature, so anything above the line blocks the view.
/// </remarks>
public sealed class SightLineProfile
{
    private readonly ProfilePoint[] _points;

    private SightLineProfile(
        LineOfSightResult result, double eyeHeight, double targetApparentHeight, ProfilePoint[] points)
    {
        Result = result;
        EyeHeight = eyeHeight;
        TargetApparentHeight = targetApparentHeight;
        _points = points;
    }

    /// <summary>Gets the result of tracing the line of sight.</summary>
    public LineOfSightResult Result { get; }

    /// <summary>Gets the height of the eye in meters above sea level.</summary>
    public double EyeHeight { get; }

    /// <summary>Gets the apparent height of the target in meters.</summary>
    public double TargetApparentHeight { get; }

    /// <summary>Gets the points along the line, in order of distance.</summary>
    public IReadOnlyList<ProfilePoint> Points => _points;

    /// <summary>Traces a line of sight and samples the terrain along it for drawing.</summary>
    /// <param name="terrain">The terrain between the viewpoint and the target.</param>
    /// <param name="viewpoint">The viewpoint.</param>
    /// <param name="target">The point below the target.</param>
    /// <param name="targetHeight">The height of the target in meters above sea level.</param>
    /// <param name="sampleSpacing">The distance in meters between samples when tracing the line of sight.</param>
    /// <param name="pointCount">The number of points to sample for drawing.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pointCount"/> is less than two.</exception>
    public static SightLineProfile Trace(
        IElevationModel terrain,
        Viewpoint viewpoint,
        GeoCoordinate target,
        double targetHeight,
        double sampleSpacing,
        int pointCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pointCount, 2);

        LineOfSightResult result = LineOfSight.Trace(terrain, viewpoint, target, targetHeight, sampleSpacing);

        TerrainRay ray = new(viewpoint, LayeredTerrain.FromModel(terrain, result.Distance), result.Azimuth);
        ProfilePoint[] points = new ProfilePoint[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            // Start just past the eye, where the ray has a direction.
            double distance = double.Max(1, result.Distance * i / (pointCount - 1));
            TerrainSample sample = ray.Sample(distance);
            double seaLevelAngle = viewpoint.ApparentElevationAngle(sample.Coordinate, 0, distance);

            points[i] = new ProfilePoint(
                distance,
                sample.Height,
                ApparentHeight(viewpoint, distance, sample.ElevationAngle),
                ApparentHeight(viewpoint, distance, seaLevelAngle));
        }

        double targetApparentHeight = ApparentHeight(viewpoint, result.Distance, result.TargetElevationAngle);

        return new SightLineProfile(result, viewpoint.Height, targetApparentHeight, points);
    }

    private static double ApparentHeight(Viewpoint viewpoint, double distance, double elevationAngle)
    {
        return viewpoint.Height + (distance * double.Tan(elevationAngle));
    }
}
