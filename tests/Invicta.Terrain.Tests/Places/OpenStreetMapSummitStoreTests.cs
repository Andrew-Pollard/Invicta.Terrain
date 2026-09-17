// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Invicta.Geodesy;

using NUnit.Framework;

namespace Invicta.Places;

/// <summary>Tests <see cref="OpenStreetMapSummitStore"/> against a fake Overpass API.</summary>
internal sealed class OpenStreetMapSummitStoreTests
{
    private string _directory = null!;

    [SetUp]
    public void ChooseUnusedDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Invicta.Terrain.Tests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task GetSummitsAsync_RegionAcrossCells_QueriesEachCellOnceAndFiltersToRegion()
    {
        using FakeOverpassApi api = new();
        using HttpClient client = new(api);
        OpenStreetMapSummitStore store = new(_directory, client, TimeSpan.Zero);
        GeoBoundingBox region = new(52, -8, 58, -3);

        IReadOnlyList<Summit> first = await store.GetSummitsAsync(region, CancellationToken.None);
        IReadOnlyList<Summit> second = await store.GetSummitsAsync(region, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(api.Queries, Is.EquivalentTo(["50,-10,55,-5", "50,-5,55,0", "55,-10,60,-5", "55,-5,60,0"]));
            Assert.That(first.Select(summit => summit.Name), Is.EquivalentTo(["Center 50,-10", "Center 55,-10"]));
            Assert.That(second, Is.EqualTo(first));
        }
    }

    [Test]
    public async Task GetSummitsAsync_ServerBusy_TriesAgain()
    {
        using FakeOverpassApi api = new() { BusyResponses = 2 };
        using HttpClient client = new(api);
        OpenStreetMapSummitStore store = new(_directory, client, TimeSpan.Zero);
        GeoBoundingBox region = new(52, -3, 53, -2);

        IReadOnlyList<Summit> summits = await store.GetSummitsAsync(region, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(api.Requests, Is.EqualTo(3));
            Assert.That(summits, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void GetSummitsAsync_ServerRejectsQuery_Throws()
    {
        using FakeOverpassApi api = new() { Rejects = true };
        using HttpClient client = new(api);
        OpenStreetMapSummitStore store = new(_directory, client, TimeSpan.Zero);

        Assert.That(
            () => store.GetSummitsAsync(new GeoBoundingBox(52, -3, 53, -2), CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
    }

    /// <summary>
    /// Answers Overpass queries with one summit at the center of each queried cell, recording the cells.
    /// </summary>
    private sealed class FakeOverpassApi : HttpMessageHandler
    {
        private readonly List<string> _queries = [];

        /// <summary>Gets or sets how many requests to answer as too busy before answering properly.</summary>
        public int BusyResponses { get; set; }

        /// <summary>Gets or sets a value indicating whether to reject every query as bad.</summary>
        public bool Rejects { get; set; }

        /// <summary>Gets the number of requests received.</summary>
        public int Requests { get; private set; }

        /// <summary>Gets the bounds of each query answered, as south, west, north, east.</summary>
        public IReadOnlyList<string> Queries => _queries;

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Rejects)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            if (BusyResponses > 0)
            {
                BusyResponses--;
                return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
            }

            string form = await request.Content!.ReadAsStringAsync(cancellationToken);
            string query = WebUtility.UrlDecode(form["data=".Length..]);
            string bounds = query[(query.LastIndexOf('(') + 1)..query.LastIndexOf(')')];
            _queries.Add(bounds);

            double[] edges =
                [.. bounds.Split(',').Select(edge => double.Parse(edge, CultureInfo.InvariantCulture))];
            var node = new
            {
                type = "node",
                id = 1,
                lat = (edges[0] + edges[2]) / 2,
                lon = (edges[1] + edges[3]) / 2,
                tags = new { name = string.Create(CultureInfo.InvariantCulture, $"Center {edges[0]},{edges[1]}") },
            };
            string json = JsonSerializer.Serialize(new { elements = new[] { node } });

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8) };
        }
    }
}
