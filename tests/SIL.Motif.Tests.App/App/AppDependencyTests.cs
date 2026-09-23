using System.Xml.Linq;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the dependency boundary for <c>SIL.Motif.App</c>: it binds to typed command outcomes from
/// <c>SIL.Motif.Commands</c> and never reaches around it into the CLI, LibLCM, or SQLite.
/// </summary>
public sealed class AppDependencyTests
{
    [Fact]
    public void AppReferencesExactlyCommandsAndContract()
    {
        Assert.Equal(
            new[] { "SIL.Motif.Commands", "SIL.Motif.Contract" },
            ReferencedProjectNames("SIL.Motif.App").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AppProjectFileNamesNoCliLibLcmOrSqliteReference()
    {
        var text = File.ReadAllText(ProjectFile("SIL.Motif.App"));
        Assert.DoesNotContain("SIL.Motif.Cli", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SIL.LCModel", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSourceImportsNoCliOrWorkerNamespaceAndNamesNoLibLcmOrSqliteType()
    {
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("using SIL.Motif.Cli", text, StringComparison.Ordinal);
            Assert.DoesNotContain("using SIL.Motif.Worker", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LcmCache", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SIL.LCModel", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SqliteConnection", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Data.Sqlite", text, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.App"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string ProjectFile(string name) =>
        Path.Combine(RepoPaths.FindRepoRoot(), "src", name, name + ".csproj");

    private static IEnumerable<string> ReferencedProjectNames(string projectName)
    {
        var document = XDocument.Load(ProjectFile(projectName));
        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value));
    }
}
