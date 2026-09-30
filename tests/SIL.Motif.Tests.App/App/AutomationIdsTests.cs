using System.Text.RegularExpressions;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class AutomationIdsTests
{
    [Fact]
    public void EveryWorkspacePageHasAUniqueAutomationId()
    {
        var ids = Enum.GetValues<WorkspacePage>().Select(AutomationIds.ForPage).ToArray();

        Assert.All(ids, id => Assert.Matches("^motif-page-[a-z0-9-]+$", id));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void WordStripPartIdsAreStableSafeAndDistinct()
    {
        var parts = new[]
        {
            "strip", "word", "opinion", "disapproved", "fieldworks", "pangloss", "action", "fix", "staged", "unread",
        };
        var textId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ids = parts.Select(part => AutomationIds.ForWordPart(textId, "günler", 2, part)).ToArray();

        Assert.All(ids, id => Assert.Matches("^motif-word-11111111111111111111111111111111-2-[a-f0-9]+-[a-z]+$", id));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("motif-word-11111111111111111111111111111111-2-67c3bc6e6c6572-strip", ids[0]);
        Assert.Equal(ids, parts.Select(part => AutomationIds.ForWordPart(textId, "gu\u0308nler", 2, part)));
        Assert.NotEqual(ids[1], AutomationIds.ForWordPart(textId, "günler", 3, "word"));
        Assert.NotEqual(ids[1], AutomationIds.ForWordPart(textId, "geldi", 2, "word"));
    }

    [Fact]
    public void WordPartIdsIncludeTheirChosenTextIdentity()
    {
        static ResultsTokenViewModel Token(Guid textId) => new ResultsLineViewModel("Text",
            new TextLine(1, [new TextToken("same", "same", null, "unanalysed") { OccurrenceIndex = 0 }]),
            new Dictionary<string, AssessmentWordResult>(StringComparer.Ordinal), textId: textId).Tokens.Single();

        var first = Token(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = Token(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.NotEqual(first.WordAutomationId, second.WordAutomationId);
        Assert.NotEqual(first.StripAutomationId, second.StripAutomationId);
    }

    [Fact]
    public void AutomationIdConstantsHaveUniqueValuesAndEveryViewIdUsesTheClass()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root.FullName, "src", "SIL.Motif.App");
        var idsPath = Path.Combine(appRoot, "AutomationIds.cs");
        Assert.True(File.Exists(idsPath), "The App must define its automation IDs in one class.");

        var idsSource = File.ReadAllText(idsPath);
        var declarations = Regex.Matches(idsSource, @"public\s+const\s+string\s+(?<name>\w+)\s*=")
            .Select(match => match.Groups["name"].Value).ToArray();
        var constants = typeof(AutomationIds).GetFields(System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
        Assert.NotEmpty(constants);
        Assert.Equal(declarations.Order(StringComparer.Ordinal), constants.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(constants.Count, constants.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(constants.Values, value => Assert.Matches("^[a-z][a-z0-9-]*$", value));

        var views = Directory.GetFiles(Path.Combine(appRoot, "Views"), "*.axaml*", SearchOption.AllDirectories);
        var mainWindow = File.ReadAllText(Path.Combine(appRoot, "Views", "MainWindow.axaml"));
        Assert.Contains("Setter Property=\"AutomationProperties.AutomationId\" Value=\"{Binding AutomationId}\"",
            mainWindow, StringComparison.Ordinal);
        var references = new List<string>();
        foreach (var view in views)
        {
            var source = File.ReadAllText(view);
            foreach (Match reference in Regex.Matches(source, @"AutomationIds\.(?<name>\w+)"))
            {
                var name = reference.Groups["name"].Value;
                Assert.Contains(name, constants.Keys);
                references.Add(name);
            }

            var assignments = Regex.Matches(source,
                @"(?:AutomationProperties\.AutomationId\s*=\s*""|Setter\s+Property=""AutomationProperties\.AutomationId""\s+Value="")(?<value>[^""]+)""");
            foreach (Match assignment in assignments)
            {
                var value = assignment.Groups["value"].Value;
                if (Regex.IsMatch(value, @"^\{Binding (?:AutomationId|\w+AutomationId)\}$")) continue;
                Assert.Matches(@"^\{x:Static\s+[\w.:]+AutomationIds\.\w+\}$", value);
            }
            Assert.DoesNotMatch(@"AutomationProperties\.SetAutomationId\s*\([^,]+,\s*""[^""]+""\s*\)", source);
        }

        Assert.All(constants.Keys, name => Assert.Contains(name, references));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        return root ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }
}
