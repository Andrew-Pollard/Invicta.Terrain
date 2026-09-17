// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Elevation;

/// <summary>
/// Computes how brightly the sun lights a slope, from the north-west and 45° up, as in the usual cartographic
/// hillshade.
/// </summary>
internal static class Hillshade
{
    private const double SunEast = -0.5;
    private const double SunNorth = 0.5;
    private static readonly double s_sunUp = Math.Sqrt(0.5);

    /// <summary>Gets the brightness of a slope.</summary>
    /// <param name="slopeEast">The rise in height per meter eastward.</param>
    /// <param name="slopeNorth">The rise in height per meter northward.</param>
    /// <returns>The cosine of the angle between the surface normal and the sun, or zero facing away.</returns>
    public static double Brightness(double slopeEast, double slopeNorth)
    {
        // The surface normal is (-slopeEast, -slopeNorth, 1), normalized.
        double towardSun = (-slopeEast * SunEast) - (slopeNorth * SunNorth) + s_sunUp;
        double length = Math.Sqrt((slopeEast * slopeEast) + (slopeNorth * slopeNorth) + 1);

        return Math.Max(0, towardSun / length);
    }
}
