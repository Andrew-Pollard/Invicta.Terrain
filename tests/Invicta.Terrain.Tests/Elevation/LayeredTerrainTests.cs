// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework;

namespace Invicta.Elevation;

/// <summary>Tests <see cref="LayeredTerrain"/>.</summary>
internal sealed class LayeredTerrainTests
{
    private readonly SeaTerrain _near = new();
    private readonly SeaTerrain _far = new();

    [TestCase(0, false)]
    [TestCase(1000, false)]
    [TestCase(1000.001, true)]
    [TestCase(1_000_000, true)]
    public void GetModel_ByDistance_ReturnsNearestLayerThatReachesIt(double distance, bool expectFar)
    {
        LayeredTerrain terrain = new([new TerrainLayer(1000, _near), new TerrainLayer(5000, _far)]);

        Assert.That(terrain.GetModel(distance), Is.SameAs(expectFar ? _far : _near));
    }

    [Test]
    public void Constructor_DistancesNotIncreasing_Throws()
    {
        TerrainLayer[] layers = [new TerrainLayer(1000, _near), new TerrainLayer(1000, _far)];

        Assert.That(() => new LayeredTerrain(layers), Throws.ArgumentException);
    }

    [Test]
    public void Constructor_NoLayers_Throws()
    {
        Assert.That(() => new LayeredTerrain([]), Throws.ArgumentException);
    }
}
