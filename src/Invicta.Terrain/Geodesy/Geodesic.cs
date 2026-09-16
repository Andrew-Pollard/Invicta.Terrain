// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Geodesy;

/// <summary>Solves geodesic problems on the WGS 84 ellipsoid.</summary>
/// <remarks>
/// The solutions are accurate to about 15 nanometers for any pair of points, including nearly antipodal ones. They
/// port the algorithms in GeographicLib by Charles Karney, described in
/// <see href="https://doi.org/10.1007/s00190-012-0578-z">Algorithms for geodesics</see>, J. Geodesy 87, 43–55
/// (2013).
/// </remarks>
public static class Geodesic
{
    // This is a port of geodesic.c from GeographicLib 2.x, reduced to distances, azimuths and positions on WGS 84.
    // Local names follow the original so that the two can be compared line by line. Branches the original takes only
    // for prolate or very eccentric ellipsoids are left out.

    /// <summary>The number of terms in each series expansion.</summary>
    internal const int SeriesOrder = 6;

    /// <summary>The number of radians in one degree.</summary>
    internal const double Degree = Math.PI / 180;

    internal const double Flattening = Wgs84.Flattening;
    internal const double PolarRadius = Wgs84.EquatorialRadius * (1 - Flattening);

    /// <summary>The ratio of the polar radius to the equatorial radius.</summary>
    internal const double F1 = 1 - Flattening;

    /// <summary>The second eccentricity squared.</summary>
    internal const double Ep2 = Flattening * (2 - Flattening) / (F1 * F1);

    private const double ThirdFlattening = Flattening / (2 - Flattening);

    private const double QuarterTurn = 90;
    private const double HalfTurn = 180;
    private const double FullTurn = 360;

    // The C library's DBL_EPSILON, 2^-52, which is not the same as double.Epsilon.
    private const double MachineEpsilon = 2.220446049250313e-16;

    private const int NewtonIterationLimit = 20;
    private const int BisectionIterationLimit = NewtonIterationLimit + 53 + 10;

    private const double Tol0 = MachineEpsilon;
    private const double Tol1 = 200 * Tol0;
    private const double Tolb = Tol0;

    // The square root of Tol0, 2^-26.
    private const double Tol2 = 1.4901161193847656e-8;
    private const double Xthresh = 1000 * Tol2;

    /// <summary>
    /// A value small enough to stand in for zero without causing division by zero: 2^-511, the square root of the
    /// smallest normal double.
    /// </summary>
    internal const double Tiny = 1.4916681462400413e-154;

    // The central angle below which a geodesic counts as really short.
    private static readonly double s_etol2 =
        0.1 * Tol2 / Math.Sqrt(Math.Max(0.001, Flattening) * Math.Min(1.0, 1 - (Flattening / 2)) / 2);

    private static readonly double[] s_a3x = ComputeA3Coefficients();
    private static readonly double[] s_c3x = ComputeC3Coefficients();

    /// <summary>Finds the shortest path between two points.</summary>
    /// <param name="start">The point where the path starts.</param>
    /// <param name="end">The point where the path ends.</param>
    /// <returns>The length of the path and the azimuths at each end.</returns>
    public static GeodesicSolution Inverse(GeoCoordinate start, GeoCoordinate end)
    {
        double lat1 = start.Latitude;
        double lat2 = end.Latitude;

        // Bring the points into a canonical position: 0 <= lon12 <= 180, -90 <= lat1 <= -0, and
        // lat1 <= lat2 <= -lat1. lonsign, swapp and latsign record how, so the azimuths can be mapped back.
        double lon12 = AngleDifference(start.Longitude, end.Longitude, out double lon12s);
        int lonsign = double.IsNegative(lon12) ? -1 : 1;
        lon12 *= lonsign;
        lon12s *= lonsign;
        double lam12 = lon12 * Degree;
        SinCosDegrees(lon12, lon12s, out double slam12, out double clam12);

        // The supplementary longitude difference.
        lon12s = HalfTurn - lon12 - lon12s;

        lat1 = RoundAngle(lat1);
        lat2 = RoundAngle(lat2);
        int swapp = Math.Abs(lat1) < Math.Abs(lat2) ? -1 : 1;
        if (swapp < 0)
        {
            lonsign *= -1;
            (lat1, lat2) = (lat2, lat1);
        }

        int latsign = double.IsNegative(lat1) ? 1 : -1;
        lat1 *= latsign;
        lat2 *= latsign;

        ReducedLatitude(lat1, out double sbet1, out double cbet1);
        ReducedLatitude(lat2, out double sbet2, out double cbet2);

        // Force bet2 = +/-bet1 exactly where the quantities that measure their difference vanish.
        if (cbet1 < -sbet1)
        {
            if (cbet2 == cbet1)
            {
                sbet2 = Math.CopySign(sbet1, sbet2);
            }
        }
        else if (Math.Abs(sbet2) == -sbet1)
        {
            cbet2 = cbet1;
        }

        double dn1 = Math.Sqrt(1 + (Ep2 * sbet1 * sbet1));
        double dn2 = Math.Sqrt(1 + (Ep2 * sbet2 * sbet2));

        Span<double> ca = stackalloc double[SeriesOrder + 1];
        double s12x = 0;
        double salp1 = 0;
        double calp1 = 0;
        double salp2 = 0;
        double calp2 = 0;

        bool meridian = lat1 == -QuarterTurn || slam12 == 0;
        if (meridian)
        {
            // The end points are on a single full meridian, so the geodesic might lie on it.
            calp1 = clam12;
            salp1 = slam12;
            calp2 = 1;
            salp2 = 0;

            double ssig1 = sbet1;
            double csig1 = calp1 * cbet1;
            double ssig2 = sbet2;
            double csig2 = calp2 * cbet2;

            double sig12 = Math.Atan2(
                Math.Max(0.0, (csig1 * ssig2) - (ssig1 * csig2)) + 0, (csig1 * csig2) + (ssig1 * ssig2));
            Lengths(ThirdFlattening, sig12, ssig1, csig1, dn1, ssig2, csig2, dn2, true, out s12x, out double m12x, ca);

            if (sig12 < Tol2 || m12x >= 0)
            {
                if (sig12 < 3 * Tiny || (sig12 < Tol0 && (s12x < 0 || m12x < 0)))
                {
                    s12x = 0;
                }

                s12x *= PolarRadius;
            }
            else
            {
                meridian = false;
            }
        }

        if (!meridian && sbet1 == 0 && lon12s >= Flattening * HalfTurn)
        {
            // The geodesic runs along the equator.
            calp1 = calp2 = 0;
            salp1 = salp2 = 1;
            s12x = Wgs84.EquatorialRadius * lam12;
        }
        else if (!meridian)
        {
            double sig12 = InverseStart(
                sbet1, cbet1, sbet2, cbet2, lam12, slam12, clam12, out salp1, out calp1, out salp2, out calp2,
                out double dnm);

            if (sig12 >= 0)
            {
                s12x = sig12 * PolarRadius * dnm;
            }
            else
            {
                s12x = SolveByNewton(
                    sbet1, cbet1, dn1, sbet2, cbet2, dn2, slam12, clam12, ref salp1, ref calp1, out salp2, out calp2,
                    ca);
            }
        }

        if (swapp < 0)
        {
            (salp1, salp2) = (salp2, salp1);
            (calp1, calp2) = (calp2, calp1);
        }

        salp1 *= swapp * lonsign;
        calp1 *= swapp * latsign;
        salp2 *= swapp * lonsign;
        calp2 *= swapp * latsign;

        // Adding zero converts -0 to 0.
        return new GeodesicSolution(0 + s12x, Atan2Degrees(salp1, calp1), Atan2Degrees(salp2, calp2));
    }

    /// <summary>
    /// Finds the initial azimuth of a geodesic that is neither meridional nor equatorial by Newton's method, keeping
    /// the root bracketed and bisecting when a step would leave the bracket.
    /// </summary>
    /// <returns>The length of the geodesic.</returns>
    private static double SolveByNewton(
        double sbet1, double cbet1, double dn1, double sbet2, double cbet2, double dn2, double slam12, double clam12,
        ref double salp1, ref double calp1, out double salp2, out double calp2, Span<double> ca)
    {
        double sig12;
        double ssig1;
        double csig1;
        double ssig2;
        double csig2;
        double eps;

        double salp1a = Tiny;
        double calp1a = 1;
        double salp1b = Tiny;
        double calp1b = -1;
        bool tripn = false;
        bool tripb = false;

        for (int numit = 0; ; numit++)
        {
            double v = Lambda12(
                sbet1, cbet1, dn1, sbet2, cbet2, dn2, salp1, calp1, slam12, clam12, out salp2, out calp2, out sig12,
                out ssig1, out csig1, out ssig2, out csig2, out eps, numit < NewtonIterationLimit, out double dv, ca);

            // The reversed test lets NaNs escape.
            if (tripb || !(Math.Abs(v) >= (tripn ? 8 : 1) * Tol0) || numit == BisectionIterationLimit)
            {
                break;
            }

            if (v > 0 && (numit > NewtonIterationLimit || calp1 / salp1 > calp1b / salp1b))
            {
                salp1b = salp1;
                calp1b = calp1;
            }
            else if (v < 0 && (numit > NewtonIterationLimit || calp1 / salp1 < calp1a / salp1a))
            {
                salp1a = salp1;
                calp1a = calp1;
            }

            if (numit < NewtonIterationLimit && dv > 0)
            {
                double dalp1 = -v / dv;
                if (Math.Abs(dalp1) < Math.PI)
                {
                    double sdalp1 = Math.Sin(dalp1);
                    double cdalp1 = Math.Cos(dalp1);
                    double nsalp1 = (salp1 * cdalp1) + (calp1 * sdalp1);
                    if (nsalp1 > 0)
                    {
                        calp1 = (calp1 * cdalp1) - (salp1 * sdalp1);
                        salp1 = nsalp1;
                        Normalize(ref salp1, ref calp1);

                        // Convergence is not always quadratic, so test against epsilon rather than its square root.
                        tripn = Math.Abs(v) <= 16 * Tol0;
                        continue;
                    }
                }
            }

            // The Newton step was unusable, so bisect the bracket.
            salp1 = (salp1a + salp1b) / 2;
            calp1 = (calp1a + calp1b) / 2;
            Normalize(ref salp1, ref calp1);
            tripn = false;
            tripb = Math.Abs(salp1a - salp1) + (calp1a - calp1) < Tolb
                || Math.Abs(salp1 - salp1b) + (calp1 - calp1b) < Tolb;
        }

        Lengths(eps, sig12, ssig1, csig1, dn1, ssig2, csig2, dn2, true, out double s12b, out _, ca);

        return s12b * PolarRadius;
    }

    /// <summary>Gets the sine and cosine of the reduced latitude, keeping the cosine positive at the poles.</summary>
    internal static void ReducedLatitude(double latitude, out double sbet, out double cbet)
    {
        SinCosDegrees(latitude, out sbet, out cbet);
        sbet *= F1;
        Normalize(ref sbet, ref cbet);
        cbet = Math.Max(Tiny, cbet);
    }

    /// <summary>
    /// Computes the distance and reduced length of a geodesic, each divided by the polar radius. The distance is only
    /// computed when <paramref name="computeDistance"/> is <see langword="true"/>.
    /// </summary>
    private static void Lengths(
        double eps, double sig12, double ssig1, double csig1, double dn1, double ssig2, double csig2, double dn2,
        bool computeDistance, out double s12b, out double m12b, Span<double> ca)
    {
        Span<double> cb = stackalloc double[SeriesOrder + 1];

        double a1 = A1Minus1(eps);
        C1Coefficients(eps, ca);
        double a2 = A2Minus1(eps);
        C2Coefficients(eps, cb);
        double m0 = a1 - a2;
        a2 = 1 + a2;
        a1 = 1 + a1;

        double j12;
        if (computeDistance)
        {
            double b1 = SinCosSeries(true, ssig2, csig2, ca, SeriesOrder)
                - SinCosSeries(true, ssig1, csig1, ca, SeriesOrder);
            s12b = a1 * (sig12 + b1);

            double b2 = SinCosSeries(true, ssig2, csig2, cb, SeriesOrder)
                - SinCosSeries(true, ssig1, csig1, cb, SeriesOrder);
            j12 = (m0 * sig12) + ((a1 * b1) - (a2 * b2));
        }
        else
        {
            s12b = double.NaN;
            for (int l = 1; l <= SeriesOrder; l++)
            {
                cb[l] = (a1 * ca[l]) - (a2 * cb[l]);
            }

            double b12 = SinCosSeries(true, ssig2, csig2, cb, SeriesOrder)
                - SinCosSeries(true, ssig1, csig1, cb, SeriesOrder);
            j12 = (m0 * sig12) + b12;
        }

        // The parentheses around csig1 * ssig2 and ssig1 * csig2 ensure accurate cancellation for coincident points.
        m12b = (dn2 * (csig1 * ssig2)) - (dn1 * (ssig1 * csig2)) - (csig1 * csig2 * j12);
    }

    /// <summary>
    /// Solves the astroid equation k^4 + 2k^3 - (x^2 + y^2 - 1)k^2 - 2y^2 k - y^2 = 0 for its positive root.
    /// </summary>
    private static double Astroid(double x, double y)
    {
        double p = x * x;
        double q = y * y;
        double r = (p + q - 1) / 6;
        if (q == 0 && r <= 0)
        {
            return 0;
        }

        double s = p * q / 4;
        double r2 = r * r;
        double r3 = r * r2;

        // The discriminant of the quadratic equation for T3, which is zero on the evolute curve p^(1/3) + q^(1/3) = 1.
        double disc = s * (s + (2 * r3));
        double u = r;
        if (disc >= 0)
        {
            // Pick the sign on the square root to maximize abs(T3), which minimizes loss of precision.
            double t3 = s + r3;
            t3 += t3 < 0 ? -Math.Sqrt(disc) : Math.Sqrt(disc);
            double t = Math.Cbrt(t3);
            u += t + (t != 0 ? r2 / t : 0);
        }
        else
        {
            // T is complex, but u is real. Pick the cube root that avoids cancellation.
            double ang = Math.Atan2(Math.Sqrt(-disc), -(s + r3));
            u += 2 * r * Math.Cos(ang / 3);
        }

        double v = Math.Sqrt((u * u) + q);
        double uv = u < 0 ? q / (v - u) : u + v;
        double w = (uv - q) / (2 * v);

        return uv / (Math.Sqrt(uv + (w * w)) + w);
    }

    /// <summary>
    /// Estimates the initial azimuth for Newton's method, or solves short lines directly.
    /// </summary>
    /// <returns>
    /// The central angle of a short line solved directly, which sets <paramref name="salp2"/>,
    /// <paramref name="calp2"/> and <paramref name="dnm"/>; otherwise -1.
    /// </returns>
    private static double InverseStart(
        double sbet1, double cbet1, double sbet2, double cbet2, double lam12, double slam12, double clam12,
        out double salp1, out double calp1, out double salp2, out double calp2, out double dnm)
    {
        double sig12 = -1;
        salp2 = 0;
        calp2 = 0;
        dnm = 0;

        // bet12 = bet2 - bet1 in [0, pi); bet12a = bet2 + bet1 in (-pi, 0].
        double sbet12 = (sbet2 * cbet1) - (cbet2 * sbet1);
        double cbet12 = (cbet2 * cbet1) + (sbet2 * sbet1);
        double sbet12a = (sbet2 * cbet1) + (cbet2 * sbet1);
        bool shortline = cbet12 >= 0 && sbet12 < 0.5 && cbet2 * lam12 < 0.5;

        double somg12;
        double comg12;
        if (shortline)
        {
            double sbetm2 = (sbet1 + sbet2) * (sbet1 + sbet2);
            sbetm2 /= sbetm2 + ((cbet1 + cbet2) * (cbet1 + cbet2));
            dnm = Math.Sqrt(1 + (Ep2 * sbetm2));
            double omg12 = lam12 / (F1 * dnm);
            somg12 = Math.Sin(omg12);
            comg12 = Math.Cos(omg12);
        }
        else
        {
            somg12 = slam12;
            comg12 = clam12;
        }

        salp1 = cbet2 * somg12;
        calp1 = comg12 >= 0
            ? sbet12 + (cbet2 * sbet1 * somg12 * somg12 / (1 + comg12))
            : sbet12a - (cbet2 * sbet1 * somg12 * somg12 / (1 - comg12));

        double ssig12 = double.Hypot(salp1, calp1);
        double csig12 = (sbet1 * sbet2) + (cbet1 * cbet2 * comg12);

        if (shortline && ssig12 < s_etol2)
        {
            salp2 = cbet1 * somg12;
            calp2 = sbet12 - (cbet1 * sbet2 * (comg12 >= 0 ? somg12 * somg12 / (1 + comg12) : 1 - comg12));
            Normalize(ref salp2, ref calp2);
            sig12 = Math.Atan2(ssig12, csig12);
        }
        else if (csig12 < 0 && ssig12 < 6 * ThirdFlattening * Math.PI * cbet1 * cbet1)
        {
            // The points are nearly antipodal, where the spherical approximation is poor. Scale lam12 and bet2 to
            // coordinates in which the antipodal point is at the origin and the singular point at (-1, 0).
            double lam12x = Math.Atan2(-slam12, -clam12);
            double k2 = sbet1 * sbet1 * Ep2;
            double eps = k2 / ((2 * (1 + Math.Sqrt(1 + k2))) + k2);
            double lamscale = Flattening * cbet1 * A3(eps) * Math.PI;
            double betscale = lamscale * cbet1;
            double x = lam12x / lamscale;
            double y = sbet12a / betscale;

            if (y > -Tol1 && x > -1 - Xthresh)
            {
                // Strip near the cut.
                salp1 = Math.Min(1.0, -x);
                calp1 = -Math.Sqrt(1 - (salp1 * salp1));
            }
            else
            {
                // Estimate omg12 from the astroid problem, then alp1 from the spherical formula.
                double k = Astroid(x, y);
                double omg12a = lamscale * (-x * k / (1 + k));
                somg12 = Math.Sin(omg12a);
                comg12 = -Math.Cos(omg12a);
                salp1 = cbet2 * somg12;
                calp1 = sbet12a - (cbet2 * sbet1 * somg12 * somg12 / (1 - comg12));
            }
        }

        // The reversed test lets NaNs through.
        if (!(salp1 <= 0))
        {
            Normalize(ref salp1, ref calp1);
        }
        else
        {
            salp1 = 1;
            calp1 = 0;
        }

        return sig12;
    }

    /// <summary>
    /// Computes the longitude difference reached by a geodesic that starts at azimuth alp1, relative to the target
    /// difference given by <paramref name="slam120"/> and <paramref name="clam120"/>, and optionally its derivative
    /// with respect to alp1.
    /// </summary>
    private static double Lambda12(
        double sbet1, double cbet1, double dn1, double sbet2, double cbet2, double dn2, double salp1, double calp1,
        double slam120, double clam120, out double salp2, out double calp2, out double sig12, out double ssig1,
        out double csig1, out double ssig2, out double csig2, out double eps, bool diffp, out double dlam12,
        Span<double> ca)
    {
        if (sbet1 == 0 && calp1 == 0)
        {
            // Break the degeneracy of the equatorial line, which has already been handled.
            calp1 = -Tiny;
        }

        double salp0 = salp1 * cbet1;
        double calp0 = double.Hypot(calp1, salp1 * sbet1);

        ssig1 = sbet1;
        double somg1 = salp0 * sbet1;
        csig1 = calp1 * cbet1;
        double comg1 = csig1;
        Normalize(ref ssig1, ref csig1);

        // Enforce symmetries where abs(bet2) = -bet1, which can otherwise make the iteration singular.
        salp2 = cbet2 != cbet1 ? salp0 / cbet2 : salp1;
        calp2 = cbet2 != cbet1 || Math.Abs(sbet2) != -sbet1
            ? Math.Sqrt((calp1 * cbet1 * calp1 * cbet1) + (cbet1 < -sbet1
                ? (cbet2 - cbet1) * (cbet1 + cbet2)
                : (sbet1 - sbet2) * (sbet1 + sbet2))) / cbet2
            : Math.Abs(calp1);

        ssig2 = sbet2;
        double somg2 = salp0 * sbet2;
        csig2 = calp2 * cbet2;
        double comg2 = csig2;
        Normalize(ref ssig2, ref csig2);

        // sig12 = sig2 - sig1 and omg12 = omg2 - omg1, both limited to [0, pi].
        sig12 = Math.Atan2(Math.Max(0.0, (csig1 * ssig2) - (ssig1 * csig2)) + 0, (csig1 * csig2) + (ssig1 * ssig2));
        double somg12 = Math.Max(0.0, (comg1 * somg2) - (somg1 * comg2)) + 0;
        double comg12 = (comg1 * comg2) + (somg1 * somg2);

        // eta = omg12 - lam120.
        double eta = Math.Atan2((somg12 * clam120) - (comg12 * slam120), (comg12 * clam120) + (somg12 * slam120));

        double k2 = calp0 * calp0 * Ep2;
        eps = k2 / ((2 * (1 + Math.Sqrt(1 + k2))) + k2);
        C3Coefficients(eps, ca);
        double b312 = SinCosSeries(true, ssig2, csig2, ca, SeriesOrder - 1)
            - SinCosSeries(true, ssig1, csig1, ca, SeriesOrder - 1);
        double domg12 = -Flattening * A3(eps) * salp0 * (sig12 + b312);
        double lam12 = eta + domg12;

        dlam12 = double.NaN;
        if (diffp)
        {
            if (calp2 == 0)
            {
                dlam12 = -2 * F1 * dn1 / sbet1;
            }
            else
            {
                Lengths(eps, sig12, ssig1, csig1, dn1, ssig2, csig2, dn2, false, out _, out dlam12, ca);
                dlam12 *= F1 / (calp2 * cbet2);
            }
        }

        return lam12;
    }

    /// <summary>Scales a sine and cosine pair so that the sum of their squares is one.</summary>
    internal static void Normalize(ref double sinx, ref double cosx)
    {
        double r = double.Hypot(sinx, cosx);
        sinx /= r;
        cosx /= r;
    }

    /// <summary>Reduces an angle in degrees to the range [-180, 180].</summary>
    internal static double NormalizeAngle(double x)
    {
        double y = Math.IEEERemainder(x, FullTurn);

        return Math.Abs(y) == HalfTurn ? Math.CopySign(HalfTurn, x) : y;
    }

    /// <summary>
    /// Computes y - x in degrees, reduced to [-180, 180], exactly as a rounded difference plus a rounding error.
    /// </summary>
    private static double AngleDifference(double x, double y, out double e)
    {
        double d = SumWithError(Math.IEEERemainder(-x, FullTurn), Math.IEEERemainder(y, FullTurn), out double t);

        // The second sum can only change d if abs(d) < 128, so the remainder is not needed again.
        d = SumWithError(Math.IEEERemainder(d, FullTurn), t, out t);

        // Fix the sign if d is -180, 0 or 180.
        if (d == 0 || Math.Abs(d) == HalfTurn)
        {
            d = Math.CopySign(d, t == 0 ? y - x : -t);
        }

        e = t;

        return d;
    }

    /// <summary>Adds two numbers exactly, as the rounded sum and the rounding error.</summary>
    private static double SumWithError(double u, double v, out double t)
    {
        double s = u + v;
        double up = s - v;
        double vpp = s - up;
        up -= u;
        vpp -= v;
        t = s != 0 ? 0 - (up + vpp) : s;

        return s;
    }

    /// <summary>Rounds an angle in degrees so that tiny values become exact multiples of the smallest step.</summary>
    internal static double RoundAngle(double x)
    {
        const double Z = 1.0 / 16.0;
        double y = Math.Abs(x);
        double w = Z - y;
        y = w > 0 ? Z - w : y;

        return Math.CopySign(y, x);
    }

    /// <summary>
    /// Computes the sine and cosine of an angle in degrees, reducing it exactly to [-45, 45] before converting it to
    /// radians to minimize rounding errors.
    /// </summary>
    internal static void SinCosDegrees(double x, out double sinx, out double cosx)
    {
        double r = RemainderQuarterTurns(x, out int quadrant);
        SinCosInQuadrant(r * Degree, quadrant, x, out sinx, out cosx);
    }

    /// <summary>Computes the sine and cosine of an angle in degrees given as a value and its rounding error.</summary>
    private static void SinCosDegrees(double x, double t, out double sinx, out double cosx)
    {
        double r = RoundAngle(RemainderQuarterTurns(x, out int quadrant) + t);
        SinCosInQuadrant(r * Degree, quadrant, x, out sinx, out cosx);
    }

    private static double RemainderQuarterTurns(double x, out int quadrant)
    {
        double r = Math.IEEERemainder(x, QuarterTurn);

        // Only the low two bits are needed, so reduce the quotient first to keep it within an int.
        quadrant = (int)(Math.Round((x - r) / QuarterTurn) % 4);

        return r;
    }

    private static void SinCosInQuadrant(double radians, int quadrant, double x, out double sinx, out double cosx)
    {
        double s = Math.Sin(radians);
        double c = Math.Cos(radians);
        (sinx, cosx) = (quadrant & 3) switch
        {
            0 => (s, c),
            1 => (c, -s),
            2 => (-s, -c),
            _ => (-c, s),
        };

        // Adding zero converts -0 to 0, and a zero sine takes the sign of the angle.
        cosx += 0;
        if (sinx == 0)
        {
            sinx = Math.CopySign(sinx, x);
        }
    }

    /// <summary>
    /// Computes atan2 in degrees, rearranging the arguments so that the underlying atan2 result is in [-45, 45] to
    /// minimize rounding errors.
    /// </summary>
    internal static double Atan2Degrees(double y, double x)
    {
        int q = 0;
        if (Math.Abs(y) > Math.Abs(x))
        {
            (x, y) = (y, x);
            q = 2;
        }

        if (double.IsNegative(x))
        {
            x = -x;
            q++;
        }

        double ang = Math.Atan2(y, x) / Degree;

        return q switch
        {
            1 => Math.CopySign(HalfTurn, y) - ang,
            2 => QuarterTurn - ang,
            3 => -QuarterTurn + ang,
            _ => ang,
        };
    }

    /// <summary>
    /// Evaluates the sum of c[i] sin(2ix) for i from 1 to n, or of c[i] cos((2i + 1)x) for i from 0 to n - 1, by
    /// Clenshaw summation.
    /// </summary>
    internal static double SinCosSeries(bool sinp, double sinx, double cosx, ReadOnlySpan<double> c, int n)
    {
        int k = n + (sinp ? 1 : 0);

        // 2 cos(2x)
        double ar = 2 * (cosx - sinx) * (cosx + sinx);
        double y0 = (n & 1) != 0 ? c[--k] : 0;
        double y1 = 0;

        for (int pairs = n / 2; pairs > 0; pairs--)
        {
            y1 = (ar * y0) - y1 + c[--k];
            y0 = (ar * y1) - y0 + c[--k];
        }

        return sinp ? 2 * sinx * cosx * y0 : cosx * (y0 - y1);
    }

    /// <summary>Evaluates a polynomial of the given order whose coefficients start with the highest power.</summary>
    private static double PolynomialValue(int order, ReadOnlySpan<double> p, double x)
    {
        double y = order < 0 ? 0 : p[0];
        for (int i = 1; i <= order; i++)
        {
            y = (y * x) + p[i];
        }

        return y;
    }

    /// <summary>Computes the scale factor A1 - 1, the mean value of (d/dsigma)I1 - 1.</summary>
    internal static double A1Minus1(double eps)
    {
        ReadOnlySpan<double> coefficients = [1, 4, 64, 0, 256];

        const int Order = SeriesOrder / 2;
        double t = PolynomialValue(Order, coefficients, eps * eps) / coefficients[Order + 1];

        return (t + eps) / (1 - eps);
    }

    /// <summary>Computes the coefficients C1[l] in the Fourier expansion of B1, into elements 1 to 6.</summary>
    internal static void C1Coefficients(double eps, Span<double> c)
    {
        ReadOnlySpan<double> coefficients =
        [
            -1, 6, -16, 32,
            -9, 64, -128, 2048,
            9, -16, 768,
            3, -5, 512,
            -7, 1280,
            -7, 2048,
        ];

        EvenSeriesCoefficients(coefficients, eps, c);
    }

    /// <summary>Computes the coefficients C1'[l] in the Fourier expansion of B1', into elements 1 to 6.</summary>
    internal static void C1PrimeCoefficients(double eps, Span<double> c)
    {
        ReadOnlySpan<double> coefficients =
        [
            205, -432, 768, 1536,
            4005, -4736, 3840, 12288,
            -225, 116, 384,
            -7173, 2695, 7680,
            3467, 7680,
            38081, 61440,
        ];

        EvenSeriesCoefficients(coefficients, eps, c);
    }

    /// <summary>Computes the scale factor A2 - 1, the mean value of (d/dsigma)I2 - 1.</summary>
    private static double A2Minus1(double eps)
    {
        ReadOnlySpan<double> coefficients = [-11, -28, -192, 0, 256];

        const int Order = SeriesOrder / 2;
        double t = PolynomialValue(Order, coefficients, eps * eps) / coefficients[Order + 1];

        return (t - eps) / (1 + eps);
    }

    /// <summary>Computes the coefficients C2[l] in the Fourier expansion of B2, into elements 1 to 6.</summary>
    private static void C2Coefficients(double eps, Span<double> c)
    {
        ReadOnlySpan<double> coefficients =
        [
            1, 2, 16, 32,
            35, 64, 384, 2048,
            15, 80, 768,
            7, 35, 512,
            63, 1280,
            77, 2048,
        ];

        EvenSeriesCoefficients(coefficients, eps, c);
    }

    /// <summary>
    /// Evaluates coefficients that are eps^l times a polynomial in eps^2, each polynomial stored with its highest
    /// power first and followed by a common denominator.
    /// </summary>
    private static void EvenSeriesCoefficients(ReadOnlySpan<double> coefficients, double eps, Span<double> c)
    {
        double eps2 = eps * eps;
        double d = eps;
        int o = 0;
        for (int l = 1; l <= SeriesOrder; l++)
        {
            int m = (SeriesOrder - l) / 2;
            c[l] = d * PolynomialValue(m, coefficients[o..], eps2) / coefficients[o + m + 1];
            o += m + 2;
            d *= eps;
        }
    }

    /// <summary>Computes the scale factor A3, the mean value of (d/dsigma)I3.</summary>
    internal static double A3(double eps)
    {
        return PolynomialValue(SeriesOrder - 1, s_a3x, eps);
    }

    /// <summary>Computes the coefficients C3[l] in the Fourier expansion of B3, into elements 1 to 5.</summary>
    internal static void C3Coefficients(double eps, Span<double> c)
    {
        double mult = 1;
        int o = 0;
        for (int l = 1; l < SeriesOrder; l++)
        {
            int m = SeriesOrder - l - 1;
            mult *= eps;
            c[l] = mult * PolynomialValue(m, s_c3x.AsSpan(o), eps);
            o += m + 1;
        }
    }

    private static double[] ComputeA3Coefficients()
    {
        ReadOnlySpan<double> coefficients =
        [
            -3, 128,
            -2, -3, 64,
            -1, -3, -1, 16,
            3, -1, -2, 8,
            1, -1, 2,
            1, 1,
        ];

        double[] a3x = new double[SeriesOrder];
        int o = 0;
        int k = 0;
        for (int j = SeriesOrder - 1; j >= 0; j--)
        {
            int m = Math.Min(SeriesOrder - j - 1, j);
            a3x[k++] = PolynomialValue(m, coefficients[o..], ThirdFlattening) / coefficients[o + m + 1];
            o += m + 2;
        }

        return a3x;
    }

    private static double[] ComputeC3Coefficients()
    {
        ReadOnlySpan<double> coefficients =
        [
            3, 128,
            2, 5, 128,
            -1, 3, 3, 64,
            -1, 0, 1, 8,
            -1, 1, 4,
            5, 256,
            1, 3, 128,
            -3, -2, 3, 64,
            1, -3, 2, 32,
            7, 512,
            -10, 9, 384,
            5, -9, 5, 192,
            7, 512,
            -14, 7, 512,
            21, 2560,
        ];

        double[] c3x = new double[SeriesOrder * (SeriesOrder - 1) / 2];
        int o = 0;
        int k = 0;
        for (int l = 1; l < SeriesOrder; l++)
        {
            for (int j = SeriesOrder - 1; j >= l; j--)
            {
                int m = Math.Min(SeriesOrder - j - 1, j);
                c3x[k++] = PolynomialValue(m, coefficients[o..], ThirdFlattening) / coefficients[o + m + 1];
                o += m + 2;
            }
        }

        return c3x;
    }
}
