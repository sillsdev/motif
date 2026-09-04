using System.Xml.Linq;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins the one-way dependency direction between the command catalog and its front ends (ADR 0043):
/// <c>SIL.Motif.Commands</c> may reach every domain project it needs, but nothing may reach it back
/// into a UI project.
/// </summary>
public sealed class CommandsProjectReferenceTests
{
    [Fact]
    public void CommandsReferencesExactlyItsDomainProjects()
    {
        Assert.Equal(
            new[]
            {
                "SIL.Motif.Contract", "SIL.Motif.Host", "SIL.Motif.LiveHost", "SIL.Motif.Model",
                "SIL.Motif.Projection", "SIL.Motif.Runner", "SIL.Motif.Worker",
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
    }

    private static IEnumerable<string> ReferencedProjectNames(string projectName)
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "src", projectName, projectName + ".csproj");
        var document = XDocument.Load(path);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value));
    }
}
