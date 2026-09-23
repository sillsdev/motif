using System.Xml.Linq;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Contract;

/// <summary>
/// Pins that every product project targets <c>net10.0</c> alone, read from the project files themselves.
/// </summary>
/// <remarks>
/// This is a test rather than a convention because the target creeps back silently: the next person who
/// wants a compatibility shim adds a second moniker, nothing fails, and the reason it was retired (ADR 0043)
/// is lost.
/// </remarks>
public sealed class CompatibilityTargetTests
{
    [Fact]
    public void EveryProductProjectTargetsOnlyNet10()
    {
        foreach (var project in ProductProjects())
        {
            var document = XDocument.Load(project);
            var targets = document.Descendants()
                .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .Select(element => element.Value)
                .ToArray();
            Assert.Equal(["net10.0"], targets);
        }
    }

    [Fact]
    public void ContractHasNoLibLcmReference()
    {
        var text = File.ReadAllText(Project("SIL.Motif.Contract"));
        Assert.DoesNotContain("SIL.LCModel", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTestProjectUnderTestsIsListedInMotifSolution()
    {
        var root = RepoPaths.FindRepoRoot();
        var solution = Path.Combine(root, "Motif.sln");
        var listedProjects = File.ReadLines(solution)
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(line => line.Split('"'))
            .Where(parts => parts.Length > 5 && parts[5].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Path.GetFullPath(Path.Combine(root, parts[5])))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var testProjects = Directory.EnumerateFiles(
                Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Where(IsTestProject)
            .Select(Path.GetFullPath)
            .ToArray();
        var missing = testProjects
            .Where(project => !listedProjects.Contains(project))
            .Select(project => Path.GetRelativePath(root, project))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(testProjects);
        Assert.True(
            missing.Length == 0,
            $"Test projects missing from Motif.sln: {string.Join(", ", missing)}");
    }

    private static IEnumerable<string> ProductProjects()
    {
        var source = Path.Combine(RepoPaths.FindRepoRoot(), "src");
        return Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories);
    }

    private static string Project(string name) =>
        ProductProjects().Single(project => Path.GetFileNameWithoutExtension(project) == name);

    private static bool IsTestProject(string project)
    {
        var document = XDocument.Load(project);
        return document.Descendants().Any(element =>
                   element.Name.LocalName == "IsTestProject"
                   && bool.TryParse(element.Value, out var isTestProject)
                   && isTestProject)
               || document.Descendants().Any(element =>
                   element.Name.LocalName == "PackageReference"
                   && element.Attribute("Include")?.Value == "Microsoft.NET.Test.Sdk");
    }
}
