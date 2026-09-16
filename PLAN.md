# Plan: Invicta.Terrain

A C# library and command-line tool that answers the hill walker's question "what am I looking at?". Given a place
on a map, it renders a 360° panorama of the real terrain with each visible summit labelled, and it can map where a
place is visible from, draw the profile of the sight line between two points, and search a whole region for its
longest lines of sight.

## Why this project

- **Concrete:** the output is a picture of a real view, and anyone who has stood on a summit can judge it.
- **Applied maths:** geodesy on the WGS 84 ellipsoid, the Earth's curvature, atmospheric refraction, interpolation
  of elevation grids and ray marching. Each part has a known right answer to test against.
- **Useful:** apps such as PeakFinder do this commercially. An open .NET library can also answer questions they
  don't, such as where a summit can be seen from.
- **Uses the machine:** one panorama is light work, but a viewshed over a 400 km radius, or a search of every summit
  in Great Britain and Ireland, needs all 16 threads and could use the GPU.

## Data

- **Elevation:** the [Copernicus GLO-30 DEM][copernicus]: 30 m, worldwide, free, in 1° Cloud Optimized GeoTIFF
  tiles on AWS that can be downloaded without an account, which I checked on a Scottish tile. It is a surface model,
  so it includes forests and buildings; that matters little for distant views and the README will say so.
- **Summit names:** [OpenStreetMap][osm] `natural=peak` nodes, fetched by region with a few polite Overpass queries
  and cached locally, with the attribution the ODbL requires.
- **Nothing committed:** tiles and peak lists download on demand into an ignored cache folder, recorded in a manifest
  with SHA-256 hashes.

## Goals

1. **Correct geometry:** geodesic calculations match GeographicLib's published test set, and visibility accounts for
   the Earth's curvature and refraction, verified against analytic cases and published real-world sight lines, such
   as the photographed 443 km view from Pic de Finestrelles in the Pyrenees to Pic Gaspard in the Alps.
2. **Real terrain at full resolution:** tiles are read on demand and cached, sight lines extend to at least 450 km,
   and a panorama from any summit in Great Britain renders in a few seconds, with the baseline measured and improved.
3. **Useful outputs:** a command-line tool that produces labelled panoramas, sight-line profiles and viewshed maps as
   PNG images, and identifies the summit at a given bearing.
4. **A real answer:** the longest lines of sight in Great Britain and Ireland, found by an exhaustive search and
   published in the README with their panoramas and profiles.
5. **Polished:** idiomatic .NET under the rules in `claude/`, a README in the style of the Configuration repository,
   and the refinement loop repeated until a whole pass finds nothing.

## Layout

| Path                                        | Contents                                                        |
|---------------------------------------------|-----------------------------------------------------------------|
| `src/Invicta.Terrain`                       | Geodesy, elevation tiles, visibility and rendering              |
| `src/Invicta.Terrain.CommandLine`           | The command-line tool                                           |
| `tests/Invicta.Terrain.Tests`               | Unit, analytic and real-terrain tests                           |
| `benchmarks/Invicta.Terrain.Benchmarks`     | Benchmarks for sampling, ray marching and rendering             |
| `samples/Invicta.Terrain.LongestSightLines` | The regional search for the longest lines of sight              |

## How I will meet the goals

### 1. Geodesy

- **Operations:** distance and initial bearing between two points, and the point at a distance along a bearing, on
  the WGS 84 ellipsoid, using Karney's algorithms.
- **Verification:** GeographicLib's geodesic test set, filtered to distances under 1,000 km.
- **Viewing geometry:** conversion to local east-north-up coordinates, so that elevation angles include curvature
  exactly rather than through the usual `d² / 2R` approximation, which I will test against the exact form.

### 2. Elevation data

- **Reading:** GeoTIFF tiles with their compression and predictor, and bilinear interpolation between cells.
- **Caching:** downloaded tiles on disk, decoded tiles in memory with a bounded least-recently-used cache.
- **Missing tiles:** treated as sea level, as the dataset documents for ocean areas.
- **Verification:** spot heights of well-surveyed summits, such as Ben Nevis and Snowdon, within the dataset's stated
  vertical accuracy.

### 3. Visibility

- **Refraction:** the standard effective-radius model with a configurable coefficient, defaulting to 0.13.
- **Horizon per azimuth:** march outward along each azimuth with steps that grow with distance, keeping the highest
  elevation angle so far, which is both the occlusion test and the silhouette.
- **Analytic tests:** a smooth ellipsoid gives the textbook horizon distance, and synthetic cones and ridges give
  known hidden and visible regions.
- **Consistency tests:** if A can see B, B can see A, checked over many random pairs of real terrain points.
- **Real-world tests:** published long sight lines are visible, and pairs known to be blocked are not.

### 4. Rendering

- **Panorama:** terrain shaded by distance and slope, with ridge lines drawn where the distance jumps.
- **Labels:** each summit whose elevation angle clears the horizon in front of it, placed without overlapping.
- **Profiles and viewsheds:** a cross-section with the sight line and curvature drawn in, and a map shading every
  cell visible from a point.

### 5. Performance

- **Measure first:** BenchmarkDotNet for sampling and ray marching, and end-to-end timings for panoramas and
  viewsheds, recorded in commit messages.
- **CPU:** columns in parallel, allocation-free sampling, and SIMD where measurements justify it.
- **GPU:** only if the regional search is still too slow on 16 threads, compared with the CPU on the same inputs.

### 6. The longest lines of sight

- **Candidates:** local maxima of the elevation grid with enough prominence, not just named summits.
- **Search:** each candidate's horizon at fine azimuth steps, keeping the farthest visible terrain.
- **Checking:** the top results confirmed from both ends and with finer sampling, with their refraction sensitivity
  reported, because a long sight line often depends on it.

### 7. Refinement

- **The loop:** `claude/refinement-loop.md`, repeated until a whole pass finds nothing.

## Working practices while unattended

- **Commits:** one improvement per commit, with the evidence in the message: test counts, timings and what was
  rejected.
- **Machine safety:** a bounded memory cache, and nothing written outside this folder and the scratchpad.
- **Downloads:** elevation tiles and peak data only, limited to the regions the tests and searches need (about 5 GB
  for Great Britain and Ireland plus the Pyrenees and western Alps). No executables.

## Needs your approval

The rules ask before adding packages. I plan to use:

- **Tests:** `NUnit`, `NUnit3TestAdapter`, `NUnit.Analyzers` and `Microsoft.NET.Test.Sdk`, as the rules specify.
- **Benchmarks:** `BenchmarkDotNet`.
- **Images:** `SkiaSharp` (MIT, maintained by Microsoft) to write PNG files and draw text, which .NET has no
  cross-platform support for.
- **GeoTIFF:** `BitMiracle.LibTiff.NET` (BSD) to decode tiles, unless it can't handle the tiles' compression, in
  which case a small reader for just the subset they use.
- **GPU, only if needed:** `ComputeSharp` (MIT) for DirectX 12 compute shaders.

[copernicus]: https://registry.opendata.aws/copernicus-dem/
[osm]: https://www.openstreetmap.org/copyright
