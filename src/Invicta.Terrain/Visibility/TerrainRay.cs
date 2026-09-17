// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

namespace Invicta.Visibility;

/// <summary>Samples the terrain along a line leaving a viewpoint at a fixed azimuth.</summary>
/// <param name="viewpoint">The viewpoint the ray leaves.</param>
/// <param name="terrain">The terrain to sample.</param>
/// <param name="azimuth">The azimuth in degrees clockwise from north.</param>
internal sealed class TerrainRay(Viewpoint viewpoint, IElevationModel terrain, double azimuth)
{
    private readonly GeodesicLine _line = new(viewpoint.Location, azimuth);

    /// <summary>Samples the terrain at a distance along the ray.</summary>
    /// <param name="distance">The distance in meters, which must be positive.</param>
    /// <returns>The sample.</returns>
    public TerrainSample Sample(double distance)
    {
        GeoCoordinate coordinate = _line.GetPosition(distance).Coordinate;
        double height = terrain.GetElevation(coordinate);
        double angle = viewpoint.ApparentElevationAngle(coordinate, height, distance);

        return new TerrainSample(distance, coordinate, height, angle);
    }
}
