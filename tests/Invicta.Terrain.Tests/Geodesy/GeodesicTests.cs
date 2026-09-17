// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.IO.Compression;

using NUnit.Framework;

namespace Invicta.Geodesy;

/// <summary>
/// Tests <see cref="Geodesic"/> and <see cref="GeodesicLine"/> against GeographicLib's published test set, whose
/// geodesics were computed to high precision with independent code.
/// </summary>
/// <remarks>
/// Errors are measured as displacements in meters and held to the 15 nm that GeographicLib states. Azimuths are ill
/// conditioned near the poles and between nearly antipodal points, where a tiny change in position swings them
/// widely, so an azimuth error counts by how far it moves the far end of the geodesic.
/// </remarks>
[Category(TestCategories.DownloadedData)]
internal sealed class GeodesicTests
{
    private const double Tolerance = 15e-9;

    private static readonly Uri s_testSetSource =
        new("https://sourceforge.net/projects/geographiclib/files/testdata/GeodTest-short.dat.gz/download");

    private ReferenceGeodesic[] _testSet = [];

    [OneTimeSetUp]
    public async Task LoadTestSet()
    {
        string path = await TestData.GetFileAsync(
            Path.Combine("geographiclib", "GeodTest-short.dat.gz"), s_testSetSource);

        await using FileStream file = File.OpenRead(path);
        await using GZipStream decompressed = new(file, CompressionMode.Decompress);
        using StreamReader reader = new(decompressed);

        List<ReferenceGeodesic> testSet = [];
        while (await reader.ReadLineAsync() is string line)
        {
            testSet.Add(ReferenceGeodesic.Parse(line));
        }

        _testSet = [.. testSet];
    }

    [Test]
    public void LoadTestSet_ShortTestSet_HasTenThousandGeodesics()
    {
        Assert.That(_testSet, Has.Length.EqualTo(10_000));
    }

    [Test]
    public void Inverse_TestSet_MatchesDistancesAndAzimuths()
    {
        double worstDistanceError = 0;
        double worstAzimuthDisplacement = 0;
        foreach (ReferenceGeodesic reference in _testSet)
        {
            GeodesicSolution solution = Geodesic.Inverse(reference.Start, reference.End);

            // The reduced length converts a change in azimuth at one end into a displacement at the other.
            double initialAzimuthError = AngleErrorInRadians(solution.InitialAzimuth, reference.InitialAzimuth);
            double finalAzimuthError = AngleErrorInRadians(solution.FinalAzimuth, reference.FinalAzimuth);
            double azimuthDisplacement =
                Math.Max(initialAzimuthError, finalAzimuthError) * Math.Abs(reference.ReducedLength);

            worstDistanceError = Math.Max(worstDistanceError, Math.Abs(solution.Distance - reference.Distance));
            worstAzimuthDisplacement = Math.Max(worstAzimuthDisplacement, azimuthDisplacement);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(worstDistanceError, Is.LessThan(Tolerance));
            Assert.That(worstAzimuthDisplacement, Is.LessThan(Tolerance));
        }
    }

    [Test]
    public void GetPosition_TestSet_MatchesPositions()
    {
        double worstPositionError = 0;
        foreach (ReferenceGeodesic reference in _testSet)
        {
            GeodesicLine line = new(reference.Start, reference.InitialAzimuth);
            GeoCoordinate position = line.GetPosition(reference.Distance);

            double northError =
                AngleErrorInRadians(position.Latitude, reference.End.Latitude) * Wgs84.EquatorialRadius;
            double eastError = AngleErrorInRadians(position.Longitude, reference.End.Longitude)
                * Wgs84.EquatorialRadius * Math.Cos(reference.End.Latitude * Math.PI / 180);
            worstPositionError = Math.Max(worstPositionError, double.Hypot(northError, eastError));
        }

        Assert.That(worstPositionError, Is.LessThan(Tolerance));
    }

    private static double AngleErrorInRadians(double actual, double expected)
    {
        return Math.Abs(Math.IEEERemainder(actual - expected, 360)) * Math.PI / 180;
    }

    /// <summary>Describes one geodesic from the test set.</summary>
    private readonly record struct ReferenceGeodesic(
        GeoCoordinate Start,
        double InitialAzimuth,
        GeoCoordinate End,
        double FinalAzimuth,
        double Distance,
        double ReducedLength)
    {
        /// <summary>Parses a line of the test set: lat1 lon1 azi1 lat2 lon2 azi2 s12 a12 m12 S12.</summary>
        public static ReferenceGeodesic Parse(string line)
        {
            double[] fields =
            [
                .. line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => double.Parse(f, CultureInfo.InvariantCulture)),
            ];

            return new ReferenceGeodesic(
                new GeoCoordinate(fields[0], fields[1]),
                fields[2],
                new GeoCoordinate(fields[3], fields[4]),
                fields[5],
                fields[6],
                fields[8]);
        }
    }
}
