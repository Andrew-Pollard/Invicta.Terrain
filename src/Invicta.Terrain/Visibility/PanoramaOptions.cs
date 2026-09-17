// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Visibility;

/// <summary>Specifies the extent and resolution of a <see cref="Panorama"/>.</summary>
public sealed record PanoramaOptions
{
    /// <summary>Gets the width in pixels of the full 360° view. The default is 7,200, each spanning 0.05°.</summary>
    public int Width { get; init; } = 7200;

    /// <summary>Gets the elevation angle in degrees at the top of the view. The default is 10°.</summary>
    public double TopAngle { get; init; } = 10;

    /// <summary>Gets the elevation angle in degrees at the bottom of the view. The default is -10°.</summary>
    public double BottomAngle { get; init; } = -10;

    /// <summary>Gets the distance in meters beyond which terrain is ignored. The default is 450 km.</summary>
    public double MaximumDistance { get; init; } = 450_000;
}
