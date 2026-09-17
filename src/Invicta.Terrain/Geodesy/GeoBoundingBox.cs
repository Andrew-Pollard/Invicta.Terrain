// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Geodesy;

/// <summary>Represents a region bounded by two parallels and two meridians.</summary>
public readonly record struct GeoBoundingBox
{
    // Sampling the circle every quarter degree misses its true extent by at most r(1 - cos(1/8°)), 1 m at 450 km.
    private const int AroundAzimuthCount = 1440;

    /// <summary>Initializes a new instance of the <see cref="GeoBoundingBox"/> struct.</summary>
    /// <param name="south">The southern latitude in degrees.</param>
    /// <param name="west">The western longitude in degrees.</param>
    /// <param name="north">The northern latitude in degrees, no less than <paramref name="south"/>.</param>
    /// <param name="east">The eastern longitude in degrees, no less than <paramref name="west"/>.</param>
    /// <remarks>
    /// A box that crosses the antimeridian has a longitude outside the range -180 to 180, such as an eastern longitude
    /// of 190.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A latitude is not from -90 to 90, a longitude is not finite, or the box is inverted.
    /// </exception>
    public GeoBoundingBox(double south, double west, double north, double east)
    {
        GeoCoordinate southWest = new(south, west);
        GeoCoordinate northEast = new(north, east);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(south, north);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(west, east);

        South = southWest.Latitude;
        West = southWest.Longitude;
        North = northEast.Latitude;
        East = northEast.Longitude;
    }

    /// <summary>Gets the southern latitude in degrees.</summary>
    public double South { get; }

    /// <summary>Gets the western longitude in degrees.</summary>
    public double West { get; }

    /// <summary>Gets the northern latitude in degrees.</summary>
    public double North { get; }

    /// <summary>Gets the eastern longitude in degrees.</summary>
    public double East { get; }

    /// <summary>Creates a box that contains every point within a distance of a center.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The distance in meters.</param>
    /// <returns>
    /// A box containing the circle to within a meter per 450 km of radius, which spans every longitude if the circle
    /// contains a pole.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or not finite.</exception>
    public static GeoBoundingBox Around(GeoCoordinate center, double radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        if (!double.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "The radius must be finite.");
        }

        double westernmost = center.Longitude - 180;
        double easternmost = center.Longitude + 180;
        if (radius >= DistanceToPole(center, 90))
        {
            return new GeoBoundingBox(SouthernmostLatitude(center, radius), westernmost, 90, easternmost);
        }

        if (radius >= DistanceToPole(center, -90))
        {
            return new GeoBoundingBox(-90, westernmost, NorthernmostLatitude(center, radius), easternmost);
        }

        double south = center.Latitude;
        double north = center.Latitude;
        double westOffset = 0;
        double eastOffset = 0;
        for (int i = 0; i < AroundAzimuthCount; i++)
        {
            GeodesicLine line = new(center, 360.0 * i / AroundAzimuthCount);
            GeoCoordinate point = line.GetPosition(radius).Coordinate;
            double longitudeOffset = Math.IEEERemainder(point.Longitude - center.Longitude, 360);

            south = Math.Min(south, point.Latitude);
            north = Math.Max(north, point.Latitude);
            westOffset = Math.Min(westOffset, longitudeOffset);
            eastOffset = Math.Max(eastOffset, longitudeOffset);
        }

        return new GeoBoundingBox(south, center.Longitude + westOffset, north, center.Longitude + eastOffset);
    }

    private static double SouthernmostLatitude(GeoCoordinate center, double radius)
    {
        return radius >= DistanceToPole(center, -90)
            ? -90
            : new GeodesicLine(center, 180).GetPosition(radius).Coordinate.Latitude;
    }

    private static double NorthernmostLatitude(GeoCoordinate center, double radius)
    {
        return radius >= DistanceToPole(center, 90)
            ? 90
            : new GeodesicLine(center, 0).GetPosition(radius).Coordinate.Latitude;
    }

    private static double DistanceToPole(GeoCoordinate center, double poleLatitude)
    {
        return Geodesic.Inverse(center, new GeoCoordinate(poleLatitude, center.Longitude)).Distance;
    }
}
