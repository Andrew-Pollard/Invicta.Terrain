// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

namespace Invicta.Geodesy;

/// <summary>Represents a position on the WGS 84 ellipsoid as a geodetic latitude and longitude in degrees.</summary>
public readonly record struct GeoCoordinate
{
    /// <summary>Initializes a new instance of the <see cref="GeoCoordinate"/> struct.</summary>
    /// <param name="latitude">The geodetic latitude in degrees, from -90 to 90, positive to the north.</param>
    /// <param name="longitude">The longitude in degrees, positive to the east.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="latitude"/> is not from -90 to 90, or <paramref name="longitude"/> is not finite.
    /// </exception>
    public GeoCoordinate(double latitude, double longitude)
    {
        // Written so that NaN fails the check too.
        if (!(latitude is >= -90.0 and <= 90.0))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "The latitude must be from -90 to 90.");
        }

        ArgumentChecks.ThrowIfNotFinite(longitude);

        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Gets the geodetic latitude in degrees, positive to the north.</summary>
    public double Latitude { get; }

    /// <summary>Gets the longitude in degrees, positive to the east.</summary>
    public double Longitude { get; }

    /// <summary>Returns the coordinate as latitude and longitude in decimal degrees.</summary>
    /// <returns>The latitude and longitude, such as <c>56.79685, -5.00360</c>.</returns>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.#####}, {Longitude:0.#####}");
    }
}
