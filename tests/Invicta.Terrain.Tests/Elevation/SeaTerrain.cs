// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

namespace Invicta.Elevation;

/// <summary>Represents a smooth sea at sea level everywhere, whose views follow from simple geometry.</summary>
internal sealed class SeaTerrain : IElevationModel
{
    /// <inheritdoc/>
    public double GetElevation(GeoCoordinate coordinate)
    {
        return 0;
    }
}
