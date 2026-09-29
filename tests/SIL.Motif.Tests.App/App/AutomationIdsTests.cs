using System.Text.RegularExpressions;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class AutomationIdsTests
{
    [Fact]
    public void EveryAutomationIdConstantIsUsedOnceAndEveryViewIdUsesTheClass()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root.FullName, "src", "SIL.Motif.App");
        var idsPath = Path.Combine(appRoot, "AutomationIds.cs");
        Assert.True(File.Exists(idsPath), "The App must define its automation IDs in one class.");

        var idsSource = File.ReadAllText(idsPath);
        var constants = Regex.Matches(idsSource, @"public\s+const\s+string\s+(?<name>\w+)\s*=")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.NotEmpty(constants);
        Assert.Equal(constants.Length, constants.Distinct(StringComparer.Ordinal).Count());

        var views = Directory.GetFiles(Path.Combine(appRoot, "Views"), "*.axaml", SearchOption.AllDirectories);
        var references = new List<string>();
        foreach (var view in views)
        {
            var source = File.ReadAllText(view);
            foreach (Match id in Regex.Matches(source,
                         @"AutomationProperties\.AutomationId\s*=\s*""(?<value>[^""]+)"""))
            {
                var value = id.Groups["value"].Value;
                var reference = Regex.Match(value, @"^\{x:Static\s+[\w.:]+AutomationIds\.(?<name>\w+)\}$");
                Assert.True(reference.Success, $"{view} has an AutomationId outside AutomationIds: {value}");
                references.Add(reference.Groups["name"].Value);
            }
        }

        Assert.Equal(constants.Order(StringComparer.Ordinal), references.Order(StringComparer.Ordinal));
        foreach (var name in constants)
            Assert.Equal(1, references.Count(reference => reference == name));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        return root ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }
}
