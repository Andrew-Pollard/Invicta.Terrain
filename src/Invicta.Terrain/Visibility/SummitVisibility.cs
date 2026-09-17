// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Places;

namespace Invicta.Visibility;

/// <summary>Determines which summits a panorama shows.</summary>
public static class SummitVisibility
{
    // Closer summits are the ground the viewer stands on, not something to label.
    private const double MinimumDistance = 200;

    // Mapped summit positions can be a few tens of meters off the terrain model's highest point, so search a
    // 3 × 3 grid of points this far apart around each.
    private const double SummitSearchSpacing = 30;

    /// <summary>Finds the summits that a panorama shows, as opposed to those hidden behind nearer terrain.</summary>
    /// <param name="panorama">The panorama.</param>
    /// <param name="terrain">The terrain the panorama was rendered from.</param>
    /// <param name="summits">The summits to consider.</param>
    /// <returns>The visible summits, in no particular order.</returns>
    /// <remarks>
    /// A summit counts as visible when the pixels where it appears show terrain at about its distance. This matches
    /// what the picture shows, which exact line-of-sight tracing would not always do at the picture's resolution.
    /// </remarks>
    public static IReadOnlyList<VisibleSummit> FindVisible(
        Panorama panorama, LayeredTerrain terrain, IEnumerable<Summit> summits)
    {
        ArgumentNullException.ThrowIfNull(panorama);

        ArgumentNullException.ThrowIfNull(terrain);

        ArgumentNullException.ThrowIfNull(summits);

        List<VisibleSummit> visible = [];
        foreach (Summit summit in summits)
        {
            if (TryProject(panorama, terrain, summit) is { } projected && ShowsTerrainAtDistance(panorama, projected))
            {
                visible.Add(projected);
            }
        }

        return visible;
    }

    /// <summary>Finds where a summit would appear in the panorama, if it were not hidden.</summary>
    private static VisibleSummit? TryProject(Panorama panorama, LayeredTerrain terrain, Summit summit)
    {
        Viewpoint viewpoint = panorama.Viewpoint;
        GeodesicSolution path = Geodesic.Inverse(viewpoint.Location, summit.Coordinate);
        if (path.Distance < MinimumDistance || path.Distance > panorama.MaximumDistance)
        {
            return null;
        }

        IElevationModel model = terrain.GetModel(path.Distance);
        double height = model.FindHighestPoint(summit.Coordinate, SummitSearchSpacing, SummitSearchSpacing).Height;
        double angle = viewpoint.ApparentElevationAngle(summit.Coordinate, height, path.Distance) * 180 / Math.PI;
        double azimuth = path.InitialAzimuth < 0 ? path.InitialAzimuth + 360 : path.InitialAzimuth;

        double x = panorama.ColumnAt(azimuth);
        double y = ((panorama.TopAngle - angle) / panorama.PixelAngle) - 0.5;
        if (x >= panorama.Width - 0.5 || y < 0 || y >= panorama.Height)
        {
            return null;
        }

        return new VisibleSummit(summit, path.Distance, azimuth, angle, x, y);
    }

    /// <summary>
    /// Determines whether any pixel at or just below a summit's position shows terrain at about the summit's distance,
    /// rather than nearer terrain in front of it.
    /// </summary>
    private static bool ShowsTerrainAtDistance(Panorama panorama, VisibleSummit summit)
    {
        double tolerance = Math.Max(250, 0.02 * summit.Distance);
        int summitRow = (int)Math.Round(summit.Y);

        foreach (int x in ColumnsAround(panorama, (int)Math.Round(summit.X)))
        {
            for (int y = summitRow; y <= Math.Min(summitRow + 2, panorama.Height - 1); y++)
            {
                if (Math.Abs(panorama.GetDistance(x, y) - summit.Distance) <= tolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Lists a column and its neighbors, wrapping around the edges of a full circle and clipping to those of a
    /// narrower view.
    /// </summary>
    private static IEnumerable<int> ColumnsAround(Panorama panorama, int center)
    {
        for (int column = center - 1; column <= center + 1; column++)
        {
            if (panorama.CoversFullCircle)
            {
                yield return ((column % panorama.Width) + panorama.Width) % panorama.Width;
            }
            else if (column >= 0 && column < panorama.Width)
            {
                yield return column;
            }
        }
    }
}
