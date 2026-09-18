// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

using Invicta.Geodesy;

namespace Invicta.Places;

/// <summary>
/// Finds named summits in OpenStreetMap through the public Overpass API, keeping each response in a folder so that
/// a region is only queried once.
/// </summary>
/// <remarks>
/// OpenStreetMap data is available under the Open Database License, which requires credit to "© OpenStreetMap
/// contributors" wherever the summits are shown. Summits are queried in cells of 5° of latitude and longitude, one
/// request at a time, to keep within the Overpass API's usage policy.
/// </remarks>
public sealed class OpenStreetMapSummitStore
{
    private const int CellSize = 5;

    // The public server often answers "too busy" for a while, so back off for one delay, then two, and so on.
    private const int MaxQueryAttempts = 5;

    private static readonly Uri s_overpassApi = new("https://overpass-api.de/api/interpreter");

    private readonly string _directory;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _retryDelay;

    /// <summary>Initializes a new instance of the <see cref="OpenStreetMapSummitStore"/> class.</summary>
    /// <param name="directory">The folder to keep responses in, which is created if it does not exist.</param>
    /// <param name="httpClient">The client to query the Overpass API with.</param>
    public OpenStreetMapSummitStore(string directory, HttpClient httpClient)
        : this(directory, httpClient, TimeSpan.FromSeconds(15))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OpenStreetMapSummitStore"/> class.</summary>
    /// <param name="directory">The folder to keep responses in, which is created if it does not exist.</param>
    /// <param name="httpClient">The client to query the Overpass API with.</param>
    /// <param name="retryDelay">The first delay before trying a query again, which tests shorten.</param>
    internal OpenStreetMapSummitStore(string directory, HttpClient httpClient, TimeSpan retryDelay)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        ArgumentNullException.ThrowIfNull(httpClient);

        _directory = directory;
        _httpClient = httpClient;
        _retryDelay = retryDelay;
    }

    /// <summary>Gets the named summits in a region.</summary>
    /// <param name="region">The region.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The summits.</returns>
    /// <exception cref="HttpRequestException">The Overpass API could not be queried.</exception>
    public async Task<IReadOnlyList<Summit>> GetSummitsAsync(GeoBoundingBox region, CancellationToken cancellationToken)
    {
        List<Summit> summits = [];
        foreach ((int south, int west) in CellsCovering(region))
        {
            string path = await GetCellPathAsync(south, west, cancellationToken).ConfigureAwait(false);

            FileStream file = File.OpenRead(path);
            await using (file.ConfigureAwait(false))
            {
                List<Summit> cellSummits = await OverpassSummitParser.ParseAsync(file, cancellationToken)
                    .ConfigureAwait(false);
                summits.AddRange(cellSummits.Where(summit => Contains(region, summit.Coordinate)));
            }
        }

        return summits;
    }

    private static IEnumerable<(int South, int West)> CellsCovering(GeoBoundingBox region)
    {
        int firstSouth = FloorToCell(region.South);
        int lastSouth = int.Min(FloorToCell(region.North), 90 - CellSize);
        int firstWest = FloorToCell(region.West);

        // A region wider than the world would otherwise list some cells twice.
        int lastWest = int.Min(FloorToCell(region.East), firstWest + 360 - CellSize);

        for (int south = firstSouth; south <= lastSouth; south += CellSize)
        {
            for (int west = firstWest; west <= lastWest; west += CellSize)
            {
                yield return (south, NormalizeCellLongitude(west));
            }
        }
    }

    private static int FloorToCell(double degrees)
    {
        return (int)double.Floor(degrees / CellSize) * CellSize;
    }

    private static int NormalizeCellLongitude(int west)
    {
        return (int)Mod(west + 180, 360) - 180;
    }

    private async Task<string> GetCellPathAsync(int south, int west, CancellationToken cancellationToken)
    {
        string path = Path.Combine(
            _directory, string.Create(CultureInfo.InvariantCulture, $"peaks_{south}_{west}.json"));
        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(_directory);
        string bounds = string.Create(
            CultureInfo.InvariantCulture, $"{south},{west},{south + CellSize},{west + CellSize}");
        string query = $"[out:json][timeout:300];node[\"natural\"=\"peak\"][\"name\"]({bounds});out body;";

        using HttpResponseMessage response = await QueryAsync(query, cancellationToken).ConfigureAwait(false);

        string partialPath = $"{path}.{Guid.NewGuid():N}.partial";
        try
        {
            FileStream file = File.Create(partialPath);
            await using (file.ConfigureAwait(false))
            {
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partialPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(partialPath);
        }

        return path;
    }

    /// <summary>
    /// Sends a query to the Overpass API, waiting and trying again while the server reports that it is too busy.
    /// </summary>
    private async Task<HttpResponseMessage> QueryAsync(string query, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, s_overpassApi)
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]),
            };
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Invicta.Terrain", "1.0"));

            HttpResponseMessage response =
                await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            bool busy = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!busy || attempt == MaxQueryAttempts)
            {
                using (response)
                {
                    response.EnsureSuccessStatusCode();
                }
            }

            response.Dispose();
            await Task.Delay(_retryDelay * attempt, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool Contains(GeoBoundingBox region, GeoCoordinate coordinate)
    {
        double longitude = region.West + Mod(coordinate.Longitude - region.West, 360);

        return coordinate.Latitude >= region.South
            && coordinate.Latitude <= region.North
            && longitude <= region.East;
    }

    private static double Mod(double value, double divisor)
    {
        double remainder = value % divisor;

        return remainder < 0 ? remainder + divisor : remainder;
    }
}
