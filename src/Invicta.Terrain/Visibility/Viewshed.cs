// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Collections;

using Invicta.Elevation;

namespace Invicta.Visibility;

/// <summary>
/// Represents the ground visible from a viewpoint within a radius, computed along rays spaced so that neighboring rays
/// are no further apart than the resolution at the edge.
/// </summary>
/// <remarks>
/// A point on the ground counts as visible when a target at the given height above it could be seen. Each ray marches
/// outward, and a point is visible when its apparent elevation angle reaches the highest angle of the terrain before
/// it.
/// </remarks>
public sealed class Viewshed
{
    private readonly BitArray[] _rays;
    private readonly double _azimuthStep;
    private readonly double _sampleSpacing;

    private Viewshed(Viewpoint viewpoint, double radius, double resolution, int rayCount)
    {
        Viewpoint = viewpoint;
        Radius = radius;
        Resolution = resolution;
        _azimuthStep = 360.0 / rayCount;
        _sampleSpacing = resolution / 2;
        _rays = new BitArray[rayCount];
    }

    /// <summary>Gets the viewpoint.</summary>
    public Viewpoint Viewpoint { get; }

    /// <summary>Gets the distance in meters that the viewshed reaches.</summary>
    public double Radius { get; }

    /// <summary>Gets the spacing in meters that the viewshed resolves at its edge.</summary>
    public double Resolution { get; }

    /// <summary>Computes the ground visible from a viewpoint.</summary>
    /// <param name="terrain">The terrain, reaching <paramref name="radius"/>.</param>
    /// <param name="viewpoint">The viewpoint.</param>
    /// <param name="radius">The distance in meters to compute visibility to.</param>
    /// <param name="resolution">The spacing in meters to resolve at the edge, and twice the sample spacing.</param>
    /// <param name="targetHeight">The height in meters above the ground of the targets to look for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The viewshed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="resolution"/> is not positive and finite, or
    /// <paramref name="targetHeight"/> is negative or not finite.
    /// </exception>
    public static Viewshed Compute(
        LayeredTerrain terrain,
        Viewpoint viewpoint,
        double radius,
        double resolution,
        double targetHeight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        ArgumentNullException.ThrowIfNull(viewpoint);

        ArgumentChecks.ThrowIfNotPositiveAndFinite(radius);

        ArgumentChecks.ThrowIfNotPositiveAndFinite(resolution);

        ArgumentChecks.ThrowIfNotFinite(targetHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(targetHeight);

        // Neighboring rays are one resolution apart at the edge.
        int rayCount = (int)Math.Ceiling(2 * Math.PI * radius / resolution);
        Viewshed viewshed = new(viewpoint, radius, resolution, rayCount);

        ParallelOptions options = new() { CancellationToken = cancellationToken };
        Parallel.For(0, rayCount, options, ray => viewshed.ComputeRay(terrain, ray, targetHeight));

        return viewshed;
    }

    /// <summary>Gets a value indicating whether the ground at a distance and azimuth is visible.</summary>
    /// <param name="distance">The distance in meters.</param>
    /// <param name="azimuth">The azimuth in degrees clockwise from north.</param>
    /// <returns>
    /// <see langword="true"/> if the nearest sample is visible, or the point is at the viewer's feet;
    /// <see langword="false"/> if it is hidden or beyond the radius.
    /// </returns>
    public bool IsVisible(double distance, double azimuth)
    {
        if (distance > Radius)
        {
            return false;
        }

        int sample = (int)Math.Round(distance / _sampleSpacing) - 1;
        if (sample < 0)
        {
            return true;
        }

        int ray = (int)Math.Round(azimuth / _azimuthStep);
        ray = ((ray % _rays.Length) + _rays.Length) % _rays.Length;

        BitArray samples = _rays[ray];

        return sample < samples.Length && samples[sample];
    }

    private void ComputeRay(LayeredTerrain terrain, int rayIndex, double targetHeight)
    {
        TerrainRay ray = new(Viewpoint, terrain, rayIndex * _azimuthStep);
        int sampleCount = (int)Math.Floor(Radius / _sampleSpacing);
        BitArray visible = new(sampleCount);

        double highestAngle = double.NegativeInfinity;
        for (int i = 0; i < sampleCount; i++)
        {
            double distance = (i + 1) * _sampleSpacing;
            TerrainSample sample = ray.Sample(distance);

            // A target above the ground is seen over the terrain before it, but blocks nothing itself.
            double targetAngle = targetHeight == 0
                ? sample.ElevationAngle
                : Viewpoint.ApparentElevationAngle(sample.Coordinate, sample.Height + targetHeight, distance);
            visible[i] = targetAngle >= highestAngle;
            highestAngle = Math.Max(highestAngle, sample.ElevationAngle);
        }

        _rays[rayIndex] = visible;
    }
}
