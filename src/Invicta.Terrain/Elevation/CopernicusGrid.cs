// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;

namespace Invicta.Elevation;

/// <summary>Describes the layout of the Copernicus GLO-30 DEM: its tiles, their grids and their overviews.</summary>
/// <remarks>
/// Each tile covers one degree, named after its south-west corner. Rows are one arc-second apart, and the first row
/// lies on the tile's northern edge. Columns are one arc-second apart below 50° of latitude and further apart toward
/// the poles, and the first column lies on the western edge. Each overview halves the resolution by averaging 2 × 2
/// blocks, so its samples sit at the centers of those blocks.
/// </remarks>
internal static class CopernicusGrid
{
    /// <summary>The number of overviews below the full resolution in each tile.</summary>
    public const int OverviewLevelCount = 3;

    private const int FullResolutionRowsPerDegree = 3600;

    /// <summary>Gets the number of rows in one degree of latitude at an overview level.</summary>
    public static int RowsPerDegree(int overviewLevel)
    {
        return FullResolutionRowsPerDegree >> overviewLevel;
    }

    /// <summary>Gets the number of columns in one degree of longitude for tiles at a latitude.</summary>
    /// <param name="tileLatitude">The latitude of the tile's southern edge.</param>
    /// <param name="overviewLevel">The overview level, where zero is full resolution.</param>
    public static int ColumnsPerDegree(int tileLatitude, int overviewLevel)
    {
        int equatorwardEdge = tileLatitude >= 0 ? tileLatitude : -(tileLatitude + 1);
        int fullResolutionColumns = equatorwardEdge switch
        {
            < 50 => 3600,
            < 60 => 2400,
            < 70 => 1800,
            < 80 => 1200,
            < 85 => 720,
            _ => 360,
        };

        return fullResolutionColumns >> overviewLevel;
    }

    /// <summary>
    /// Gets how far the first sample lies inside a tile's edge, in degrees, given the spacing of samples at full
    /// resolution.
    /// </summary>
    public static double SampleOffset(double fullResolutionSpacing, int overviewLevel)
    {
        return ((1 << overviewLevel) - 1) * fullResolutionSpacing / 2;
    }

    /// <summary>Gets the name of the tile whose south-west corner is at a latitude and longitude.</summary>
    public static string TileName(int latitude, int longitude)
    {
        char hemisphere = latitude < 0 ? 'S' : 'N';
        char side = longitude < 0 ? 'W' : 'E';

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Copernicus_DSM_COG_10_{hemisphere}{double.Abs(latitude):00}_00_{side}{double.Abs(longitude):000}_00_DEM");
    }
}
