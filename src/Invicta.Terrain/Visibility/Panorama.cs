// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

namespace Invicta.Visibility;

/// <summary>
/// Represents the view from a viewpoint as a grid of pixels, each recording the terrain it shows: its distance, height
/// and shading.
/// </summary>
/// <remarks>
/// The view spans a full circle unless a narrower field of view is asked for, such as the view ahead of a moving
/// camera. Columns run clockwise from the azimuth at the left edge, and every pixel spans the same angle across and up,
/// so the image is an equirectangular projection of the view.
/// </remarks>
public sealed class Panorama
{
    // Steps closer than this gain nothing, since the terrain has a sample every 30 m.
    private const double MinimumStep = 15;

    private readonly float[] _distances;
    private readonly float[] _heights;
    private readonly float[] _shading;

    private Panorama(Viewpoint viewpoint, PanoramaOptions options)
    {
        Viewpoint = viewpoint;
        Width = options.Width;
        HorizontalFieldOfView = options.HorizontalFieldOfView;
        LeftEdgeAzimuth = options.LeftEdgeAzimuth;
        PixelAngle = options.PixelAngle;
        TopAngle = options.TopAngle;
        Height = (int)Math.Ceiling((options.TopAngle - options.BottomAngle) / PixelAngle);
        MaximumDistance = options.MaximumDistance;

        _distances = new float[Width * Height];
        _heights = new float[Width * Height];
        _shading = new float[Width * Height];
        Array.Fill(_distances, float.NaN);
    }

    /// <summary>Gets the viewpoint.</summary>
    public Viewpoint Viewpoint { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the angle in degrees the view spans across.</summary>
    public double HorizontalFieldOfView { get; }

    /// <summary>Gets the azimuth in degrees at the left edge of the view.</summary>
    public double LeftEdgeAzimuth { get; }

    /// <summary>Gets a value indicating whether the view spans a full circle, so that its edges meet.</summary>
    internal bool CoversFullCircle => HorizontalFieldOfView >= 360;

    /// <summary>Gets the angle in degrees that each pixel spans, across and up.</summary>
    public double PixelAngle { get; }

    /// <summary>Gets the elevation angle in degrees at the top edge.</summary>
    public double TopAngle { get; }

    /// <summary>Gets the distance in meters beyond which terrain was ignored.</summary>
    public double MaximumDistance { get; }

    /// <summary>Renders the view from a viewpoint.</summary>
    /// <param name="terrain">The terrain, reaching the maximum distance in <paramref name="options"/>.</param>
    /// <param name="viewpoint">The viewpoint.</param>
    /// <param name="options">The extent and resolution of the view.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The panorama.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    public static Panorama Render(
        LayeredTerrain terrain, Viewpoint viewpoint, PanoramaOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        ArgumentNullException.ThrowIfNull(viewpoint);

        ArgumentNullException.ThrowIfNull(options);
        ThrowIfInvalid(options);

        Panorama panorama = new(viewpoint, options);
        ParallelOptions parallelOptions = new() { CancellationToken = cancellationToken };
        Parallel.For(0, panorama.Width, parallelOptions, x => panorama.RenderColumn(terrain, x));

        return panorama;
    }

    private static void ThrowIfInvalid(PanoramaOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Width, 4, nameof(options));

        if (options.HorizontalFieldOfView is not (> 0 and <= 360))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "The field of view must be more than 0° and at most 360°.");
        }

        ArgumentChecks.ThrowIfNotFinite(options.LeftEdgeAzimuth);

        bool topInRange = options.TopAngle is > -90 and <= 90;
        bool bottomInRange = options.BottomAngle >= -90 && options.BottomAngle < options.TopAngle;
        if (!topInRange || !bottomInRange)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "The angles must be from -90° to 90°, with the top above the bottom.");
        }

        ArgumentChecks.ThrowIfNotPositiveAndFinite(options.MaximumDistance);
    }

    /// <summary>
    /// Marches outward along a column's azimuth, filling pixels from the bottom up as the terrain rises into view.
    /// </summary>
    private void RenderColumn(LayeredTerrain terrain, int x)
    {
        double pixelRadians = PixelAngle * Math.PI / 180;
        double topRadians = TopAngle * Math.PI / 180;
        TerrainRay ray = new(Viewpoint, terrain, AzimuthAt(x));

        // Rows from firstFilledRow down are filled. Pixel y shows terrain once the terrain's apparent angle reaches its
        // center, which is at row coordinate (top - angle) / pixel - 1/2.
        int firstFilledRow = Height;
        double previousRow = double.PositiveInfinity;
        double previousDistance = 0;
        double step;
        for (double distance = MinimumStep; distance <= MaximumDistance; distance += step)
        {
            // Step further as the terrain recedes, so that each step spans about a pixel.
            step = Math.Max(MinimumStep, distance * pixelRadians);

            TerrainSample sample = ray.Sample(distance);
            double row = ((topRadians - sample.ElevationAngle) / pixelRadians) - 0.5;
            int newFirstFilledRow = Math.Max(0, (int)Math.Ceiling(row));
            if (newFirstFilledRow < firstFilledRow)
            {
                float shading = (float)Shade(terrain.GetModel(distance), sample, step);
                for (int y = newFirstFilledRow; y < firstFilledRow; y++)
                {
                    // Interpolate the distance between this sample and the previous one by row, so that slopes rising
                    // into view get smoothly varying distances rather than steps.
                    double fraction = previousRow > row ? Math.Clamp((y - row) / (previousRow - row), 0, 1) : 0;
                    int index = Index(x, y);
                    _distances[index] = (float)(distance + (fraction * (previousDistance - distance)));
                    _heights[index] = (float)sample.Height;
                    _shading[index] = shading;
                }

                firstFilledRow = newFirstFilledRow;
                if (firstFilledRow == 0)
                {
                    break;
                }
            }

            previousRow = row;
            previousDistance = distance;
        }
    }

    /// <summary>Computes the hillshade at a sample from the terrain's slope, measured over a given distance.</summary>
    private static double Shade(IElevationModel model, TerrainSample sample, double baseline)
    {
        GeoCoordinate center = sample.Coordinate;
        double east = model.GetElevation(center.Offset(0, baseline));
        double west = model.GetElevation(center.Offset(0, -baseline));
        double north = model.GetElevation(center.Offset(baseline, 0));
        double south = model.GetElevation(center.Offset(-baseline, 0));

        double slopeEast = (east - west) / (2 * baseline);
        double slopeNorth = (north - south) / (2 * baseline);

        return Hillshade.Brightness(slopeEast, slopeNorth);
    }

    /// <summary>Gets the azimuth in degrees at the center of a column.</summary>
    /// <param name="x">The column.</param>
    /// <returns>The azimuth, clockwise from north, which a narrow view can carry beyond 360°.</returns>
    public double AzimuthAt(double x)
    {
        return LeftEdgeAzimuth + ((x + 0.5) * PixelAngle);
    }

    /// <summary>Gets the column that shows an azimuth.</summary>
    /// <param name="azimuth">The azimuth in degrees, clockwise from north.</param>
    /// <returns>
    /// The column, which is from -0.5 to the width less 0.5 when the azimuth is in view, and beyond that range when it
    /// is behind the view.
    /// </returns>
    public double ColumnAt(double azimuth)
    {
        double fromLeftEdge = Math.IEEERemainder(azimuth - LeftEdgeAzimuth, 360);
        if (fromLeftEdge < 0)
        {
            fromLeftEdge += 360;
        }

        return (fromLeftEdge / PixelAngle) - 0.5;
    }

    /// <summary>Gets the elevation angle in degrees at the center of a row.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The elevation angle.</returns>
    public double ElevationAngleAt(double y)
    {
        return TopAngle - ((y + 0.5) * PixelAngle);
    }

    /// <summary>Gets the row that shows an elevation angle.</summary>
    /// <param name="elevationAngle">The angle in degrees above the horizontal, negative below it.</param>
    /// <returns>
    /// The row, which is from -0.5 to the height less 0.5 when the angle is in view, and outside that range when it
    /// is above or below the view.
    /// </returns>
    public double RowAt(double elevationAngle)
    {
        return ((TopAngle - elevationAngle) / PixelAngle) - 0.5;
    }

    /// <summary>Gets the distance to the terrain a pixel shows.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The distance in meters, or <see cref="double.NaN"/> if the pixel shows sky.</returns>
    public double GetDistance(int x, int y)
    {
        return _distances[Index(x, y)];
    }

    /// <summary>Gets the height of the terrain a pixel shows.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The height in meters above sea level, or zero if the pixel shows sky.</returns>
    public double GetTerrainHeight(int x, int y)
    {
        return _heights[Index(x, y)];
    }

    /// <summary>Gets how brightly sunlight from the north-west lights the terrain a pixel shows.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The brightness from 0, facing away from the sun, to 1, facing it.</returns>
    public double GetShading(int x, int y)
    {
        return _shading[Index(x, y)];
    }

    private int Index(int x, int y)
    {
        return (y * Width) + x;
    }
}
