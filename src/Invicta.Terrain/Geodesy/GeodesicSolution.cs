// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Geodesy;

/// <summary>Describes the shortest path between two points on the ellipsoid.</summary>
/// <param name="Distance">The length of the path in meters.</param>
/// <param name="InitialAzimuth">The azimuth at the start in degrees clockwise from north, from -180 to 180.</param>
/// <param name="FinalAzimuth">The azimuth at the end in degrees clockwise from north, from -180 to 180.</param>
public readonly record struct GeodesicSolution(double Distance, double InitialAzimuth, double FinalAzimuth);
