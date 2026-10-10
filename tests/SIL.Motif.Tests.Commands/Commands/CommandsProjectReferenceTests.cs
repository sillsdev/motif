using System.Xml.Linq;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins the command catalog's dependencies (ADR 0043), including Help's Parsimony recipe metadata
/// used to caption finding-based dispositions.
/// </summary>
public sealed class CommandsProjectReferenceTests
{
    [Fact]
    public void CommandsReferencesItsDomainProjectsAndHelpMetadata()
    {
        Assert.Equal(
            new[]
            {
                "SIL.Motif.Contract", "SIL.Motif.Help", "SIL.Motif.Host", "SIL.Motif.LiveHost", "SIL.Motif.Model",
                "SIL.Motif.Projection", "SIL.Motif.Runner", "SIL.Motif.Worker.Runtime",
            },
            ReferencedProjectNames("SIL.Motif.Commands").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CommandsNeverReferencesCli()
    {
        Assert.DoesNotContain("SIL.Motif.Cli", ReferencedProjectNames("SIL.Motif.Commands"));
    }

    [Fact]
    public void CliReferencesCommandsAndContract()
    {
        var references = ReferencedProjectNames("SIL.Motif.Cli");
        Assert.Contains("SIL.Motif.Commands", references);
        Assert.Contains("SIL.Motif.Contract", references);
        Assert.Contains("SIL.Motif.Worker.Runtime", references);
        Assert.DoesNotContain("SIL.Motif.Worker", references);
    }

    private static IEnumerable<string> ReferencedProjectNames(string projectName)
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", projectName, projectName + ".csproj");
        var document = XDocument.Load(path);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                element.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
    }
}
