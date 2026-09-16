// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Geodesy;

/// <summary>Defines the WGS 84 ellipsoid, which GPS coordinates and the elevation data refer to.</summary>
public static class Wgs84
{
    /// <summary>The equatorial radius in meters.</summary>
    public const double EquatorialRadius = 6_378_137;

    /// <summary>The flattening: the equatorial radius minus the polar radius, over the equatorial radius.</summary>
    public const double Flattening = 1 / 298.257223563;
}
