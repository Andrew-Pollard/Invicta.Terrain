// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

namespace Invicta.Places;

/// <summary>Describes a named summit.</summary>
/// <param name="Name">The summit's name.</param>
/// <param name="Coordinate">The summit's position.</param>
/// <param name="Elevation">
/// The summit's surveyed height in meters above sea level, or <see langword="null"/> if it is not recorded.
/// </param>
/// <param name="Prominence">
/// How far in meters the summit rises above the lowest contour enclosing it and no higher summit, or
/// <see langword="null"/> if it is not recorded.
/// </param>
public sealed record Summit(string Name, GeoCoordinate Coordinate, double? Elevation, double? Prominence);
