// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;
using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Visibility;

/// <summary>Tests <see cref="Panorama"/> over a smooth sea, where the view follows from simple geometry.</summary>
internal sealed class PanoramaTests
{
    private const double EyeHeight = 1000;
    private const double RefractionCoefficient = 0.13;
    private const double MaximumDistance = 200_000;

    // Refraction stretches the Earth's effective radius to R / (1 - k).
    private const double EffectiveRadius = 6_371_000 / (1 - RefractionCoefficient);

    private static readonly GeoCoordinate s_location = new(57, -5);

    private Panorama _panorama = null!;

    [OneTimeSetUp]
    public void RenderOverSea()
    {
        Viewpoint viewpoint = new(s_location, EyeHeight, RefractionCoefficient);
        LayeredTerrain terrain = LayeredTerrain.FromModel(new SeaTerrain(), MaximumDistance);
        PanoramaOptions options = new()
        {
            Width = 3600,
            TopAngle = 1,
            BottomAngle = -3,
            MaximumDistance = MaximumDistance,
        };

        _panorama = Panorama.Render(terrain, viewpoint, options, CancellationToken.None);
    }

    [Test]
    public void Render_OverSea_SizesImageFromPixelAngle()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_panorama.PixelAngle, Is.EqualTo(0.1));
            Assert.That(_panorama.Height, Is.EqualTo(40));
        }
    }

    [TestCase(0)]
    [TestCase(900)]
    [TestCase(2700)]
    public void Render_OverSea_PutsHorizonAtDipAngle(int column)
    {
        // The horizon dips below the horizontal by sqrt(2h / R), to within a pixel.
        double dip = Math.Sqrt(2 * EyeHeight / EffectiveRadius) * 180 / Math.PI;

        int horizonRow = Enumerable.Range(0, _panorama.Height)
            .First(y => !double.IsNaN(_panorama.GetDistance(column, y)));

        Assert.That(_panorama.ElevationAngleAt(horizonRow), Is.EqualTo(-dip).Within(_panorama.PixelAngle));
    }

    [TestCase(22)]
    [TestCase(25)]
    [TestCase(39)]
    public void Render_OverSea_ShowsSeaAtDistanceMatchingDepressionAngle(int row)
    {
        // Looking down by a small angle a, the sea falls away with curvature, so the line of sight meets it where
        // a = h / d + d / 2R. The nearer root is d = R (a - sqrt(a^2 - 2h / R)).
        double depression = -_panorama.ElevationAngleAt(row) * Math.PI / 180;
        double discriminant = (depression * depression) - (2 * EyeHeight / EffectiveRadius);
        double expected = EffectiveRadius * (depression - Math.Sqrt(discriminant));

        Assert.That(_panorama.GetDistance(100, row), Is.EqualTo(expected).Within(0.02 * expected));
    }

    [Test]
    public void Render_WidthTooSmall_Throws()
    {
        Viewpoint viewpoint = new(s_location, EyeHeight);
        LayeredTerrain terrain = LayeredTerrain.FromModel(new SeaTerrain(), 1000);
        PanoramaOptions options = new() { Width = 2 };

        Assert.That(
            () => Panorama.Render(terrain, viewpoint, options, CancellationToken.None),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
