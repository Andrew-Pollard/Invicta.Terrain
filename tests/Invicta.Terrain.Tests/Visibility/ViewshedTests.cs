// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>Tests <see cref="Viewshed"/> on terrain whose visible ground follows from simple geometry.</summary>
internal sealed class ViewshedTests
{
    private static readonly GeoCoordinate s_origin = new(57, -5);

    [TestCase(0.0)]
    [TestCase(90.0)]
    [TestCase(225.0)]
    public void Compute_OverSea_SeesSeaOnlyToTheHorizon(double azimuth)
    {
        // From 200 m with standard refraction, the horizon is sqrt(2Rh / (1 - k)), about 54 km.
        const double EyeHeight = 200;
        double horizon = Math.Sqrt(2 * 6_371_000 * EyeHeight / (1 - Viewpoint.StandardRefractionCoefficient));
        Viewpoint viewpoint = new(s_origin, EyeHeight);

        Viewshed viewshed = Viewshed.Compute(
            LayeredTerrain.FromModel(new SeaTerrain(), 80_000), viewpoint, 80_000, 500, 0, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewshed.IsVisible(0.95 * horizon, azimuth), Is.True);
            Assert.That(viewshed.IsVisible(1.05 * horizon, azimuth), Is.False);
            Assert.That(viewshed.IsVisible(81_000, azimuth), Is.False, "Beyond the radius");
        }
    }

    [TestCase(0, 4_000, 0, true)]
    [TestCase(0, 4_960, 0, true)]
    [TestCase(0, 5_040, 0, false)]
    [TestCase(0, 10_000, 0, false)]
    [TestCase(180, 10_000, 0, true)]
    [TestCase(0, 10_000, 250, true)]
    [TestCase(0, 10_000, 120, false)]
    public void Compute_BehindWall_HidesWhatIsBelowTheLineOverTheWall(
        double azimuth, double distance, double targetHeight, bool expected)
    {
        // The eye is 50 m up, so without refraction the horizon is 25 km away. A 100 m wall 5 km north shows its front
        // edge but hides its own flat top. Over the wall the line of sight rises at about 0.0096 rad after curvature,
        // so 10 km north only a target at least 154 m tall is seen. Ground 10 km south, the other way, is in view.
        Viewpoint viewpoint = new(s_origin, 50, refractionCoefficient: 0);
        LayeredTerrain terrain = LayeredTerrain.FromModel(new WallTerrain(5_000), 20_000);

        Viewshed viewshed = Viewshed.Compute(terrain, viewpoint, 20_000, 20, targetHeight, CancellationToken.None);

        Assert.That(viewshed.IsVisible(distance, azimuth), Is.EqualTo(expected));
    }

    /// <summary>Represents flat ground at sea level with a wall 100 m high and thick, running east to west.</summary>
    private sealed class WallTerrain(double distanceNorth) : IElevationModel
    {
        /// <inheritdoc/>
        public double GetElevation(GeoCoordinate coordinate)
        {
            double north = (coordinate.Latitude - s_origin.Latitude) * 111_320;

            return Math.Abs(north - distanceNorth) <= 50 ? 100 : 0;
        }
    }
}
