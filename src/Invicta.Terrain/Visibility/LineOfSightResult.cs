// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Visibility;

/// <summary>Describes a line of sight from a viewpoint to a target.</summary>
/// <param name="Distance">The distance to the target in meters along the surface.</param>
/// <param name="Azimuth">The azimuth of the target in degrees clockwise from north.</param>
/// <param name="TargetElevationAngle">The target's apparent angle above the horizontal in radians.</param>
/// <param name="Obstruction">
/// The terrain sample with the highest apparent angle before the target, or <see langword="null"/> if the target is
/// too close for any samples.
/// </param>
public readonly record struct LineOfSightResult(
    double Distance, double Azimuth, double TargetElevationAngle, TerrainSample? Obstruction)
{
    /// <summary>
    /// Gets the angle in radians by which the target clears the highest obstruction, negative if the target is hidden.
    /// </summary>
    public double Clearance => Obstruction is { } obstruction
        ? TargetElevationAngle - obstruction.ElevationAngle
        : double.PositiveInfinity;

    /// <summary>Gets a value indicating whether the target can be seen.</summary>
    public bool IsVisible => Clearance > 0;
}
