// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Geodesy;

/// <summary>Describes a point along a geodesic.</summary>
/// <param name="Coordinate">The point's latitude and longitude, with the longitude from -180 to 180.</param>
/// <param name="Azimuth">The geodesic's azimuth at the point in degrees clockwise from north, from -180 to 180.</param>
public readonly record struct GeodesicPosition(GeoCoordinate Coordinate, double Azimuth);
