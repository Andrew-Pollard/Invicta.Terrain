// © 2026 Andrew Pollard. All rights reserved.
// Licensed under the MIT License.

using System.CommandLine;

namespace Invicta;

/// <summary>Runs the command-line tool.</summary>
internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        RootCommand root = new("Renders and analyzes views of real terrain from the Copernicus GLO-30 DEM.");
        root.Subcommands.Add(PanoramaCommand.Create());

        return root.Parse(args).InvokeAsync();
    }
}
