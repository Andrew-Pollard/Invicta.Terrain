// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

namespace Invicta.Visibility;

/// <summary>Represents the position of an observer's eye, and the refraction of the air they look through.</summary>
/// <remarks>
/// <para>
/// Elevation angles account for the curvature of the WGS 84 ellipsoid exactly. Heights above sea level are treated as
/// heights above the ellipsoid; the difference, the geoid undulation, changes by only a few meters across a view, which
/// shifts angles far less than the uncertainty in refraction does.
/// </para>
/// <para>
/// Refraction bends light down around the Earth, so distant objects appear higher than they are. It follows the usual
/// model of a ray curving with a radius of the Earth's mean radius divided by the refraction coefficient, which raises
/// an object at distance d by the angle kd / 2R.
/// </para>
/// </remarks>
public sealed class Viewpoint
{
    /// <summary>The refraction coefficient of a standard atmosphere, as used in geodetic surveying.</summary>
    public const double StandardRefractionCoefficient = 0.13;

    /// <summary>The Earth's mean radius in meters, which sets the scale of refraction.</summary>
    private const double MeanEarthRadius = 6_371_008.8;

    /// <summary>Initializes a new instance of the <see cref="Viewpoint"/> class.</summary>
    /// <param name="location">The point below the eye.</param>
    /// <param name="height">The height of the eye in meters above sea level.</param>
    /// <param name="refractionCoefficient">
    /// The ratio of the Earth's radius to the radius of curvature of light, less than one. Zero ignores refraction.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="height"/> is not finite, or <paramref name="refractionCoefficient"/> is not less than one.
    /// </exception>
    public Viewpoint(
        GeoCoordinate location, double height, double refractionCoefficient = StandardRefractionCoefficient)
    {
        ArgumentChecks.ThrowIfNotFinite(height);

        // Written so that NaN fails the check too.
        if (!(refractionCoefficient < 1) || double.IsNegativeInfinity(refractionCoefficient))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refractionCoefficient), refractionCoefficient, "The coefficient must be less than one.");
        }

        Location = location;
        Height = height;
        RefractionCoefficient = refractionCoefficient;
        Position = GeocentricPosition.FromGeodetic(location, height);
        Up = GeocentricPosition.Up(location);
    }

    /// <summary>Gets the point below the eye.</summary>
    public GeoCoordinate Location { get; }

    /// <summary>Gets the height of the eye in meters above sea level.</summary>
    public double Height { get; }

    /// <summary>Gets the refraction coefficient.</summary>
    public double RefractionCoefficient { get; }

    /// <summary>Gets the position of the eye.</summary>
    internal GeocentricPosition Position { get; }

    /// <summary>Gets the unit vector pointing straight up from the eye.</summary>
    internal GeocentricPosition Up { get; }

    /// <summary>Creates a viewpoint a given height above the terrain, such as a standing person's eye level.</summary>
    /// <param name="terrain">The terrain.</param>
    /// <param name="location">The point below the eye.</param>
    /// <param name="heightAboveTerrain">The height of the eye in meters above the terrain.</param>
    /// <param name="refractionCoefficient">The refraction coefficient.</param>
    /// <returns>The viewpoint.</returns>
    public static Viewpoint AboveTerrain(
        IElevationModel terrain,
        GeoCoordinate location,
        double heightAboveTerrain,
        double refractionCoefficient = StandardRefractionCoefficient)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        return new Viewpoint(location, terrain.GetElevation(location) + heightAboveTerrain, refractionCoefficient);
    }

    /// <summary>Gets the angle at which a point appears above the horizontal, after refraction.</summary>
    /// <param name="point">The point.</param>
    /// <param name="height">The point's height in meters above sea level.</param>
    /// <param name="distance">The distance to the point in meters along the surface.</param>
    /// <returns>The apparent elevation angle in radians, negative below the horizontal.</returns>
    internal double ApparentElevationAngle(GeoCoordinate point, double height, double distance)
    {
        GeocentricPosition sightLine = GeocentricPosition.FromGeodetic(point, height) - Position;
        double sine = sightLine.Dot(Up) / sightLine.Length;
        double geometricAngle = Math.Asin(Math.Clamp(sine, -1, 1));

        return geometricAngle + (RefractionCoefficient * distance / (2 * MeanEarthRadius));
    }
}
