// -----------------------------------------------------------------------
// <copyright file="NoSassyLiteralsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;

namespace Compendium.Adapters.GitHub.Tests;

/// <summary>
/// The package is published publicly, so no sassy-solutions-specific literal may
/// leak into it. This scans the adapter's own source (skipping the standard
/// copyright header comments, which carry the license attribution) for the org
/// slug, the bot login, and the platform App id.
/// </summary>
public sealed class NoSassyLiteralsTests
{
    private static readonly string[] Forbidden = ["sassy", "nxs-bot", "3654042"];

    [Fact]
    public void AdapterSource_ContainsNoSassySpecificLiterals()
    {
        var sourceRoot = AdapterSourceRoot();
        Directory.Exists(sourceRoot).Should().BeTrue($"expected the adapter source at {sourceRoot}");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var trimmed = line.TrimStart();

                // Skip comment lines (the copyright header carries the license attribution).
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var token in Forbidden)
                {
                    if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{lineNumber} -> '{token}'");
                    }
                }
            }
        }

        offenders.Should().BeEmpty("the published package must not carry sassy-solutions-specific literals");
    }

    private static string AdapterSourceRoot([CallerFilePath] string? thisFile = null)
    {
        // thisFile: tests/Unit/Compendium.Adapters.GitHub.Tests/NoSassyLiteralsTests.cs
        var testProjectDir = Path.GetDirectoryName(thisFile)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "Adapters", "Compendium.Adapters.GitHub");
    }
}
