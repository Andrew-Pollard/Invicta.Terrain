// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using Invicta.Geodesy;
using Invicta.Rendering;

namespace Invicta;

/// <summary>Describes a flight: the way it goes, how fast, and how the camera flies it.</summary>
/// <remarks>
/// Routes are JSON files, so a new flight needs no code. The <c>Routes</c> folder beside the program holds the ones
/// this sample ships with.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Built by the JSON deserializer from a route file.")]
internal sealed record Route
{
    private static readonly JsonSerializerOptions s_fileFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new WaypointConverter() },
    };

    /// <summary>Gets the name of the flight, which each frame carries.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the points the flight passes through, in order, each a [latitude, longitude] pair.</summary>
    public required IReadOnlyList<GeoCoordinate> Waypoints { get; init; }

    /// <summary>Gets the speed in meters a second, which sets how many frames the flight takes.</summary>
    public required double Speed { get; init; }

    /// <summary>Gets the height in meters the camera keeps above the ground ahead of it.</summary>
    public required double Clearance { get; init; }

    /// <summary>Gets the distance in meters ahead of the camera that it climbs to clear.</summary>
    public required double LookAhead { get; init; }

    /// <summary>Gets the elevation angle in degrees at the top of the frame.</summary>
    public required double TopAngle { get; init; }

    /// <summary>Gets the distance in meters beyond which terrain is ignored.</summary>
    public required double ViewDistance { get; init; }

    /// <summary>Gets the distance in meters before the last waypoint at which the flight stops.</summary>
    public double StopShortOf { get; init; }

    /// <summary>Gets the credit for the OpenStreetMap data the flight uses.</summary>
    public string Credit { get; init; } = DataCredits.OpenStreetMap;

    /// <summary>Reads a route from a JSON file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The route.</returns>
    /// <exception cref="InvalidDataException">The file is empty, or a value in it is out of range.</exception>
    /// <exception cref="JsonException">The file is not a route.</exception>
    public static async Task<Route> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);
        Route route = await JsonSerializer.DeserializeAsync<Route>(file, s_fileFormat, cancellationToken)
            ?? throw new InvalidDataException($"{path} holds no route.");

        route.ThrowIfInvalid(path);

        return route;
    }

    private void ThrowIfInvalid(string path)
    {
        if (Waypoints.Count < 2)
        {
            throw new InvalidDataException($"The route in {path} needs at least two waypoints.");
        }

        bool positive = Speed > 0 && Clearance > 0 && LookAhead > 0 && ViewDistance > 0;
        if (!positive)
        {
            throw new InvalidDataException(
                $"The speed, clearance, look ahead and view distance in {path} must all be positive.");
        }

        if (!double.IsFinite(StopShortOf) || StopShortOf < 0)
        {
            throw new InvalidDataException($"The distance to stop short by in {path} must be zero or more.");
        }

        if (TopAngle is <= -90 or > 90)
        {
            throw new InvalidDataException($"The top angle in {path} must be from -90° to 90°.");
        }
    }

    /// <summary>Reads a waypoint written as a [latitude, longitude] pair.</summary>
    private sealed class WaypointConverter : JsonConverter<GeoCoordinate>
    {
        public override GeoCoordinate Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            double[] pair = JsonSerializer.Deserialize<double[]>(ref reader, options) ?? [];
            if (pair.Length != 2)
            {
                throw new JsonException("A waypoint must be a [latitude, longitude] pair.");
            }

            return new GeoCoordinate(pair[0], pair[1]);
        }

        public override void Write(Utf8JsonWriter writer, GeoCoordinate value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStartArray();
            writer.WriteNumberValue(value.Latitude);
            writer.WriteNumberValue(value.Longitude);
            writer.WriteEndArray();
        }
    }
}
