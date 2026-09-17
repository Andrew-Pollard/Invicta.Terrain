// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

namespace Invicta.Visibility;

/// <summary>Describes a point on the terrain as seen from a viewpoint.</summary>
/// <param name="Distance">The distance from the viewpoint in meters along the surface.</param>
/// <param name="Coordinate">The point's latitude and longitude.</param>
/// <param name="Height">The height of the terrain in meters above sea level.</param>
/// <param name="ElevationAngle">The apparent angle above the horizontal in radians, after refraction.</param>
public readonly record struct TerrainSample(
    double Distance, GeoCoordinate Coordinate, double Height, double ElevationAngle);
