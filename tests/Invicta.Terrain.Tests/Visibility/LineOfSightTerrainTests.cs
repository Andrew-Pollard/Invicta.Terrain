// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>Tests <see cref="LineOfSight"/> over real terrain around Ben Nevis.</summary>
[Category(TestCategories.DownloadedData)]
internal sealed class LineOfSightTerrainTests
{
    private const double EyeHeight = 2;
    private const double SampleSpacing = 15;

    private static readonly GeoBoundingBox s_region = new(55.5, -5.5, 56.9, -4.5);

    private CopernicusElevationModel _terrain = null!;

    [OneTimeSetUp]
    public async Task LoadTerrain()
    {
        _terrain = await CopernicusElevationModel.LoadAsync(
            TestData.CopernicusTiles, s_region, 0, CancellationToken.None);
    }

    [Test]
    public void Trace_BetweenNearbyPairs_AgreesInBothDirections()
    {
        // Samples fall at different points in each direction, so lines that only just clear or only just miss the
        // terrain may disagree. Anything clearing or missing by more than 0.01° must agree.
        const double Margin = 0.01 * Math.PI / 180;

        Random random = new(57);
        int visible = 0;
        int hidden = 0;
        int disagreements = 0;
        for (int i = 0; i < 1000; i++)
        {
            GeoCoordinate first = RandomPoint(random);
            GeoCoordinate second = new GeodesicLine(first, random.NextDouble() * 360)
                .GetPosition(200 + (random.NextDouble() * 4800));

            LineOfSightResult forward = TraceBetween(first, second);
            LineOfSightResult backward = TraceBetween(second, first);
            if (Math.Abs(forward.Clearance) > Margin && Math.Abs(backward.Clearance) > Margin)
            {
                visible += forward.IsVisible ? 1 : 0;
                hidden += forward.IsVisible ? 0 : 1;
                disagreements += forward.IsVisible == backward.IsVisible ? 0 : 1;
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(disagreements, Is.Zero);
            Assert.That(visible, Is.GreaterThan(200), "Too few visible pairs for the test to mean much.");
            Assert.That(hidden, Is.GreaterThan(200), "Too few hidden pairs for the test to mean much.");
        }
    }

    private LineOfSightResult TraceBetween(GeoCoordinate from, GeoCoordinate to)
    {
        Viewpoint viewpoint = Viewpoint.AboveTerrain(_terrain, from, EyeHeight);

        return LineOfSight.Trace(_terrain, viewpoint, to, _terrain.GetElevation(to) + EyeHeight, SampleSpacing);
    }

    private static GeoCoordinate RandomPoint(Random random)
    {
        // Keep 5 km inside the region, so the second point of each pair is inside too.
        const double Inset = 0.1;

        double latitudeRange = s_region.North - s_region.South - (2 * Inset);
        double longitudeRange = s_region.East - s_region.West - (2 * Inset);

        return new GeoCoordinate(
            s_region.South + Inset + (random.NextDouble() * latitudeRange),
            s_region.West + Inset + (random.NextDouble() * longitudeRange));
    }
}
