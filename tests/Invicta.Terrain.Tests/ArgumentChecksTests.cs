// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using NUnit.Framework;

namespace Invicta;

/// <summary>Tests <see cref="ArgumentChecks"/>.</summary>
internal sealed class ArgumentChecksTests
{
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void ThrowIfNotFinite_NotFinite_ThrowsNamingTheArgument(double value)
    {
        Assert.That(
            () => ArgumentChecks.ThrowIfNotFinite(value),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("value"));
    }

    [TestCase(double.MinValue)]
    [TestCase(0.0)]
    [TestCase(double.MaxValue)]
    public void ThrowIfNotFinite_Finite_DoesNotThrow(double value)
    {
        Assert.That(() => ArgumentChecks.ThrowIfNotFinite(value), Throws.Nothing);
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(0.0)]
    [TestCase(-1e-300)]
    public void ThrowIfNotPositiveAndFinite_NotPositiveAndFinite_Throws(double value)
    {
        Assert.That(
            () => ArgumentChecks.ThrowIfNotPositiveAndFinite(value), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(double.Epsilon)]
    [TestCase(double.MaxValue)]
    public void ThrowIfNotPositiveAndFinite_PositiveAndFinite_DoesNotThrow(double value)
    {
        Assert.That(() => ArgumentChecks.ThrowIfNotPositiveAndFinite(value), Throws.Nothing);
    }
}
