// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Geodesy;

/// <summary>
/// Represents a position or displacement in Earth-centered, Earth-fixed Cartesian coordinates, in meters: X toward
/// latitude 0° longitude 0°, Y toward longitude 90° E, and Z toward the North Pole.
/// </summary>
internal readonly record struct GeocentricPosition(double X, double Y, double Z)
{
    private const double EccentricitySquared = Wgs84.Flattening * (2 - Wgs84.Flattening);

    /// <summary>Gets the length of the vector.</summary>
    public double Length => Math.Sqrt(Dot(this));

    /// <summary>Converts a latitude, longitude and height above the ellipsoid to geocentric coordinates.</summary>
    public static GeocentricPosition FromGeodetic(GeoCoordinate coordinate, double height)
    {
        double latitude = coordinate.Latitude * Math.PI / 180;
        double longitude = coordinate.Longitude * Math.PI / 180;
        double sinLatitude = Math.Sin(latitude);
        double cosLatitude = Math.Cos(latitude);

        // The prime vertical radius of curvature.
        double n = Wgs84.EquatorialRadius / Math.Sqrt(1 - (EccentricitySquared * sinLatitude * sinLatitude));

        return new GeocentricPosition(
            (n + height) * cosLatitude * Math.Cos(longitude),
            (n + height) * cosLatitude * Math.Sin(longitude),
            ((n * (1 - EccentricitySquared)) + height) * sinLatitude);
    }

    /// <summary>Gets the unit vector normal to the ellipsoid at a point, pointing up.</summary>
    public static GeocentricPosition Up(GeoCoordinate coordinate)
    {
        double latitude = coordinate.Latitude * Math.PI / 180;
        double longitude = coordinate.Longitude * Math.PI / 180;

        return new GeocentricPosition(
            Math.Cos(latitude) * Math.Cos(longitude),
            Math.Cos(latitude) * Math.Sin(longitude),
            Math.Sin(latitude));
    }

    /// <summary>Gets the displacement from one position to another.</summary>
    public static GeocentricPosition operator -(GeocentricPosition left, GeocentricPosition right)
    {
        return new GeocentricPosition(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    /// <summary>Computes the dot product with another vector.</summary>
    public double Dot(GeocentricPosition other)
    {
        return (X * other.X) + (Y * other.Y) + (Z * other.Z);
    }
}
