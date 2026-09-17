// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Visibility;

/// <summary>Describes a point along a <see cref="SightLineProfile"/>.</summary>
/// <param name="Distance">The distance from the viewpoint in meters along the surface.</param>
/// <param name="TerrainHeight">The height of the terrain in meters above sea level.</param>
/// <param name="ApparentHeight">The terrain's apparent height in meters, as the profile describes.</param>
/// <param name="SeaLevelApparentHeight">The apparent height of sea level at the same distance, in meters.</param>
public readonly record struct ProfilePoint(
    double Distance, double TerrainHeight, double ApparentHeight, double SeaLevelApparentHeight);
