// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>Tests <see cref="SightLineProfile"/>.</summary>
internal sealed class SightLineProfileTests
{
    private const double EyeHeight = 500;
    private const double RefractionCoefficient = 0.13;

    private static readonly GeoCoordinate s_origin = new(57, -5);

    [Test]
    public void Trace_OverSea_ApparentHeightsFallWithCurvatureLessRefraction()
    {
        Viewpoint viewpoint = new(s_origin, EyeHeight, RefractionCoefficient);
        GeoCoordinate target = new GeodesicLine(s_origin, 45).GetPosition(60_000);

        SightLineProfile profile = SightLineProfile.Trace(new SeaTerrain(), viewpoint, target, 0, 100, 61);

        double worstError = 0;
        foreach (ProfilePoint point in profile.Points)
        {
            // Sea level, lowered by the curvature that refraction does not make up.
            double expected = -(1 - RefractionCoefficient) * point.Distance * point.Distance / (2 * 6_371_000);
            worstError = double.Max(worstError, double.Abs(point.ApparentHeight - expected));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Points, Has.Count.EqualTo(61));
            Assert.That(
                profile.Points.Select(point => point.ApparentHeight),
                Is.EqualTo(profile.Points.Select(point => point.SeaLevelApparentHeight)));

            // The parabola approximates the ellipsoid's curve; at 60 km the difference is well under a meter.
            Assert.That(worstError, Is.LessThan(1));
        }
    }

    [TestCase(200, false)]
    [TestCase(900, true)]
    public void Trace_BehindHill_TerrainRisesAboveTheLineOnlyWhenHidden(double targetHeight, bool expectVisible)
    {
        Viewpoint viewpoint = new(s_origin, 2, RefractionCoefficient);
        HillTerrain terrain = new(new GeodesicLine(s_origin, 0).GetPosition(5_000));
        GeoCoordinate target = new GeodesicLine(s_origin, 0).GetPosition(20_000);

        SightLineProfile profile = SightLineProfile.Trace(terrain, viewpoint, target, targetHeight, 15, 2001);

        double highestAboveLine = profile.Points
            .Where(point => point.Distance < profile.Result.Distance)
            .Max(point => point.ApparentHeight - LineHeight(profile, point.Distance));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Result.IsVisible, Is.EqualTo(expectVisible));
            Assert.That(highestAboveLine > 0, Is.EqualTo(!expectVisible));
        }
    }

    private static double LineHeight(SightLineProfile profile, double distance)
    {
        double rise = profile.TargetApparentHeight - profile.EyeHeight;

        return profile.EyeHeight + (rise * distance / profile.Result.Distance);
    }

    /// <summary>Represents flat ground with a round hill 200 m high and 1 km across.</summary>
    private sealed class HillTerrain(GeoCoordinate top) : IElevationModel
    {
        /// <inheritdoc/>
        public double GetElevation(GeoCoordinate coordinate)
        {
            double distance = Geodesic.Inverse(top, coordinate).Distance;

            return double.Max(0, 200 * (1 - (distance / 500)));
        }
    }
}
