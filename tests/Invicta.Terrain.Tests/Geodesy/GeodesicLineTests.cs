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
    public void GetPosition_AzimuthBeyondFullTurn_MatchesEquivalentAzimuth()
    {
        GeoCoordinate start = new(57, -5);

        GeodesicPosition beyond = new GeodesicLine(start, 370).GetPosition(100_000);
        GeodesicPosition equivalent = new GeodesicLine(start, 10).GetPosition(100_000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(beyond.Coordinate.Latitude, Is.EqualTo(equivalent.Coordinate.Latitude).Within(1e-12));
            Assert.That(beyond.Coordinate.Longitude, Is.EqualTo(equivalent.Coordinate.Longitude).Within(1e-12));
        }
    }
}
