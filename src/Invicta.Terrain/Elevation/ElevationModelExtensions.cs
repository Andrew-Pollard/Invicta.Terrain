// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

namespace Invicta.Elevation;

/// <summary>Provides searches of an <see cref="IElevationModel"/>.</summary>
public static class ElevationModelExtensions
{
    private const double MetersPerDegreeOfLatitude = 111_320;

    /// <summary>
    /// Finds the highest terrain in a square around a point, such as the top of a summit whose mapped position is a
    /// little off.
    /// </summary>
    /// <param name="model">The elevation model.</param>
    /// <param name="center">The center of the square.</param>
    /// <param name="halfWidth">Half the width of the square in meters.</param>
    /// <param name="spacing">The distance in meters between the points searched.</param>
    /// <returns>The highest point searched, and its height in meters above sea level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="halfWidth"/> is negative, or <paramref name="spacing"/> is not positive.
    /// </exception>
    public static (GeoCoordinate Coordinate, double Height) FindHighestPoint(
        this IElevationModel model, GeoCoordinate center, double halfWidth, double spacing)
    {
        ArgumentNullException.ThrowIfNull(model);

        ArgumentOutOfRangeException.ThrowIfNegative(halfWidth);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);

        int steps = (int)Math.Floor(halfWidth / spacing);
        double latitudeStep = spacing / MetersPerDegreeOfLatitude;
        double longitudeStep = latitudeStep / Math.Max(0.01, Math.Cos(center.Latitude * Math.PI / 180));

        GeoCoordinate highest = center;
        double highestHeight = model.GetElevation(center);
        for (int north = -steps; north <= steps; north++)
        {
            for (int east = -steps; east <= steps; east++)
            {
                double latitude = Math.Clamp(center.Latitude + (north * latitudeStep), -90, 90);
                GeoCoordinate point = new(latitude, center.Longitude + (east * longitudeStep));
                double height = model.GetElevation(point);
                if (height > highestHeight)
                {
                    highest = point;
                    highestHeight = height;
                }
            }
        }

        return (highest, highestHeight);
    }
}
