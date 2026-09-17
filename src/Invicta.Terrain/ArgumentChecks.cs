// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Invicta;

/// <summary>
/// Validates floating-point arguments where <see cref="ArgumentOutOfRangeException"/>'s own throw helpers stop short:
/// they let infinity through, and NaN depending on its sign bit.
/// </summary>
internal static class ArgumentChecks
{
    /// <summary>Throws if a value is NaN or infinite.</summary>
    /// <param name="value">The argument.</param>
    /// <param name="paramName">The argument's name, which the compiler supplies.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite.</exception>
    public static void ThrowIfNotFinite(
        double value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be finite.");
        }
    }

    /// <summary>Throws if a value is not greater than zero, or is NaN or infinite.</summary>
    /// <param name="value">The argument.</param>
    /// <param name="paramName">The argument's name, which the compiler supplies.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not positive and finite.</exception>
    public static void ThrowIfNotPositiveAndFinite(
        double value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "The value must be positive and finite.");
        }
    }
}
