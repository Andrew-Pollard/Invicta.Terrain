// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>
/// Tests the elevation angles from <see cref="Viewpoint"/> against a sphere whose radius matches the ellipsoid's
/// curvature in the direction of view, which is independent of the geocentric calculation.
/// </summary>
internal sealed class ViewpointTests
{
    private const double EccentricitySquared = Wgs84.Flattening * (2 - Wgs84.Flattening);

    private static readonly GeoCoordinate s_scotland = new(57, -5);

    [TestCase(0, 10_000, 1e-8)]
    [TestCase(45, 10_000, 1e-8)]
    [TestCase(90, 10_000, 1e-8)]
    [TestCase(0, 50_000, 1e-7)]
    [TestCase(45, 50_000, 1e-7)]
    [TestCase(90, 50_000, 1e-7)]
    public void ApparentElevationAngle_WithoutRefraction_MatchesOsculatingSphere(
        double azimuth, double distance, double tolerance)
    {
        Viewpoint viewpoint = new(s_scotland, 1000, refractionCoefficient: 0);
        GeoCoordinate target = new GeodesicLine(s_scotland, azimuth).GetPosition(distance);

        double angle = viewpoint.ApparentElevationAngle(target, 500, distance);

        double radius = RadiusOfCurvature(s_scotland.Latitude, azimuth);
        Assert.That(angle, Is.EqualTo(SphereElevationAngle(radius, 1000, 500, distance)).Within(tolerance));
    }

    [Test]
    public void ApparentElevationAngle_ByDirection_DiffersAsTheEllipsoidCurves()
    {
        // The meridian curves more tightly than the prime vertical, so a point 50 km north drops further than one
        // 50 km east. Checking the difference is resolved shows the test above is sensitive to the ellipsoid.
        Viewpoint viewpoint = new(s_scotland, 1000, refractionCoefficient: 0);
        GeoCoordinate north = new GeodesicLine(s_scotland, 0).GetPosition(50_000);
        GeoCoordinate east = new GeodesicLine(s_scotland, 90).GetPosition(50_000);

        double difference = viewpoint.ApparentElevationAngle(east, 500, 50_000)
            - viewpoint.ApparentElevationAngle(north, 500, 50_000);

        double radiusNorth = RadiusOfCurvature(s_scotland.Latitude, 0);
        double radiusEast = RadiusOfCurvature(s_scotland.Latitude, 90);
        double expected = SphereElevationAngle(radiusEast, 1000, 500, 50_000)
            - SphereElevationAngle(radiusNorth, 1000, 500, 50_000);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(difference, Is.GreaterThan(5e-6));
            Assert.That(difference, Is.EqualTo(expected).Within(2e-7));
        }
    }

    [Test]
    public void ApparentElevationAngle_WithRefraction_RaisesByDistanceTimesCoefficientOverTwiceRadius()
    {
        GeoCoordinate target = new GeodesicLine(s_scotland, 30).GetPosition(200_000);
        Viewpoint withoutRefraction = new(s_scotland, 1000, refractionCoefficient: 0);
        Viewpoint withRefraction = new(s_scotland, 1000, refractionCoefficient: 0.13);

        double raise = withRefraction.ApparentElevationAngle(target, 0, 200_000)
            - withoutRefraction.ApparentElevationAngle(target, 0, 200_000);

        Assert.That(raise, Is.EqualTo(0.13 * 200_000 / (2 * 6_371_008.8)).Within(1e-12));
    }

    [TestCase(1.0)]
    [TestCase(double.NaN)]
    [TestCase(double.NegativeInfinity)]
    public void Constructor_RefractionCoefficientNotBelowOne_Throws(double coefficient)
    {
        Assert.That(() => new Viewpoint(s_scotland, 0, coefficient), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Gets the ellipsoid's radius of curvature in a direction, from Euler's theorem.</summary>
    private static double RadiusOfCurvature(double latitude, double azimuth)
    {
        double sinLatitude = Math.Sin(latitude * Math.PI / 180);
        double w2 = 1 - (EccentricitySquared * sinLatitude * sinLatitude);
        double meridian = Wgs84.EquatorialRadius * (1 - EccentricitySquared) / (w2 * Math.Sqrt(w2));
        double primeVertical = Wgs84.EquatorialRadius / Math.Sqrt(w2);

        double sinAzimuth = Math.Sin(azimuth * Math.PI / 180);
        double cosAzimuth = Math.Cos(azimuth * Math.PI / 180);

        return meridian * primeVertical
            / ((meridian * sinAzimuth * sinAzimuth) + (primeVertical * cosAzimuth * cosAzimuth));
    }

    /// <summary>Gets the elevation angle between two points on a sphere, by plane geometry.</summary>
    private static double SphereElevationAngle(double radius, double eyeHeight, double targetHeight, double distance)
    {
        double centralAngle = distance / radius;
        double across = (radius + targetHeight) * Math.Sin(centralAngle);
        double up = ((radius + targetHeight) * Math.Cos(centralAngle)) - (radius + eyeHeight);

        return Math.Atan2(up, across);
    }
}
