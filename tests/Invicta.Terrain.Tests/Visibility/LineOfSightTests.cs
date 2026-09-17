// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>Tests <see cref="LineOfSight"/> over synthetic terrain whose answers follow from simple geometry.</summary>
internal sealed class LineOfSightTests
{
    private static readonly GeoCoordinate s_origin = new(57, -5);

    [TestCase(0.0)]
    [TestCase(0.13)]
    public void Trace_SeaLevelTargetAroundHorizon_IsVisibleOnlyBeforeIt(double refractionCoefficient)
    {
        // Over a smooth sea the horizon is at sqrt(2Rh / (1 - k)), where refraction stretches the effective radius.
        const double EyeHeight = 1000;
        Viewpoint viewpoint = new(s_origin, EyeHeight, refractionCoefficient);
        double horizon = Math.Sqrt(2 * 6_371_000 * EyeHeight / (1 - refractionCoefficient));

        LineOfSightResult near = TraceOverSea(viewpoint, 0.97 * horizon, 0);
        LineOfSightResult far = TraceOverSea(viewpoint, 1.03 * horizon, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(near.IsVisible, Is.True);
            Assert.That(far.IsVisible, Is.False);
        }
    }

    [TestCase(210_000, true)]
    [TestCase(240_000, false)]
    public void Trace_MountainBeyondSeaHorizon_IsVisibleWhileTheirHorizonsOverlap(double distance, bool expected)
    {
        // Without refraction, two points 1,000 m above the sea can see each other up to 2 sqrt(2Rh), about 226 km.
        Viewpoint viewpoint = new(s_origin, 1000, refractionCoefficient: 0);

        LineOfSightResult result = TraceOverSea(viewpoint, distance, 1000);

        Assert.That(result.IsVisible, Is.EqualTo(expected));
    }

    [TestCase(700, false)]
    [TestCase(800, true)]
    public void Trace_TargetBehindRidge_IsVisibleOnlyAboveTheRidgeLine(double targetHeight, bool expected)
    {
        // The eye is 100 m up and a 300 m ridge stands 10 km away. Extending the sight line over the ridge to 30 km,
        // and adding the 70 m the Earth's curvature drops away there, a target must be about 747 m high to be seen.
        Viewpoint viewpoint = new(s_origin, 100, refractionCoefficient: 0);
        RidgeTerrain terrain = new(s_origin, ridgeDistance: 10_000, ridgeHeight: 300);
        GeoCoordinate target = new GeodesicLine(s_origin, 60).GetPosition(30_000);

        LineOfSightResult result = LineOfSight.Trace(terrain, viewpoint, target, targetHeight, 15);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsVisible, Is.EqualTo(expected));
            Assert.That(result.Obstruction?.Distance, Is.EqualTo(10_000).Within(50));
        }
    }

    [TestCase(0, false)]
    [TestCase(2, true)]
    public void Trace_TargetOnFlatSummitFarAway_IsHiddenBySummitUnlessRaised(double heightAboveSummit, bool expected)
    {
        // A 1,000 m hill 40 km away has a flat top 300 m across. From an eye at the same height, the Earth's curvature
        // makes the near edge of the top appear higher than its center, which hides a target on the ground there but
        // not a person standing there.
        Viewpoint viewpoint = new(s_origin, 1002);
        GeoCoordinate summit = new GeodesicLine(s_origin, 45).GetPosition(40_000);
        FlatTopTerrain terrain = new(summit, height: 1000, radius: 150);

        LineOfSightResult result = LineOfSight.Trace(terrain, viewpoint, summit, 1000 + heightAboveSummit, 15);

        Assert.That(result.IsVisible, Is.EqualTo(expected));
    }

    [Test]
    public void Trace_SampleSpacingNotPositive_Throws()
    {
        Viewpoint viewpoint = new(s_origin, 100);

        Assert.That(
            () => LineOfSight.Trace(new SeaTerrain(), viewpoint, new GeoCoordinate(57.1, -5), 0, 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static LineOfSightResult TraceOverSea(Viewpoint viewpoint, double distance, double targetHeight)
    {
        GeoCoordinate target = new GeodesicLine(viewpoint.Location, 135).GetPosition(distance);

        return LineOfSight.Trace(new SeaTerrain(), viewpoint, target, targetHeight, 100);
    }

    /// <summary>Represents flat ground at sea level with a circular ridge 100 m wide around a center.</summary>
    private sealed class RidgeTerrain(GeoCoordinate center, double ridgeDistance, double ridgeHeight) : IElevationModel
    {
        /// <inheritdoc/>
        public double GetElevation(GeoCoordinate coordinate)
        {
            double distance = Geodesic.Inverse(center, coordinate).Distance;

            return Math.Abs(distance - ridgeDistance) <= 50 ? ridgeHeight : 0;
        }
    }

    /// <summary>Represents flat ground at sea level with a steep-sided hill whose top is flat.</summary>
    private sealed class FlatTopTerrain(GeoCoordinate center, double height, double radius) : IElevationModel
    {
        /// <inheritdoc/>
        public double GetElevation(GeoCoordinate coordinate)
        {
            return Geodesic.Inverse(center, coordinate).Distance <= radius ? height : 0;
        }
    }
}
