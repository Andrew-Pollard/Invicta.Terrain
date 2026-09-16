// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework;

namespace Invicta.Geodesy;

/// <summary>Tests <see cref="GeodesicLine"/> arguments; <see cref="GeodesicTests"/> covers its accuracy.</summary>
internal sealed class GeodesicLineTests
{
    [TestCase(double.NaN)]
    [TestCase(double.NegativeInfinity)]
    public void Constructor_AzimuthNotFinite_Throws(double azimuth)
    {
        GeoCoordinate start = new(0, 0);

        Assert.That(() => new GeodesicLine(start, azimuth), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void GetPosition_DistanceNotFinite_Throws(double distance)
    {
        GeodesicLine line = new(new GeoCoordinate(0, 0), 45);

        Assert.That(() => line.GetPosition(distance), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Azimuth_OutsideHalfTurn_IsNormalized()
    {
        GeodesicLine line = new(new GeoCoordinate(0, 0), 370);

        Assert.That(line.Azimuth, Is.EqualTo(10).Within(1e-12));
    }
}
