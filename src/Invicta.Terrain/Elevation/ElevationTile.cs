// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Elevation;

/// <summary>Holds the grid of heights for one tile of elevation data.</summary>
internal sealed class ElevationTile
{
    private readonly float[]? _heights;

    /// <summary>Initializes a new instance of the <see cref="ElevationTile"/> class.</summary>
    /// <param name="heights">The heights in meters, row by row from the north-west corner.</param>
    /// <param name="columns">The number of columns in each row.</param>
    public ElevationTile(float[] heights, int columns)
    {
        _heights = heights;
        Columns = columns;
    }

    private ElevationTile()
    {
    }

    /// <summary>Gets a tile for an area with no data because it is open sea, where every height is zero.</summary>
    public static ElevationTile Sea { get; } = new();

    /// <summary>Gets the number of columns in each row.</summary>
    public int Columns { get; }

    /// <summary>Gets the height at a row and column.</summary>
    /// <param name="row">The row, counting from the north.</param>
    /// <param name="column">The column, counting from the west.</param>
    /// <returns>The height in meters.</returns>
    public float GetHeight(int row, int column)
    {
        return _heights is null ? 0 : _heights[(row * Columns) + column];
    }
}
