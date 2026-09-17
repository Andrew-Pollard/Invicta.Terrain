// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>
/// Tests <see cref="LineOfSight"/> on the photographed 443 km view from Pic de Finestrelles in the Pyrenees to Pic
/// Gaspard in the Alps, which passes over the Gulf of Lion and so depends on refraction.
/// </summary>
/// <remarks>
/// Summit positions come from OpenStreetMap, moved to the highest terrain within 150 m. The terrain model puts Pic
/// Gaspard at 3,785 m, about 100 m below its surveyed height, as 30 m data does on sharp summits.
/// </remarks>
[Category(TestCategories.DownloadedData)]
internal sealed class RecordLineOfSightTests
{
    private const double SampleSpacing = 15;

    private static readonly GeoCoordinate s_picDeFinestrelles = new(42.4144346, 2.1334676);
    private static readonly GeoCoordinate s_picGaspard = new(44.9978931, 6.3305818);

    private CopernicusElevationModel _terrain = null!;

    [OneTimeSetUp]
    public async Task LoadTerrain()
    {
        GeoBoundingBox region = new(42.3, 2.0, 45.1, 6.4);

        _terrain = await CopernicusElevationModel.LoadAsync(
            TestData.CopernicusTiles, region, 0, CancellationToken.None);
    }

    [Test]
    public void Trace_WithStrongRefraction_SeesPicGaspard()
    {
        LineOfSightResult result = Trace(refractionCoefficient: 0.2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Distance, Is.EqualTo(443_500).Within(500));
            Assert.That(result.IsVisible, Is.True);
        }
    }

    [TestCase(0.0)]
    [TestCase(Viewpoint.StandardRefractionCoefficient)]
    public void Trace_WithLessRefraction_IsBlockedByLowGroundAcrossTheEarthsCurve(double refractionCoefficient)
    {
        // The line of sight dips toward the coast of the Gulf of Lion, where it clips hills barely 100 m high.
        LineOfSightResult result = Trace(refractionCoefficient);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsVisible, Is.False);
            Assert.That(result.Obstruction?.Height, Is.LessThan(200));
            Assert.That(result.Obstruction?.Distance, Is.InRange(150_000, 250_000));
        }
    }

    private LineOfSightResult Trace(double refractionCoefficient)
    {
        (GeoCoordinate eye, double eyeGround) = _terrain.FindHighestPoint(s_picDeFinestrelles, 150, 5);
        (GeoCoordinate summit, double summitHeight) = _terrain.FindHighestPoint(s_picGaspard, 150, 5);
        Viewpoint viewpoint = new(eye, eyeGround + 2, refractionCoefficient);

        return LineOfSight.Trace(_terrain, viewpoint, summit, summitHeight, SampleSpacing);
    }
}
