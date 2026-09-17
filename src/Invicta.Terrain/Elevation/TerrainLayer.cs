// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Elevation;

/// <summary>Describes one layer of <see cref="LayeredTerrain"/>.</summary>
/// <param name="MaximumDistance">The distance in meters up to which this layer's model is used.</param>
/// <param name="Model">The elevation model.</param>
public readonly record struct TerrainLayer(double MaximumDistance, IElevationModel Model);
