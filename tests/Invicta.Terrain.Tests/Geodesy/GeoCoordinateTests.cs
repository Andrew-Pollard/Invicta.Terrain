// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework;

namespace Invicta.Geodesy;

/// <summary>Tests <see cref="GeoCoordinate"/>.</summary>
internal sealed class GeoCoordinateTests
{
    [TestCase(-90.0000001)]
    [TestCase(90.0000001)]
    [TestCase(double.NaN)]
    public void Constructor_LatitudeNotFromMinus90To90_Throws(double latitude)
    {
        Assert.That(() => new GeoCoordinate(latitude, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void Constructor_LongitudeNotFinite_Throws(double longitude)
    {
        Assert.That(() => new GeoCoordinate(0, longitude), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ToString_BenNevis_ReturnsInvariantDecimalDegrees()
    {
        GeoCoordinate coordinate = new(56.796851, -5.003508);

        Assert.That(coordinate.ToString(), Is.EqualTo("56.79685, -5.00351"));
    }
}
