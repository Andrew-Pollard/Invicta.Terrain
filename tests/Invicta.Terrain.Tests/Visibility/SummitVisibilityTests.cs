// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;
using Invicta.Places;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>Tests <see cref="SummitVisibility"/> with conical hills on flat ground at sea level.</summary>
internal sealed class SummitVisibilityTests
{
    private const double MaximumDistance = 60_000;

    private static readonly GeoCoordinate s_origin = new(57, -5);

    private readonly Dictionary<string, VisibleSummit> _visible = [];

    [OneTimeSetUp]
    public void FindVisibleSummits()
    {
        // The front hill hides the one behind it on the same bearing: 498 m up at 10 km is 0.049 rad, while
        // 698 m up at 30 km is only 0.023 rad.
        Cone front = new("Front", At(20, 10_000), 500, 3000);
        Cone behind = new("Behind", At(20, 30_000), 700, 3000);
        Cone aside = new("Aside", At(90, 20_000), 400, 2000);
        Cone tooFar = new("Too far", At(200, 70_000), 2000, 5000);
        Cone[] cones = [front, behind, aside, tooFar];

        Viewpoint viewpoint = new(s_origin, 2);
        LayeredTerrain terrain = LayeredTerrain.FromModel(new ConeTerrain(cones), MaximumDistance);
        PanoramaOptions options = new()
        {
            Width = 3600,
            TopAngle = 5,
            BottomAngle = -2,
            MaximumDistance = MaximumDistance,
        };
        Panorama panorama = Panorama.Render(terrain, viewpoint, options, CancellationToken.None);

        IEnumerable<Summit> summits = cones.Select(cone => new Summit(cone.Name, cone.Peak, cone.Height, null))
            .Append(new Summit("Underfoot", At(0, 100), 2, null));
        foreach (VisibleSummit summit in SummitVisibility.FindVisible(panorama, terrain, summits))
        {
            _visible.Add(summit.Summit.Name, summit);
        }
    }

    [Test]
    public void FindVisible_HillsInView_AreVisibleAndOthersAreNot()
    {
        Assert.That(_visible.Keys, Is.EquivalentTo(["Front", "Aside"]));
    }

    [Test]
    public void FindVisible_VisibleHill_IsPlacedAtItsBearingAndElevationAngle()
    {
        // Rising 398 m over 20 km, less the Earth's curvature d / 2R, of which refraction gives back the fraction k.
        VisibleSummit aside = _visible["Aside"];
        double curvature = (1 - Viewpoint.StandardRefractionCoefficient) * 20_000 / (2 * 6_371_000);
        double expectedAngle = (double.Atan2(398, 20_000) - curvature) * 180 / double.Pi;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(aside.Distance, Is.EqualTo(20_000).Within(1));
            Assert.That(aside.Azimuth, Is.EqualTo(90).Within(0.01));
            Assert.That(aside.X, Is.EqualTo(899.5).Within(0.2));

            Assert.That(aside.ElevationAngle, Is.EqualTo(expectedAngle).Within(0.002));
        }
    }

    /// <summary>Tests that a view narrower than a full circle labels only the summits it shows.</summary>
    [Test]
    public void FindVisible_NarrowFieldOfView_LabelsOnlySummitsInView()
    {
        Cone ahead = new("Ahead", At(90, 20_000), 400, 2000);
        Cone behindTheCamera = new("Behind the camera", At(270, 20_000), 400, 2000);
        Cone[] cones = [ahead, behindTheCamera];

        Viewpoint viewpoint = new(s_origin, 2);
        LayeredTerrain terrain = LayeredTerrain.FromModel(new ConeTerrain(cones), MaximumDistance);
        PanoramaOptions options = new()
        {
            Width = 900,
            HorizontalFieldOfView = 90,
            LeftEdgeAzimuth = 45,
            TopAngle = 5,
            BottomAngle = -2,
            MaximumDistance = MaximumDistance,
        };
        Panorama panorama = Panorama.Render(terrain, viewpoint, options, CancellationToken.None);

        IEnumerable<Summit> summits = cones.Select(cone => new Summit(cone.Name, cone.Peak, cone.Height, null));
        IReadOnlyList<VisibleSummit> visible = SummitVisibility.FindVisible(panorama, terrain, summits);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(visible.Select(summit => summit.Summit.Name), Is.EquivalentTo(["Ahead"]));
            Assert.That(visible[0].X, Is.EqualTo(449.5).Within(0.2));
        }
    }

    private static GeoCoordinate At(double azimuth, double distance)
    {
        return new GeodesicLine(s_origin, azimuth).GetPosition(distance);
    }

    /// <summary>Describes a conical hill.</summary>
    private sealed record Cone(string Name, GeoCoordinate Peak, double Height, double Radius);

    /// <summary>Represents flat ground at sea level with conical hills on it.</summary>
    private sealed class ConeTerrain(IReadOnlyList<Cone> cones) : IElevationModel
    {
        /// <inheritdoc/>
        public double GetElevation(GeoCoordinate coordinate)
        {
            double height = 0;
            foreach (Cone cone in cones)
            {
                double distance = Geodesic.Inverse(cone.Peak, coordinate).Distance;
                height = double.Max(height, cone.Height * (1 - (distance / cone.Radius)));
            }

            return height;
        }
    }
}
