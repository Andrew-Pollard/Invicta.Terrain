// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using Invicta.Elevation;

namespace Invicta;

/// <summary>Finds and downloads the data files that tests read.</summary>
internal static class TestData
{
    private static readonly HttpClient s_httpClient = new();

    /// <summary>Gets the <c>.cache</c> folder in the repository root, which Git ignores.</summary>
    public static string CacheDirectory { get; } = Path.Combine(FindRepositoryRoot(), ".cache");

    /// <summary>Gets a store that keeps Copernicus DEM tiles in the cache folder.</summary>
    public static CopernicusTileStore CopernicusTiles { get; } =
        new(Path.Combine(CacheDirectory, "copernicus"), s_httpClient);

    /// <summary>Gets the path of a cached file, downloading it first if it is not already in the cache.</summary>
    /// <param name="relativePath">The path of the file within the cache folder.</param>
    /// <param name="source">The address to download the file from.</param>
    /// <returns>The full path of the cached file.</returns>
    public static async Task<string> GetFileAsync(string relativePath, Uri source)
    {
        string path = Path.Combine(CacheDirectory, relativePath);
        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Download to a temporary name first, so an interrupted download never looks complete.
        string partialPath = path + ".partial";
        using (HttpResponseMessage response =
            await s_httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using FileStream file = File.Create(partialPath);
            await response.Content.CopyToAsync(file);
        }

        File.Move(partialPath, path, overwrite: true);

        return path;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Invicta.Terrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
