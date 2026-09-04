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

    private static IEnumerable<string> ProductProjects()
    {
        var source = Path.Combine(RepoPaths.FindRepoRoot(), "src");
        return Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories);
    }

    private static string Project(string name) =>
        ProductProjects().Single(project => Path.GetFileNameWithoutExtension(project) == name);
}
