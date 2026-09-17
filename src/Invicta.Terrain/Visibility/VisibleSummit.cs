// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Places;

namespace Invicta.Visibility;

/// <summary>Describes a summit that a panorama shows, and where.</summary>
/// <param name="Summit">The summit.</param>
/// <param name="Distance">The distance to the summit in meters along the surface.</param>
/// <param name="Azimuth">The azimuth of the summit in degrees clockwise from north, from 0 to 360.</param>
/// <param name="ElevationAngle">The summit's apparent angle above the horizontal in degrees.</param>
/// <param name="X">The summit's horizontal position in the panorama, in pixels.</param>
/// <param name="Y">The summit's vertical position in the panorama, in pixels.</param>
public sealed record VisibleSummit(
    Summit Summit, double Distance, double Azimuth, double ElevationAngle, double X, double Y);
