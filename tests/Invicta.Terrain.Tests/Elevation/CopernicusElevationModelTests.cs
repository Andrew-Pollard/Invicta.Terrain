// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Elevation;

/// <summary>
/// Tests <see cref="CopernicusElevationModel"/> against surveyed heights and against itself across tile edges and
/// overview levels, using the four tiles that meet at 56° N 5° W, near Ben Nevis.
/// </summary>
[Category(TestCategories.DownloadedData)]
internal sealed class CopernicusElevationModelTests
{
    private static readonly GeoBoundingBox s_region = new(55.5, -5.5, 56.9, -4.5);

    private readonly CopernicusElevationModel[] _models =
        new CopernicusElevationModel[CopernicusGrid.OverviewLevelCount + 1];

    [OneTimeSetUp]
    public async Task LoadModels()
    {
        for (int level = 0; level < _models.Length; level++)
        {
            _models[level] = await LoadAsync(s_region, level);
        }
    }

    [Test]
    public void GetElevation_BenNevisTrigPoint_MatchesSurveyedHeight()
    {
        // Ordnance Survey: 1,345 m at NN 16671 71259. The DEM's stated absolute vertical accuracy is better than 4 m.
        GeoCoordinate trigPoint = new(56.796851, -5.003508);

        Assert.That(_models[0].GetElevation(trigPoint), Is.EqualTo(1345).Within(4));
    }

    [Test]
    public void GetElevation_AcrossTileCorner_IsContinuous()
    {
        const double Step = 1e-9;

        double[] heights =
        [
            _models[0].GetElevation(new GeoCoordinate(56 + Step, -5 - Step)),
            _models[0].GetElevation(new GeoCoordinate(56 + Step, -5 + Step)),
            _models[0].GetElevation(new GeoCoordinate(56 - Step, -5 - Step)),
            _models[0].GetElevation(new GeoCoordinate(56 - Step, -5 + Step)),
        ];

        Assert.That(heights.Max() - heights.Min(), Is.LessThan(1e-3));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void GetElevation_AtOverviewSample_MatchesMeanOfFinerLevel(int level)
    {
        // An overview sample is the mean of a 2 × 2 block at the next finer level, and bilinear interpolation at the
        // center of that block gives the same mean. This pins down where each level's samples lie.
        Random random = new(level);
        double worstDifference = 0;
        for (int i = 0; i < 100; i++)
        {
            int row = random.Next(0, CopernicusGrid.RowsPerDegree(level));
            int column = random.Next(0, CopernicusGrid.ColumnsPerDegree(56, level));
            GeoCoordinate sample = OverviewSample(56, -6, level, row, column);

            double difference = _models[level].GetElevation(sample) - _models[level - 1].GetElevation(sample);
            worstDifference = double.Max(worstDifference, double.Abs(difference));
        }

        Assert.That(worstDifference, Is.LessThan(0.01));
    }

    [Test]
    public void GetElevation_OutsideLoadedRegion_Throws()
    {
        GeoCoordinate farAway = new(51.5, -0.1);

        Assert.That(() => _models[0].GetElevation(farAway), Throws.InvalidOperationException);
    }

    [Test]
    public async Task GetElevation_OpenSea_IsZero()
    {
        GeoBoundingBox atlantic = new(45.2, -30.8, 45.8, -30.2);
        CopernicusElevationModel model = await LoadAsync(atlantic, 0);

        Assert.That(model.GetElevation(new GeoCoordinate(45.5, -30.5)), Is.Zero);
    }

    private static Task<CopernicusElevationModel> LoadAsync(GeoBoundingBox region, int level)
    {
        return CopernicusElevationModel.LoadAsync(TestData.CopernicusTiles, region, level, CancellationToken.None);
    }

    /// <summary>Gets the position of a sample in a tile at an overview level.</summary>
    private static GeoCoordinate OverviewSample(int tileLatitude, int tileLongitude, int level, int row, int column)
    {
        double rowSpacing = 1.0 / CopernicusGrid.RowsPerDegree(level);
        double rowOffset = CopernicusGrid.SampleOffset(1.0 / CopernicusGrid.RowsPerDegree(0), level);
        double latitude = tileLatitude + 1 - rowOffset - (row * rowSpacing);

        double columnSpacing = 1.0 / CopernicusGrid.ColumnsPerDegree(tileLatitude, level);
        double fullColumnSpacing = 1.0 / CopernicusGrid.ColumnsPerDegree(tileLatitude, 0);
        double columnOffset = CopernicusGrid.SampleOffset(fullColumnSpacing, level);
        double longitude = tileLongitude + columnOffset + (column * columnSpacing);

        return new GeoCoordinate(latitude, longitude);
    }
}
