using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Neo.Shell.Tests;

// The repo must open on any computer (packages from nuget.org) and still restore offline in the
// job container (exact pinned versions, all already in the global packages folder there).
public class PackageSourceTests
{
    private const string NuGetOrg = "https://api.nuget.org/v3/index.json";
    private static readonly Regex ExactVersion = new(@"^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.\-]+)?$");
    private static readonly string[] SkippedDirs = { ".git", ".godot", "bin", "obj", "TestResults" };

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
                return dir.FullName;
        throw new InvalidOperationException("repo root (project.godot) not found");
    }

    private static IEnumerable<string> RepoFiles(string dir)
    {
        foreach (var file in Directory.GetFiles(dir))
            yield return file;
        foreach (var sub in Directory.GetDirectories(dir))
            if (!SkippedDirs.Contains(Path.GetFileName(sub)))
                foreach (var file in RepoFiles(sub))
                    yield return file;
    }

    // Files that steer restore, build or the scripts (not docs).
    private static List<string> BuildFiles()
    {
        var names = new[] { "nuget.config", "global.json", "hub.json", "project.godot" };
        var extensions = new[] { ".csproj", ".props", ".targets", ".sh", ".config" };
        return RepoFiles(RepoRoot())
            .Where(f => names.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                        || extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    [Fact]
    public void NuGetConfigListsOnlyNuGetOrg()
    {
        var sources = XDocument.Load(Path.Combine(RepoRoot(), "nuget.config"))
            .Root!.Element("packageSources")!;
        Assert.NotNull(sources.Element("clear"));
        var adds = sources.Elements("add").ToList();
        var add = Assert.Single(adds);
        Assert.Equal(NuGetOrg, (string?)add.Attribute("value"));
        Assert.Empty(XDocument.Load(Path.Combine(RepoRoot(), "nuget.config")).Descendants("disabledPackageSources").Elements());
    }

    [Fact]
    public void EveryPackageVersionIsPinnedExactly()
    {
        var projects = BuildFiles().Where(f => f.EndsWith(".csproj") || f.EndsWith(".props") || f.EndsWith(".targets")).ToList();
        Assert.Contains(projects, p => Path.GetFileName(p) == "Neo.csproj");
        var references = 0;
        foreach (var project in projects)
        {
            var doc = XDocument.Load(project);
            var sdk = (string?)doc.Root!.Attribute("Sdk");
            if (sdk != null && sdk.Contains('/'))
                Assert.Matches(ExactVersion, sdk.Split('/')[1]);
            foreach (var reference in doc.Descendants("PackageReference"))
            {
                references++;
                var version = (string?)reference.Attribute("Version") ?? reference.Element("Version")?.Value;
                Assert.True(version != null && ExactVersion.IsMatch(version),
                    $"{project}: {(string?)reference.Attribute("Include")} version '{version}' is not an exact version");
            }
        }
        Assert.True(references > 0, "no PackageReference found");

        using var global = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "global.json")));
        foreach (var sdk in global.RootElement.GetProperty("msbuild-sdks").EnumerateObject())
            Assert.Matches(ExactVersion, sdk.Value.GetString()!);
    }

    [Fact]
    public void NothingAssumesTheJobContainer()
    {
        var forbidden = new[] { "/opt/", "NUGET_PACKAGES", "RestoreSources", "RestorePackagesPath",
                                "globalPackagesFolder", "RestoreAdditionalProjectSources", "/root/", "/work/" };
        foreach (var file in BuildFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var word in forbidden)
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"{file} mentions {word}");
        }
    }

    [Fact]
    public void OfflineAuditFailureIsNotAnError()
    {
        // With nuget.org listed, an offline restore cannot fetch vulnerability data (NU1900);
        // with TreatWarningsAsErrors that would fail every offline build.
        var props = XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"));
        var notAsErrors = string.Join(";", props.Descendants("WarningsNotAsErrors").Select(e => e.Value));
        Assert.Contains("NU1900", notAsErrors);
    }

    [Fact]
    public void ReadmeSaysHowToOpenItOnYourComputer()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        var start = readme.IndexOf("## Open it on your computer", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no \"Open it on your computer\" section");
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? readme[start..] : readme[start..end];
        foreach (var word in new[] { ".NET 10 SDK", "Godot 4.7.2 .NET", "Windows", "Linux", "macOS",
                                     "Apple Silicon", "clone", "project.godot", "Play", "download" })
            Assert.Contains(word, section);
    }
}
