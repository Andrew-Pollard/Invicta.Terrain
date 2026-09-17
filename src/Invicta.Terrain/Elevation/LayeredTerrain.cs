// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta.Elevation;

/// <summary>
/// Represents terrain seen from a point, in layers of decreasing detail with distance, so that distant terrain takes
/// less memory and is sampled no more finely than it can be seen.
/// </summary>
public sealed class LayeredTerrain
{
    private readonly TerrainLayer[] _layers;

    /// <summary>Initializes a new instance of the <see cref="LayeredTerrain"/> class.</summary>
    /// <param name="layers">The layers, ordered from nearest to furthest.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="layers"/> is empty, or its maximum distances do not increase.
    /// </exception>
    public LayeredTerrain(IEnumerable<TerrainLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        _layers = [.. layers];
        if (_layers.Length == 0)
        {
            throw new ArgumentException("There must be at least one layer.", nameof(layers));
        }

        for (int i = 1; i < _layers.Length; i++)
        {
            if (_layers[i].MaximumDistance <= _layers[i - 1].MaximumDistance)
            {
                throw new ArgumentException("The layers' maximum distances must increase.", nameof(layers));
            }
        }
    }

    /// <summary>Gets the distance in meters that the furthest layer reaches.</summary>
    public double MaximumDistance => _layers[^1].MaximumDistance;

    /// <summary>Creates terrain of a single layer.</summary>
    /// <param name="model">The elevation model.</param>
    /// <param name="maximumDistance">The distance in meters that the model covers.</param>
    /// <returns>The terrain.</returns>
    public static LayeredTerrain FromModel(IElevationModel model, double maximumDistance)
    {
        return new LayeredTerrain([new TerrainLayer(maximumDistance, model)]);
    }

    /// <summary>Gets the elevation model for terrain at a distance.</summary>
    /// <param name="distance">The distance in meters.</param>
    /// <returns>The model of the nearest layer that reaches the distance, or of the furthest layer.</returns>
    public IElevationModel GetModel(double distance)
    {
        foreach (TerrainLayer layer in _layers)
        {
            if (distance <= layer.MaximumDistance)
            {
                return layer.Model;
            }
        }

        return _layers[^1].Model;
    }
}
