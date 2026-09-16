// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Geodesy;

/// <summary>Represents a geodesic that leaves a point at a given azimuth, and finds points along it.</summary>
/// <remarks>
/// Finding many points along one line is much cheaper than solving each as a separate problem, because the series
/// coefficients depend only on the start. Accuracy matches <see cref="Geodesic"/>.
/// </remarks>
public sealed class GeodesicLine
{
    // A port of geod_lineinit and geod_genposition from GeographicLib's geodesic.c, for distances rather than arcs.
    // As in Geodesic, local names follow the original.
    private readonly double _lon1;
    private readonly double _salp0;
    private readonly double _calp0;
    private readonly double _ssig1;
    private readonly double _csig1;
    private readonly double _somg1;
    private readonly double _comg1;
    private readonly double _stau1;
    private readonly double _ctau1;
    private readonly double _a1m1;
    private readonly double _b11;
    private readonly double _a3c;
    private readonly double _b31;
    private readonly double[] _c1pa = new double[Geodesic.SeriesOrder + 1];
    private readonly double[] _c3a = new double[Geodesic.SeriesOrder];

    /// <summary>Initializes a new instance of the <see cref="GeodesicLine"/> class.</summary>
    /// <param name="start">The point where the line starts.</param>
    /// <param name="azimuth">The azimuth at the start in degrees clockwise from north.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="azimuth"/> is not finite.</exception>
    public GeodesicLine(GeoCoordinate start, double azimuth)
    {
        if (!double.IsFinite(azimuth))
        {
            throw new ArgumentOutOfRangeException(nameof(azimuth), azimuth, "The azimuth must be finite.");
        }

        Start = start;
        Azimuth = Geodesic.NormalizeAngle(azimuth);
        _lon1 = start.Longitude;

        // Rounding the azimuth guards against underflow in salp0.
        Geodesic.SinCosDegrees(Geodesic.RoundAngle(Azimuth), out double salp1, out double calp1);
        Geodesic.ReducedLatitude(Geodesic.RoundAngle(start.Latitude), out double sbet1, out double cbet1);

        // sin(alp0) = sin(alp1) cos(bet1), and the hypotenuse form of cos(alp0) behaves better when salp1 is zero.
        _salp0 = salp1 * cbet1;
        _calp0 = double.Hypot(calp1, salp1 * sbet1);

        // tan(sig1) = tan(bet1) / cos(alp1) and tan(omg1) = sin(alp0) tan(sig1), with sig = 0 at the northward
        // crossing of the equator.
        _ssig1 = sbet1;
        _somg1 = _salp0 * sbet1;
        _csig1 = sbet1 != 0 || calp1 != 0 ? cbet1 * calp1 : 1;
        _comg1 = _csig1;
        Geodesic.Normalize(ref _ssig1, ref _csig1);

        double k2 = _calp0 * _calp0 * Geodesic.Ep2;
        double eps = k2 / ((2 * (1 + Math.Sqrt(1 + k2))) + k2);

        Span<double> c1a = stackalloc double[Geodesic.SeriesOrder + 1];
        _a1m1 = Geodesic.A1Minus1(eps);
        Geodesic.C1Coefficients(eps, c1a);
        _b11 = Geodesic.SinCosSeries(true, _ssig1, _csig1, c1a, Geodesic.SeriesOrder);

        // tau1 = sig1 + B11.
        double s = Math.Sin(_b11);
        double c = Math.Cos(_b11);
        _stau1 = (_ssig1 * c) + (_csig1 * s);
        _ctau1 = (_csig1 * c) - (_ssig1 * s);

        Geodesic.C1PrimeCoefficients(eps, _c1pa);

        Geodesic.C3Coefficients(eps, _c3a);
        _a3c = -Geodesic.Flattening * _salp0 * Geodesic.A3(eps);
        _b31 = Geodesic.SinCosSeries(true, _ssig1, _csig1, _c3a, Geodesic.SeriesOrder - 1);
    }

    /// <summary>Gets the point where the line starts.</summary>
    public GeoCoordinate Start { get; }

    /// <summary>Gets the azimuth at the start in degrees clockwise from north, from -180 to 180.</summary>
    public double Azimuth { get; }

    /// <summary>Finds the point at a distance along the line.</summary>
    /// <param name="distance">The distance from the start in meters, which may be negative.</param>
    /// <returns>The point, and the line's azimuth there.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is not finite.</exception>
    public GeodesicPosition GetPosition(double distance)
    {
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "The distance must be finite.");
        }

        // tau2 = tau1 + tau12, and sig12 follows from reverting the distance series.
        double tau12 = distance / (Geodesic.PolarRadius * (1 + _a1m1));
        double s = Math.Sin(tau12);
        double c = Math.Cos(tau12);
        double b12 = -Geodesic.SinCosSeries(
            true, (_stau1 * c) + (_ctau1 * s), (_ctau1 * c) - (_stau1 * s), _c1pa, Geodesic.SeriesOrder);
        double sig12 = tau12 - (b12 - _b11);
        double ssig12 = Math.Sin(sig12);
        double csig12 = Math.Cos(sig12);

        // sig2 = sig1 + sig12.
        double ssig2 = (_ssig1 * csig12) + (_csig1 * ssig12);
        double csig2 = (_csig1 * csig12) - (_ssig1 * ssig12);

        // sin(bet2) = cos(alp0) sin(sig2).
        double sbet2 = _calp0 * ssig2;
        double cbet2 = double.Hypot(_salp0, _calp0 * csig2);
        if (cbet2 == 0)
        {
            // Break the degeneracy where salp0 and csig2 are both zero.
            cbet2 = csig2 = Geodesic.Tiny;
        }

        // tan(alp0) = cos(sig2) tan(alp2), which needs no normalization.
        double salp2 = _salp0;
        double calp2 = _calp0 * csig2;

        // tan(omg2) = sin(alp0) tan(sig2), and omg12 = omg2 - omg1.
        double somg2 = _salp0 * ssig2;
        double comg2 = csig2;
        double omg12 = Math.Atan2((somg2 * _comg1) - (comg2 * _somg1), (comg2 * _comg1) + (somg2 * _somg1));

        double b32 = Geodesic.SinCosSeries(true, ssig2, csig2, _c3a, Geodesic.SeriesOrder - 1);
        double lam12 = omg12 + (_a3c * (sig12 + (b32 - _b31)));
        double lon12 = lam12 / Geodesic.Degree;
        double lon2 = Geodesic.NormalizeAngle(Geodesic.NormalizeAngle(_lon1) + Geodesic.NormalizeAngle(lon12));
        double lat2 = Geodesic.Atan2Degrees(sbet2, Geodesic.F1 * cbet2);

        return new GeodesicPosition(new GeoCoordinate(lat2, lon2), Geodesic.Atan2Degrees(salp2, calp2));
    }
}
