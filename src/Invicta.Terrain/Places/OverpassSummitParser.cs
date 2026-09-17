// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;

using Invicta.Geodesy;

namespace Invicta.Places;

/// <summary>Reads summits from the JSON that the Overpass API returns for OpenStreetMap peak nodes.</summary>
internal static class OverpassSummitParser
{
    /// <summary>Reads the named summits from an Overpass response.</summary>
    /// <param name="json">The response, with elements that are nodes carrying tags.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The summits, skipping any node without a name.</returns>
    /// <exception cref="JsonException">The response is not valid JSON.</exception>
    public static async Task<List<Summit>> ParseAsync(Stream json, CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        List<Summit> summits = [];
        foreach (JsonElement element in document.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (TryReadSummit(element) is { } summit)
            {
                summits.Add(summit);
            }
        }

        return summits;
    }

    private static Summit? TryReadSummit(JsonElement element)
    {
        if (!element.TryGetProperty("tags", out JsonElement tags)
            || !tags.TryGetProperty("name", out JsonElement name)
            || string.IsNullOrWhiteSpace(name.GetString()))
        {
            return null;
        }

        GeoCoordinate coordinate = new(element.GetProperty("lat").GetDouble(), element.GetProperty("lon").GetDouble());

        return new Summit(name.GetString()!, coordinate, ReadMeters(tags, "ele"), ReadMeters(tags, "prominence"));
    }

    /// <summary>
    /// Reads a tag holding a length in meters, which mappers write as a plain number or with a trailing "m".
    /// Anything else, such as feet, is treated as missing rather than guessed at.
    /// </summary>
    private static double? ReadMeters(JsonElement tags, string key)
    {
        if (!tags.TryGetProperty(key, out JsonElement value) || value.GetString() is not string text)
        {
            return null;
        }

        string number = text.Trim();
        if (number.EndsWith('m'))
        {
            number = number[..^1].TrimEnd();
        }

        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double meters)
            && double.IsFinite(meters)
            ? meters
            : null;
    }
}
