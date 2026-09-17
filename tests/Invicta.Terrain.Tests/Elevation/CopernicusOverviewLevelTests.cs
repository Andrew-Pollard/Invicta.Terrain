// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework;

namespace Invicta.Elevation;

/// <summary>Tests how <see cref="CopernicusElevationModel"/> chooses an overview level, which needs no data.</summary>
internal sealed class CopernicusOverviewLevelTests
{
    [TestCase(10, 0)]
    [TestCase(61.9, 0)]
    [TestCase(62, 1)]
    [TestCase(124, 2)]
    [TestCase(248, 3)]
    [TestCase(100_000, 3)]
    public void CoarsestOverviewLevelFor_Spacing_UsesLevelWhoseSamplesAreNoFurtherApart(double spacing, int level)
    {
        Assert.That(CopernicusElevationModel.CoarsestOverviewLevelFor(spacing), Is.EqualTo(level));
    }
}
