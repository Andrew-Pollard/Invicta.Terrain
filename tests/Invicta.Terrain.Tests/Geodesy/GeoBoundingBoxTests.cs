// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework;

namespace Invicta.Geodesy;

/// <summary>Tests <see cref="GeoBoundingBox"/>.</summary>
internal sealed class GeoBoundingBoxTests
{
    [TestCase(56.8, -5.0, 450_000)]
    [TestCase(-33.9, 151.2, 1_000_000)]
    [TestCase(0, 179.5, 200_000)]
    public void Around_PointsOnCircle_TouchEachEdge(double latitude, double longitude, double radius)
    {
        GeoCoordinate center = new(latitude, longitude);
        GeoBoundingBox box = GeoBoundingBox.Around(center, radius);

        double south = 90;
        double north = -90;
        double west = double.PositiveInfinity;
        double east = double.NegativeInfinity;
        for (int i = 0; i < 36_000; i++)
        {
            GeoCoordinate point = new GeodesicLine(center, i / 100.0).GetPosition(radius).Coordinate;
            double unwrappedLongitude = longitude + Math.IEEERemainder(point.Longitude - longitude, 360);

            south = Math.Min(south, point.Latitude);
            north = Math.Max(north, point.Latitude);
            west = Math.Min(west, unwrappedLongitude);
            east = Math.Max(east, unwrappedLongitude);
        }

        // Sampling the circle every quarter degree can miss its extent by r(1 - cos 1/8°).
        double sampling = radius * (1 - Math.Cos(0.125 * Math.PI / 180));
        double latitudeTolerance = sampling / 110_000;
        double widestLatitude = Math.Max(Math.Abs(box.North), Math.Abs(box.South)) * Math.PI / 180;
        double longitudeTolerance = latitudeTolerance / Math.Cos(widestLatitude);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(box.South, Is.EqualTo(south).Within(latitudeTolerance));
            Assert.That(box.North, Is.EqualTo(north).Within(latitudeTolerance));
            Assert.That(box.West, Is.EqualTo(west).Within(longitudeTolerance));
            Assert.That(box.East, Is.EqualTo(east).Within(longitudeTolerance));
        }
    }

    [Test]
    public void Around_CircleContainingNorthPole_SpansAllLongitudes()
    {
        GeoBoundingBox box = GeoBoundingBox.Around(new GeoCoordinate(88, 10), 500_000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(box.North, Is.EqualTo(90));
            Assert.That(box.East - box.West, Is.EqualTo(360));
            Assert.That(box.South, Is.EqualTo(83.5).Within(0.1));
        }
    }

    [Test]
    public void Constructor_Inverted_Throws()
    {
        Assert.That(() => new GeoBoundingBox(10, 0, 5, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
