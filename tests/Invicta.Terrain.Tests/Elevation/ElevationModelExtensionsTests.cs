// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Elevation;

/// <summary>Tests <see cref="ElevationModelExtensions"/>.</summary>
internal sealed class ElevationModelExtensionsTests
{
    [Test]
    public void FindHighestPoint_RidgeRunningNorthToSouth_FindsPointOnRidge()
    {
        // A ridge along longitude -5.0003, about 18 m west of the center, falling away 1 m per millionth of a degree.
        RidgeTerrain terrain = new(-5.0003);
        GeoCoordinate center = new(57, -5);

        (GeoCoordinate coordinate, double height) = terrain.FindHighestPoint(center, 100, 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coordinate.Longitude, Is.EqualTo(-5.0003).Within(0.0001));
            Assert.That(height, Is.EqualTo(terrain.GetElevation(coordinate)));
            Assert.That(height, Is.GreaterThan(terrain.GetElevation(center)));
        }
    }

    [Test]
    public void FindHighestPoint_SpacingNotPositive_Throws()
    {
        Assert.That(
            () => new SeaTerrain().FindHighestPoint(new GeoCoordinate(57, -5), 100, 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Represents a ridge along a meridian.</summary>
    private sealed class RidgeTerrain(double ridgeLongitude) : IElevationModel
    {
        /// <inheritdoc/>
        public double GetElevation(GeoCoordinate coordinate)
        {
            return 1000 - (Math.Abs(coordinate.Longitude - ridgeLongitude) * 1e6);
        }
    }
}
