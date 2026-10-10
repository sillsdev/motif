using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class AgentPluginSkillTests
{
    private static readonly Regex ToolName = new(@"\bmotif_[a-z][a-z0-9_]*\b", RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLink = new(@"!?\[[^\]]*\]\(([^)]+)\)", RegexOptions.CultureInvariant);
    private static readonly string[] SkillNames =
    [
        "fieldworks-expert",
        "fieldworks-parsing-expert",
        "linguistic-consultant",
        "motif-workflow",
        "parsimony-review",
    ];

    [Fact]
    public void PluginSkillsUseDiscoverableLayoutAndOnlyNameAvailableTools()
    {
        var repository = FindRepositoryRoot();
        var skillsRoot = Path.Combine(repository, "plugin", "skills");
        var skillFiles = Directory.GetFiles(skillsRoot, "*", SearchOption.AllDirectories)
            .Where(path => !Directory.Exists(path))
            .ToArray();
        var folders = Directory.GetDirectories(skillsRoot).ToDictionary(folder => Path.GetFileName(folder), StringComparer.Ordinal);

        Assert.Equal(SkillNames.Order(StringComparer.Ordinal), folders.Keys.Order(StringComparer.Ordinal));
        foreach (var name in SkillNames)
            Assert.True(File.Exists(Path.Combine(folders[name], "SKILL.md")), $"{name} must contain SKILL.md.");

        var knownTools = AgentTools.Names;
        foreach (var file in skillFiles)
        {
            var content = File.ReadAllText(file);
            var names = ToolName.Matches(content).Select(match => match.Value).Distinct(StringComparer.Ordinal);
            foreach (var name in names)
            {
                Assert.Contains(name, knownTools);
                Assert.True(IsToolSkill(skillsRoot, file), $"Only motif-workflow and parsimony-review may name tools: {file}");
            }
        }
    }

    [Fact]
    public void EverySkillLinkStaysInsideItsOwnFolder()
    {
        var skillsRoot = Path.Combine(FindRepositoryRoot(), "plugin", "skills");
        foreach (var skillDirectory in Directory.GetDirectories(skillsRoot))
        {
            var fullSkillDirectory = Path.GetFullPath(skillDirectory);
            var boundary = Path.TrimEndingDirectorySeparator(fullSkillDirectory) + Path.DirectorySeparatorChar;
            foreach (var file in Directory.GetFiles(skillDirectory, "*.md", SearchOption.AllDirectories))
            foreach (Match match in MarkdownLink.Matches(File.ReadAllText(file)))
            {
                var target = match.Groups[1].Value.Trim();
                if (target.StartsWith('<') && target.Contains('>'))
                    target = target[1..target.IndexOf('>')];
                var separator = target.IndexOfAny([' ', '\t', '\r', '\n']);
                if (separator >= 0) target = target[..separator];
                if (target.Length == 0 || Uri.TryCreate(target, UriKind.Absolute, out _)) continue;

                var localPath = Uri.UnescapeDataString(target.Split('#', '?')[0]);
                if (localPath.Length == 0) continue;
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, localPath));
                Assert.True(resolved.StartsWith(boundary, PathComparison) ||
                    string.Equals(resolved, fullSkillDirectory, PathComparison),
                    $"Skill links must stay inside {skillDirectory}: {target}");
                Assert.True(File.Exists(resolved) || Directory.Exists(resolved),
                    $"Skill link target does not exist: {target} in {file}");
            }
        }
    }

    [Fact]
    public void BundledManifestsExposeTheLocalServerWithoutAProjectArgument()
    {
        var repository = FindRepositoryRoot();
        var pluginRoot = Path.Combine(repository, "plugin");
        var claudePlugin = JsonNode.Parse(File.ReadAllText(Path.Combine(pluginRoot, ".claude-plugin", "plugin.json")))!;
        var codexPlugin = JsonNode.Parse(File.ReadAllText(Path.Combine(pluginRoot, "plugin.json")))!;
        var claudeServer = JsonNode.Parse(File.ReadAllText(Path.Combine(pluginRoot, ".mcp.json")))!["mcpServers"]!["motif"]!;
        var codexServer = JsonNode.Parse(File.ReadAllText(Path.Combine(pluginRoot, "mcp.json")))!["mcpServers"]!["motif"]!;
        var claudeMarketplace = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, ".claude-plugin", "marketplace.json")))!;
        var codexMarketplace = JsonNode.Parse(File.ReadAllText(Path.Combine(pluginRoot, ".agents", "plugins", "marketplace.json")))!;

        Assert.Equal("motif", claudePlugin["name"]!.GetValue<string>());
        Assert.Equal("motif", codexPlugin["name"]!.GetValue<string>());
        Assert.Equal("motif", claudeMarketplace["name"]!.GetValue<string>());
        Assert.Equal("./plugin", claudeMarketplace["plugins"]![0]!["source"]!["path"]!.GetValue<string>());
        Assert.Equal("./", codexMarketplace["plugins"]![0]!["source"]!["path"]!.GetValue<string>());
        foreach (var server in new[] { claudeServer, codexServer })
        {
            Assert.Equal("motif", Path.GetFileNameWithoutExtension(server["command"]!.GetValue<string>()));
            Assert.Equal("mcp", server["args"]![0]!.GetValue<string>());
            Assert.DoesNotContain("--project", server["args"]!.AsArray().Select(value => value!.GetValue<string>()));
        }
    }

    private static bool IsToolSkill(string skillsRoot, string file)
    {
        var relativePath = Path.GetRelativePath(skillsRoot, file);
        var skillName = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return skillName is "motif-workflow" or "parsimony-review";
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "plugin", "skills")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find plugin/skills from the test output directory.");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
