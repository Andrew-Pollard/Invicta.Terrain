// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

namespace Invicta;

/// <summary>
/// Joins a route's waypoints into one path, giving the position at any distance along it. Distances beyond either end
/// carry on along the first or last leg, so that a camera can look ahead past the end of the path.
/// </summary>
internal sealed class RoutePath
{
    private readonly GeodesicLine[] _legs;

    // Where each leg starts, as a distance along the whole path.
    private readonly double[] _legStarts;

    /// <summary>Initializes a new instance of the <see cref="RoutePath"/> class.</summary>
    /// <param name="waypoints">The points the path passes through, in order.</param>
    /// <exception cref="ArgumentException"><paramref name="waypoints"/> holds fewer than two points.</exception>
    public RoutePath(IReadOnlyList<GeoCoordinate> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);

        if (waypoints.Count < 2)
        {
            throw new ArgumentException("A path needs at least two waypoints.", nameof(waypoints));
        }

        _legs = new GeodesicLine[waypoints.Count - 1];
        _legStarts = new double[waypoints.Count - 1];

        double start = 0;
        for (int leg = 0; leg < _legs.Length; leg++)
        {
            GeodesicSolution step = Geodesic.Inverse(waypoints[leg], waypoints[leg + 1]);
            _legs[leg] = new GeodesicLine(waypoints[leg], step.InitialAzimuth);
            _legStarts[leg] = start;
            start += step.Distance;
        }

        Distance = start;
    }

    /// <summary>Gets the length of the path in meters.</summary>
    public double Distance { get; }

    /// <summary>Gets the position at a distance along the path.</summary>
    /// <param name="along">The distance in meters from the start.</param>
    /// <returns>The position.</returns>
    public GeoCoordinate GetPosition(double along)
    {
        int leg = Array.BinarySearch(_legStarts, along);
        if (leg < 0)
        {
            leg = ~leg - 1;
        }

        leg = Math.Clamp(leg, 0, _legs.Length - 1);

        return _legs[leg].GetPosition(along - _legStarts[leg]);
    }
}
