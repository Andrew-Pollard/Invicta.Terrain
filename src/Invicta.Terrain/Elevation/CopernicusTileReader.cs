// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.InteropServices;

using BitMiracle.LibTiff.Classic;

namespace Invicta.Elevation;

/// <summary>Decodes Copernicus DEM tiles, which are tiled, compressed, 32-bit floating-point GeoTIFFs.</summary>
internal static class CopernicusTileReader
{
    private static readonly QuietErrorHandler s_errorHandler = new();

    /// <summary>Reads the heights at one overview level of a tile.</summary>
    /// <param name="path">The path of the GeoTIFF file.</param>
    /// <param name="tileLatitude">The latitude of the tile's southern edge, which sets its expected width.</param>
    /// <param name="overviewLevel">The overview level, where zero is full resolution.</param>
    /// <returns>The tile.</returns>
    /// <exception cref="InvalidDataException">The file is not a tile with the expected layout.</exception>
    public static ElevationTile Read(string path, int tileLatitude, int overviewLevel)
    {
        int expectedRows = CopernicusGrid.RowsPerDegree(overviewLevel);
        int expectedColumns = CopernicusGrid.ColumnsPerDegree(tileLatitude, overviewLevel);

        // LibTiff reports problems through a process-wide handler, which by default writes to the console; among them
        // are warnings for the GeoTIFF tags it does not know. Failures still surface through return values.
        Tiff.SetErrorHandler(s_errorHandler);

        using Tiff tiff = Tiff.Open(path, "r") ?? throw new InvalidDataException($"'{path}' is not a TIFF file.");
        if (!tiff.SetDirectory((short)overviewLevel))
        {
            throw new InvalidDataException($"'{path}' has no overview at level {overviewLevel}.");
        }

        int columns = GetInt(tiff, TiffTag.IMAGEWIDTH);
        int rows = GetInt(tiff, TiffTag.IMAGELENGTH);
        if (rows != expectedRows || columns != expectedColumns
            || GetInt(tiff, TiffTag.BITSPERSAMPLE) != 32
            || GetInt(tiff, TiffTag.SAMPLEFORMAT) != (int)SampleFormat.IEEEFP
            || !tiff.IsTiled())
        {
            throw new InvalidDataException(
                $"'{path}' at level {overviewLevel} is not a tiled {expectedColumns} × {expectedRows} float grid.");
        }

        float[] heights = new float[rows * columns];
        ReadBlocks(tiff, heights, rows, columns);

        return new ElevationTile(heights, columns);
    }

    private static void ReadBlocks(Tiff tiff, float[] heights, int rows, int columns)
    {
        int blockWidth = GetInt(tiff, TiffTag.TILEWIDTH);
        int blockHeight = GetInt(tiff, TiffTag.TILELENGTH);
        byte[] buffer = new byte[tiff.TileSize()];

        // LibTiff returns samples in the machine's byte order, so the bytes can be reinterpreted directly.
        ReadOnlySpan<float> block = MemoryMarshal.Cast<byte, float>(buffer.AsSpan());
        for (int blockTop = 0; blockTop < rows; blockTop += blockHeight)
        {
            for (int blockLeft = 0; blockLeft < columns; blockLeft += blockWidth)
            {
                if (tiff.ReadTile(buffer, 0, blockLeft, blockTop, 0, 0) < 0)
                {
                    throw new InvalidDataException(
                        $"The block at column {blockLeft}, row {blockTop} is corrupt: {QuietErrorHandler.LastError}");
                }

                CopyBlock(block, blockWidth, blockLeft, blockTop, heights, rows, columns);
            }
        }
    }

    /// <summary>Copies the part of a block that lies inside the image, since edge blocks are padded.</summary>
    private static void CopyBlock(
        ReadOnlySpan<float> block,
        int blockWidth,
        int blockLeft,
        int blockTop,
        Span<float> heights,
        int rows,
        int columns)
    {
        int width = int.Min(blockWidth, columns - blockLeft);
        int height = int.Min(block.Length / blockWidth, rows - blockTop);
        for (int row = 0; row < height; row++)
        {
            block.Slice(row * blockWidth, width).CopyTo(heights.Slice(((blockTop + row) * columns) + blockLeft, width));
        }
    }

    private static int GetInt(Tiff tiff, TiffTag tag)
    {
        FieldValue[]? value = tiff.GetField(tag);

        return value is null ? -1 : value[0].ToInt();
    }

    /// <summary>Discards LibTiff's warnings and remembers its last error message for exceptions.</summary>
    private sealed class QuietErrorHandler : TiffErrorHandler
    {
        /// <summary>Gets the last error LibTiff reported on this thread.</summary>
        [field: ThreadStatic]
        public static string? LastError { get; private set; }

        /// <inheritdoc/>
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        /// <inheritdoc/>
        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args)
        {
            // The format is printf-style, so show the arguments beside it rather than trying to substitute them.
            LastError = $"{method}: {format} ({string.Join(", ", args)})";
        }
    }
}
