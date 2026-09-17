// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

namespace Invicta.Visibility;

/// <summary>
/// Represents the full 360° view from a viewpoint as a grid of pixels, each recording the terrain it shows: its
/// distance, height and shading.
/// </summary>
/// <remarks>
/// Columns run clockwise from north at the left edge, and every pixel spans the same angle across and up, so the
/// image is an equirectangular projection of the view.
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
        PixelAngle = 360.0 / options.Width;
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
        const double MetersPerDegreeOfLatitude = 111_320;

        GeoCoordinate center = sample.Coordinate;
        double latitudeStep = baseline / MetersPerDegreeOfLatitude;
        double longitudeStep = latitudeStep / Math.Max(0.01, Math.Cos(center.Latitude * Math.PI / 180));

        double northLatitude = Math.Min(90, center.Latitude + latitudeStep);
        double southLatitude = Math.Max(-90, center.Latitude - latitudeStep);

        double east = model.GetElevation(new GeoCoordinate(center.Latitude, center.Longitude + longitudeStep));
        double west = model.GetElevation(new GeoCoordinate(center.Latitude, center.Longitude - longitudeStep));
        double north = model.GetElevation(new GeoCoordinate(northLatitude, center.Longitude));
        double south = model.GetElevation(new GeoCoordinate(southLatitude, center.Longitude));

        double slopeEast = (east - west) / (2 * baseline);
        double slopeNorth = (north - south) / (2 * baseline);

        return Hillshade.Brightness(slopeEast, slopeNorth);
    }

    /// <summary>Gets the azimuth in degrees at the center of a column.</summary>
    /// <param name="x">The column.</param>
    /// <returns>The azimuth, clockwise from north.</returns>
    public double AzimuthAt(double x)
    {
        return (x + 0.5) * PixelAngle;
    }

    /// <summary>Gets the elevation angle in degrees at the center of a row.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The elevation angle.</returns>
    public double ElevationAngleAt(double y)
    {
        return TopAngle - ((y + 0.5) * PixelAngle);
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
