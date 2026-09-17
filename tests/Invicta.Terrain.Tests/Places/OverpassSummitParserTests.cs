// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.Text;

using NUnit.Framework;

namespace Invicta.Places;

/// <summary>Tests <see cref="OverpassSummitParser"/>.</summary>
internal sealed class OverpassSummitParserTests
{
    private const string Response = """
        {
          "version": 0.6,
          "elements": [
            { "type": "node", "id": 1, "lat": 56.7968582, "lon": -5.0035260,
              "tags": { "natural": "peak", "name": "Ben Nevis", "ele": "1345", "prominence": "1345" } },
            { "type": "node", "id": 2, "lat": 56.8052539, "lon": -4.9866573,
              "tags": { "natural": "peak", "name": "Càrn Mòr Dearg", "ele": "1220 m" } },
            { "type": "node", "id": 3, "lat": 56.9, "lon": -5.1,
              "tags": { "natural": "peak", "name": "Imperial", "ele": "3,000 ft" } },
            { "type": "node", "id": 4, "lat": 56.9, "lon": -5.2,
              "tags": { "natural": "peak", "ele": "500" } }
          ]
        }
        """;

    private List<Summit> _summits = [];

    [OneTimeSetUp]
    public async Task Parse()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(Response));

        _summits = await OverpassSummitParser.ParseAsync(stream, CancellationToken.None);
    }

    [Test]
    public void ParseAsync_NodeWithoutName_IsSkipped()
    {
        Assert.That(_summits.Select(summit => summit.Name), Is.EqualTo(["Ben Nevis", "Càrn Mòr Dearg", "Imperial"]));
    }

    [Test]
    public void ParseAsync_PlainNumbers_AreReadAsMeters()
    {
        Summit benNevis = _summits[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(benNevis.Coordinate.Latitude, Is.EqualTo(56.7968582));
            Assert.That(benNevis.Coordinate.Longitude, Is.EqualTo(-5.0035260));
            Assert.That(benNevis.Elevation, Is.EqualTo(1345));
            Assert.That(benNevis.Prominence, Is.EqualTo(1345));
        }
    }

    [Test]
    public void ParseAsync_NumberWithMeterSuffix_IsReadAsMeters()
    {
        Assert.That(_summits[1].Elevation, Is.EqualTo(1220));
    }

    [Test]
    public void ParseAsync_OtherUnitsOrMissing_AreNull()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_summits[2].Elevation, Is.Null);
            Assert.That(_summits[1].Prominence, Is.Null);
        }
    }
}
