// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Visibility;

/// <summary>Specifies the extent and resolution of a <see cref="Panorama"/>.</summary>
public sealed record PanoramaOptions
{
    /// <summary>Gets the width in pixels of the view. The default is 7,200, each spanning 0.05°.</summary>
    public int Width { get; init; } = 7200;

    /// <summary>
    /// Gets the angle in degrees the view spans across, from more than zero to a full circle. The default is 360°.
    /// </summary>
    public double HorizontalFieldOfView { get; init; } = 360;

    /// <summary>Gets the azimuth in degrees at the left edge of the view. The default is 0, due north.</summary>
    public double LeftEdgeAzimuth { get; init; }

    /// <summary>Gets the elevation angle in degrees at the top of the view. The default is 10°.</summary>
    public double TopAngle { get; init; } = 10;

    /// <summary>Gets the elevation angle in degrees at the bottom of the view. The default is -10°.</summary>
    public double BottomAngle { get; init; } = -10;

    /// <summary>Gets the distance in meters beyond which terrain is ignored. The default is 450 km.</summary>
    public double MaximumDistance { get; init; } = 450_000;
}
