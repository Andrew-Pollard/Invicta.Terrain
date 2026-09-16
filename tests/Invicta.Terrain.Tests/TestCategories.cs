// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

namespace Invicta;

/// <summary>Defines the names of the NUnit categories that tests are grouped into.</summary>
internal static class TestCategories
{
    /// <summary>
    /// Names the category of tests that download data on their first run, and so need internet access. Exclude them
    /// with <c>dotnet test --filter TestCategory!=DownloadedData</c>.
    /// </summary>
    public const string DownloadedData = nameof(DownloadedData);
}
