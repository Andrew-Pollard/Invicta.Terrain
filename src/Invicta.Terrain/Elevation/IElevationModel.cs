// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

namespace Invicta.Elevation;

/// <summary>Provides the height of the Earth's surface at any point in a region.</summary>
public interface IElevationModel
{
    /// <summary>Gets the height of the surface at a point.</summary>
    /// <param name="coordinate">The point.</param>
    /// <returns>The height in meters above mean sea level.</returns>
    public double GetElevation(GeoCoordinate coordinate);
}
