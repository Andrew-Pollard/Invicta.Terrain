// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Frozen;

namespace Invicta.Elevation;

/// <summary>
/// Downloads tiles of the Copernicus GLO-30 DEM from its public Amazon S3 bucket and keeps them in a folder, so each
/// is downloaded only once.
/// </summary>
/// <remarks>
/// The bucket has no tiles for open sea. The store downloads the bucket's list of tiles to tell sea apart from a
/// tile it has yet to fetch.
/// </remarks>
public sealed class CopernicusTileStore
{
    private static readonly Uri s_bucket = new("https://copernicus-dem-30m.s3.amazonaws.com/");

    private readonly string _directory;
    private readonly HttpClient _httpClient;

    // Replaced if a download fails, so the next request tries again. Two callers may both start a download, which
    // is harmless.
    private Task<FrozenSet<string>>? _tileNames;

    /// <summary>Initializes a new instance of the <see cref="CopernicusTileStore"/> class.</summary>
    /// <param name="directory">The folder to keep downloaded tiles in, which is created if it does not exist.</param>
    /// <param name="httpClient">The client to download tiles with.</param>
    public CopernicusTileStore(string directory, HttpClient httpClient)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(httpClient);

        _directory = directory;
        _httpClient = httpClient;
    }

    /// <summary>Gets the path of a tile, downloading it first if it is not already in the folder.</summary>
    /// <param name="latitude">The latitude of the tile's southern edge.</param>
    /// <param name="longitude">The longitude of the tile's western edge.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The path of the tile, or <see langword="null"/> if the area is open sea and has no tile.</returns>
    internal async Task<string?> GetTilePathAsync(int latitude, int longitude, CancellationToken cancellationToken)
    {
        string name = CopernicusGrid.TileName(latitude, longitude);
        string path = Path.Combine(_directory, name + ".tif");
        if (File.Exists(path))
        {
            return path;
        }

        FrozenSet<string> tileNames = await GetTileNamesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!tileNames.Contains(name))
        {
            return null;
        }

        await DownloadAsync(new Uri(s_bucket, $"{name}/{name}.tif"), path, cancellationToken).ConfigureAwait(false);

        return path;
    }

    private Task<FrozenSet<string>> GetTileNamesAsync()
    {
        Task<FrozenSet<string>>? tileNames = _tileNames;
        if (tileNames is null || tileNames.IsFaulted || tileNames.IsCanceled)
        {
            tileNames = LoadTileNamesAsync();
            _tileNames = tileNames;
        }

        return tileNames;
    }

    private async Task<FrozenSet<string>> LoadTileNamesAsync()
    {
        string path = Path.Combine(_directory, "tileList.txt");
        if (!File.Exists(path))
        {
            await DownloadAsync(new Uri(s_bucket, "tileList.txt"), path, CancellationToken.None).ConfigureAwait(false);
        }

        string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);

        return lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToFrozenSet(StringComparer.Ordinal);
    }

    private async Task DownloadAsync(Uri source, string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);

        // Download to a unique temporary name, so an interrupted download never looks complete and concurrent
        // downloads of the same file don't collide.
        string partialPath = $"{path}.{Guid.NewGuid():N}.partial";
        try
        {
            using (HttpResponseMessage response = await _httpClient
                .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                FileStream file = File.Create(partialPath);
                await using (file.ConfigureAwait(false))
                {
                    await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(partialPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(partialPath);
        }
    }
}
