// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

using Invicta.Geodesy;

namespace Invicta.Elevation;

/// <summary>
/// Provides heights from the Copernicus GLO-30 DEM, interpolated bilinearly between samples, for a region loaded into
/// memory.
/// </summary>
/// <remarks>
/// <para>
/// Heights are those of the surface, including forests and buildings, above the EGM2008 geoid, which is within a meter
/// or so of mean sea level. Open sea has a height of zero.
/// </para>
/// <para>
/// At full resolution a tile takes 35 MB to 52 MB of memory, depending on its latitude. Each overview level halves the
/// resolution and quarters the memory, by averaging blocks of samples.
/// </para>
/// </remarks>
public sealed class CopernicusElevationModel : IElevationModel
{
    private const int TileSlotsPerLatitude = 360;

    // Enough margin to include the neighboring tiles that interpolation near the region's edges reaches into.
    private const double LoadMargin = 0.01;

    // Downloads and decoding run together; more at once would add little but load on the bucket.
    private const int MaxConcurrentLoads = 8;

    // One arc-second of latitude, to the nearest meter.
    private const double FullResolutionSpacing = 31;

    // Indexed by whole degrees of latitude and longitude, where null marks a tile that was not loaded.
    private readonly ElevationTile?[] _tiles = new ElevationTile?[180 * TileSlotsPerLatitude];

    private readonly int _rowsPerDegree;
    private readonly double _rowOffset;

    private CopernicusElevationModel(int overviewLevel)
    {
        OverviewLevel = overviewLevel;
        _rowsPerDegree = CopernicusGrid.RowsPerDegree(overviewLevel);
        _rowOffset = CopernicusGrid.SampleOffset(1.0 / CopernicusGrid.RowsPerDegree(0), overviewLevel);
    }

    /// <summary>Gets the overview level, where zero is full resolution and each level halves it.</summary>
    public int OverviewLevel { get; }

    /// <summary>Loads the tiles that cover a region, downloading any that are not yet in the store.</summary>
    /// <param name="store">The store to take tiles from.</param>
    /// <param name="region">The region.</param>
    /// <param name="overviewLevel">The overview level from 0, full resolution, to 3, an eighth of it.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The model, which provides heights anywhere in the region.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="overviewLevel"/> is not from 0 to 3.</exception>
    /// <exception cref="InvalidDataException">A tile is not in the expected format.</exception>
    public static async Task<CopernicusElevationModel> LoadAsync(
        CopernicusTileStore store, GeoBoundingBox region, int overviewLevel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        ArgumentOutOfRangeException.ThrowIfNegative(overviewLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(overviewLevel, CopernicusGrid.OverviewLevelCount);

        CopernicusElevationModel model = new(overviewLevel);
        ParallelOptions options = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = MaxConcurrentLoads,
        };

        await Parallel.ForEachAsync(TilesCovering(region), options, async (tile, token) =>
        {
            (int latitude, int longitude) = tile;
            string? path = await store.GetTilePathAsync(latitude, longitude, token).ConfigureAwait(false);
            model._tiles[TileSlot(latitude, longitude)] = path is null
                ? ElevationTile.Sea
                : CopernicusTileReader.Read(path, latitude, overviewLevel);
        }).ConfigureAwait(false);

        return model;
    }

    /// <summary>
    /// Loads the terrain around a center in layers, using each overview level from the distance at which its samples
    /// are no further apart than the angular resolution spans.
    /// </summary>
    /// <param name="store">The store to take tiles from.</param>
    /// <param name="center">The center, such as a viewpoint.</param>
    /// <param name="radius">The distance in meters to load terrain to.</param>
    /// <param name="angularResolution">The smallest angle in radians to resolve, such as a pixel.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The terrain.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="angularResolution"/> is not positive and finite.
    /// </exception>
    public static async Task<LayeredTerrain> LoadLayeredAsync(
        CopernicusTileStore store,
        GeoCoordinate center,
        double radius,
        double angularResolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        ThrowIfNotPositiveAndFinite(radius);

        ThrowIfNotPositiveAndFinite(angularResolution);

        List<TerrainLayer> layers = [];
        for (int level = 0; level <= CopernicusGrid.OverviewLevelCount; level++)
        {
            double nextLevelSpacing = FullResolutionSpacing * (2 << level);
            double reach = level == CopernicusGrid.OverviewLevelCount
                ? radius
                : Math.Min(radius, nextLevelSpacing / angularResolution);

            GeoBoundingBox region = GeoBoundingBox.Around(center, reach);
            CopernicusElevationModel model =
                await LoadAsync(store, region, level, cancellationToken).ConfigureAwait(false);
            layers.Add(new TerrainLayer(reach, model));
            if (reach >= radius)
            {
                break;
            }
        }

        return new LayeredTerrain(layers);
    }

    private static void ThrowIfNotPositiveAndFinite(
        double value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!(value > 0) || double.IsPositiveInfinity(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be positive and finite.");
        }
    }

    private static IEnumerable<(int Latitude, int Longitude)> TilesCovering(GeoBoundingBox region)
    {
        int south = Math.Max(-90, (int)Math.Floor(region.South - LoadMargin));
        int north = Math.Min(89, (int)Math.Floor(region.North + LoadMargin));
        int west = (int)Math.Floor(region.West - LoadMargin);
        int east = (int)Math.Floor(region.East + LoadMargin);

        // A region wider than the world would otherwise list some tiles twice.
        east = Math.Min(east, west + TileSlotsPerLatitude - 1);

        for (int latitude = south; latitude <= north; latitude++)
        {
            for (int longitude = west; longitude <= east; longitude++)
            {
                yield return (latitude, NormalizeTileLongitude(longitude));
            }
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The point is outside the region that was loaded.</exception>
    public double GetElevation(GeoCoordinate coordinate)
    {
        // Rows lie at whole multiples of the row spacing, less the offset, counting north from the equator.
        double rowPosition = (coordinate.Latitude + _rowOffset) * _rowsPerDegree;
        double southRow = Math.Floor(rowPosition);
        double northWeight = rowPosition - southRow;

        double south = InterpolateAlongRow((long)southRow, coordinate.Longitude);
        double north = InterpolateAlongRow((long)southRow + 1, coordinate.Longitude);

        return south + ((north - south) * northWeight);
    }

    private double InterpolateAlongRow(long row, double longitude)
    {
        // A row belongs to the tile below it, except that a tile's first row lies on its northern edge.
        long tileLatitude = CeilingDivide(row, _rowsPerDegree) - 1;
        if (tileLatitude is < -90 or >= 90)
        {
            return 0;
        }

        int rowInTile = (int)(((tileLatitude + 1) * _rowsPerDegree) - row);
        int columnsPerDegree = CopernicusGrid.ColumnsPerDegree((int)tileLatitude, OverviewLevel);
        double fullResolutionSpacing = 1.0 / CopernicusGrid.ColumnsPerDegree((int)tileLatitude, 0);
        double columnOffset = CopernicusGrid.SampleOffset(fullResolutionSpacing, OverviewLevel);

        double columnPosition = (longitude - columnOffset) * columnsPerDegree;
        double westColumn = Math.Floor(columnPosition);
        double eastWeight = columnPosition - westColumn;

        double west = GetSample((int)tileLatitude, rowInTile, (long)westColumn, columnsPerDegree);
        double east = GetSample((int)tileLatitude, rowInTile, (long)westColumn + 1, columnsPerDegree);

        return west + ((east - west) * eastWeight);
    }

    private float GetSample(int tileLatitude, int rowInTile, long column, int columnsPerDegree)
    {
        long tileLongitude = Math.DivRem(column, columnsPerDegree, out long columnInTile);
        if (columnInTile < 0)
        {
            tileLongitude--;
            columnInTile += columnsPerDegree;
        }

        ElevationTile tile = _tiles[TileSlot(tileLatitude, NormalizeTileLongitude((int)tileLongitude))]
            ?? throw new InvalidOperationException("The point is outside the region that was loaded.");

        return tile.GetHeight(rowInTile, (int)columnInTile);
    }

    private static int TileSlot(int latitude, int longitude)
    {
        return ((latitude + 90) * TileSlotsPerLatitude) + longitude + 180;
    }

    private static int NormalizeTileLongitude(int longitude)
    {
        return ((((longitude + 180) % TileSlotsPerLatitude) + TileSlotsPerLatitude) % TileSlotsPerLatitude) - 180;
    }

    private static long CeilingDivide(long dividend, long divisor)
    {
        long quotient = Math.DivRem(dividend, divisor, out long remainder);

        return remainder > 0 ? quotient + 1 : quotient;
    }
}
