# Invicta.Terrain

What can you see from the top of a hill? Invicta.Terrain answers from real elevation data: it renders the 360° view
from any point with the visible summits named, draws the profile of a line of sight between two points, maps the
ground visible from a point, and searches a region for its longest lines of sight.

![The view north-east from Ben Nevis, with the Cairngorms on the horizon][ben-nevis]

Elevations come from the [Copernicus GLO-30 DEM][copernicus], 30 m data covering the world, downloaded tile by tile
from its public bucket. Summit names come from [OpenStreetMap][osm] through the [Overpass API][overpass]. Both are
downloaded once and kept in a cache folder.

Requires .NET 10.

## Command-line tool

`src/Invicta.Terrain.CommandLine` builds `invicta-terrain`. Each command downloads what it needs on first use, into
`%LOCALAPPDATA%\Invicta.Terrain` unless `--cache` says otherwise.

- **`panorama`:** renders the 360° view from a point as a PNG, with visible summits labelled. From Ben Nevis, to 450 km
  at 7,200 × 400 pixels, it takes 2.4 s once the data is cached.
- **`profile`:** traces the line of sight between two points and draws it as a cross-section.
- **`viewshed`:** maps the ground visible from a point.

```bash
invicta-terrain panorama --lat 56.79685 --lon -5.00351 -o ben-nevis.png
```

```bash
invicta-terrain profile --from-lat 55.13901 --from-lon -4.46821 --to-lat 53.06864 --to-lon -4.07626 -o profile.png
```

```bash
invicta-terrain viewshed --lat 56.79685 --lon -5.00351 --radius 60 --resolution 60 -o viewshed.png
```

## Library

`src/Invicta.Terrain` holds everything the tool does, in four namespaces.

- **`Invicta.Geodesy`:** distances, azimuths and positions on the WGS 84 ellipsoid, ported from
  [GeographicLib][geographiclib] and accurate to 15 nm.
- **`Invicta.Elevation`:** Copernicus tiles and their overviews, interpolated bilinearly across tile edges.
- **`Invicta.Visibility`:** lines of sight, panoramas, viewsheds and profiles.
- **`Invicta.Rendering`:** PNG images of those, drawn with [SkiaSharp][skiasharp].

```csharp
using HttpClient httpClient = new();
CopernicusTileStore tiles = new(cacheDirectory, httpClient);

GeoCoordinate benNevis = new(56.79685, -5.00351);
GeoCoordinate benMacdui = new(57.07039, -3.66913);
CopernicusElevationModel terrain = await CopernicusElevationModel.LoadAsync(
    tiles, new GeoBoundingBox(56.7, -5.1, 57.2, -3.6), overviewLevel: 0, cancellationToken);

// Can someone on Ben Nevis see someone standing on Ben Macdui, 87 km away?
Viewpoint viewpoint = Viewpoint.AboveTerrain(terrain, benNevis, heightAboveTerrain: 2);
LineOfSightResult result = LineOfSight.Trace(
    terrain, viewpoint, benMacdui, terrain.GetElevation(benMacdui) + 2, sampleSpacing: 15);
```

## How it works

- **Geometry:** elevation angles come from the Earth-centred positions of both points on the WGS 84 ellipsoid, so the
  Earth's curvature is exact in every direction rather than the usual d²/2R approximation.
- **Refraction:** air bends light down around the Earth, raising a point at distance d by kd/2R. The coefficient k is
  0.13 by default, the value used in geodetic surveying; in practice it varies widely with the weather and the time of
  day, and is largest in the cold, still air around dawn.
- **Panoramas:** each column marches outward along its azimuth, filling pixels from the bottom up as terrain rises into
  view, with steps that grow with distance. Beyond a few tens of kilometres it reads the tiles' averaged overviews,
  which are fine enough for a pixel's width and a fraction of the memory.
- **Labels:** a summit is labelled when the pixels where it would appear show terrain at its distance, so the labels
  agree with the picture.

## A 443 km line of sight

At dawn on 16 July 2016, Marc Bret of the [Beyond Horizons][beyond-horizons] team photographed Pic Gaspard in the French
Alps from Pic de Finestrelles in the Pyrenees, 443 km away, crediting favourable refraction. The line skims the curve of
the Earth across the Gulf of Lion, so whether it clears does depend on refraction.
`RecordLineOfSightTests` reproduces it:

| Refraction coefficient | Result |
|---|---|
| 0.10 to 0.13 | Blocked 167 km out by a 122 m hill near Agde |
| 0.14 to 0.15 | Blocked 229 km out by 82 m ground near Nîmes |
| 0.16 and above | Visible |

![The line of sight from Pic de Finestrelles to Pic Gaspard, crossing the Gulf of Lion][profile]

## Longest lines of sight in Great Britain and Ireland

`samples/Invicta.Terrain.LongestSightLines` searches every pair of named summits of at least 300 m in Great Britain,
Ireland and the Isle of Man, 11,746 summits in all. It keeps the 5 million pairs over 150 km apart whose horizons
could meet, traces each at full resolution between people standing on the two summits, and finds the least refraction
coefficient at which they can see each other. The search takes 12 minutes on 16 threads. The tables leave out lines
whose ends are both within 10 km of a longer line's ends, which are usually the same view.

With standard refraction, the longest is Merrick in Galloway to Yr Wyddfa (Snowdon), at 231.9 km, which agrees with
the [232 km line between them][merrick-wikipedia] long cited as the longest in the British Isles. It needs a
refraction coefficient of at least 0.094; with less, the curve of the Irish Sea itself blocks it, just off the east
coast of the Isle of Man:

| Distance | From | To | Least refraction coefficient |
|---:|---|---|---:|
| 231.9 km | Merrick (841 m) | Yr Wyddfa (1,073 m) | 0.094 |
| 223.8 km | Carnedd Dafydd (1,041 m) | Merrick (841 m) | 0.042 |
| 221.5 km | Millfore (651 m) | Yr Wyddfa (1,073 m) | 0.116 |
| 217.9 km | Sawel (677 m) | Sron An Isean (959 m) | 0.124 |
| 217.7 km | Cross Fell (892 m) | Moel Llyfnant (744 m) | 0.121 |
| 215.5 km | Cadair Idris (885 m) | Slieve Bearnagh (719 m) | 0.119 |
| 214.9 km | Great Dun Fell (846 m) | Moelwyn Mawr (764 m) | 0.130 |
| 213.2 km | Carnedd Dafydd (1,041 m) | Millfore (651 m) | 0.061 |

![The line of sight from Merrick to Yr Wyddfa, across the Irish Sea][merrick]

Heights are the terrain model's, at the highest point within 60 m of each mapped summit. With stronger refraction,
such as in the cold, still air around dawn, the lines grow to over 250 km, and more cross between Ireland and
Scotland:

| Distance | From | To | Least refraction coefficient |
|---:|---|---|---:|
| 255.1 km | Ben Cruachan (1,110 m) | Slieve Donard (846 m) | 0.236 |
| 243.7 km | Cairnsmore of Carsphairn (795 m) | Yr Wyddfa (1,073 m) | 0.201 |
| 243.1 km | An Earagail (729 m) | Ben Cruachan (1,110 m) | 0.212 |
| 242.2 km | Blackcraig Hill (698 m) | Carnedd Llewelyn (1,059 m) | 0.250 |
| 241.9 km | Ballencleuch Law (689 m) | Carnedd Llewelyn (1,059 m) | 0.247 |

![The line of sight from Ben Cruachan to Slieve Donard][cruachan]

## Viewsheds

A viewshed is the ground that can be seen from a point. The map below shades orange everything within 60 km that
someone standing on Ben Nevis could see, over hillshaded terrain with the sea and lochs in blue.

![The ground visible from Ben Nevis, within 60 km][viewshed]

- **Reading the map:** Ben Nevis is at the centre, marked by the small circle, with north up. The map uses the
  azimuthal equidistant projection, so the distance and direction of every point from the centre are true, and the
  scale bar measures distances from the centre exactly.
- **What it shows:** most visible ground is on slopes facing Ben Nevis. The long orange strips are where it looks
  straight along a valley, such as the Great Glen to the north-east and Loch Linnhe to the south-west. Little of the
  mountain's own flanks is visible from the top, because they fall away beyond the edge of the summit plateau.
- **How it is computed:** rays leave the centre so close together that neighbouring rays are one pixel apart at the
  edge of the map, 6,284 of them here. Each ray samples the terrain every half pixel, and a point counts as visible
  when its apparent angle, with the Earth's curvature and refraction, reaches the highest angle of anything nearer.
  At 60 m per pixel that is 12.6 million samples, which take about 0.6 s, and painting the map a further 0.7 s.
- **Targets above the ground:** `--target-height` maps where something standing on the ground could be seen, such
  as a person at 2 m or a mast, rather than the ground itself. A target is seen over the terrain in front of it,
  but does not hide anything behind it.

## Flyovers

`samples/Invicta.Terrain.Flyover` renders the frames of a flight along a route, each frame the view ahead from a
moving camera. A panorama can span less than the full circle, so a frame is a 60° view 1,280 pixels wide, a sixth of
the work of a whole turn at the same resolution. A frame takes about 60 ms once the data is cached, so a flight of
1,200 frames renders in under two minutes.

```bash
invicta-terrain-flyover Routes/grand-canyon.json .cache frames
```

![The Matterhorn at the end of the flight, with Monte Rosa behind it][matterhorn]

- **The routes:** a route is a JSON file of waypoints and the camera's settings, so a new flight needs no code. Two
  come with the sample: `alps.json` flies 55 km from Chamonix to the Matterhorn, and `grand-canyon.json` follows
  39 km of the Colorado River through the Grand Canyon, its waypoints taken from OpenStreetMap.
- **The camera:** it stays the route's clearance above the highest ground within its look-ahead distance, averaged
  across neighbouring frames so that it climbs and turns gradually. Over the Alps that is 900 m above the ground
  7 km ahead, which carries it over the ridges; down the Colorado it is 350 m above the ground 1.6 km ahead, which
  keeps it between the walls of the gorge instead of climbing over the rim.
- **The summits:** each frame names the most prominent summits it shows, leaving out any whose label would overlap
  one already placed. Naming them per frame makes the labels flicker a little as summits come in and out of view.
- **The video:** the sample writes JPEG frames for a video tool to assemble.

```bash
ffmpeg -framerate 60 -i frame%05d.jpg -c:v libx264 -crf 20 -pix_fmt yuv420p flyover.mp4
```

![Flying down the Colorado between the walls of the Grand Canyon][canyon]

## Accuracy and limitations

- **Surface model:** GLO-30 measures the surface, including forests and buildings, and smooths sharp summits: it puts
  Ben Nevis at 1,343 m against a surveyed 1,345 m, but Pic Gaspard at 3,785 m against 3,883 m.
- **Refraction:** the largest uncertainty in any long line of sight, as the table above shows.
- **Heights:** the data's heights above the EGM2008 geoid are treated as heights above the ellipsoid. Over a view the
  difference changes by a few metres, which moves angles far less than refraction does.
- **Summits:** only named `natural=peak` nodes in OpenStreetMap are labelled.

## Tests

`tests/Invicta.Terrain.Tests` checks the geometry against independent answers: GeographicLib's test set of 10,000
geodesics, a sphere with the ellipsoid's curvature, horizon distances over a smooth sea, and conical hills. Tests in the
`DownloadedData` category download data on first run; exclude them with `--filter TestCategory!=DownloadedData`.

## Licence

Released under the [MIT License][license]. The notices for the projects this repository draws on are in
[THIRD-PARTY-NOTICES.md][notices].

Images made from the elevation data carry the notice its licence requires: produced using Copernicus WorldDEM-30 © DLR
e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA;
all rights reserved. The organisations in charge of the Copernicus programme by law or by delegation do not incur any
liability for any use of the Copernicus WorldDEM-30. Summit names are © OpenStreetMap contributors, available under the
Open Database License.

[ben-nevis]: docs/images/ben-nevis-north-east.png
[profile]: docs/images/finestrelles-to-gaspard.png
[viewshed]: docs/images/ben-nevis-viewshed.png
[merrick]: docs/images/merrick-to-yr-wyddfa.png
[merrick-wikipedia]: https://en.wikipedia.org/wiki/Merrick_(Galloway)
[cruachan]: docs/images/ben-cruachan-to-slieve-donard.png
[matterhorn]: docs/images/chamonix-to-matterhorn.jpg
[canyon]: docs/images/grand-canyon.jpg
[copernicus]: https://registry.opendata.aws/copernicus-dem/
[osm]: https://www.openstreetmap.org/copyright
[overpass]: https://wiki.openstreetmap.org/wiki/Overpass_API
[geographiclib]: https://geographiclib.sourceforge.io/
[skiasharp]: https://github.com/mono/SkiaSharp
[beyond-horizons]: https://beyondrange.wordpress.com/2016/08/03/pic-de-finestrelles-pic-gaspard-ecrins-443-km/
[license]: LICENSE
[notices]: THIRD-PARTY-NOTICES.md
