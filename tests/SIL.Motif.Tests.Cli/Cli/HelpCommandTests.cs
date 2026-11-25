using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Help;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class HelpCommandTests
{
    [Fact]
    public void HelpResolvesEveryMultiWordCommandAsSeparateWords()
    {
        var failures = HelpCatalog.Load().Entries
            .Where(entry => entry.Kind == HelpEntryKind.Command && entry.Code.Contains(' ', StringComparison.Ordinal))
            .Select(entry => entry.Code)
            .Where(code => Run(true, true, ["help", .. code.Split(' ')]).ExitCode != 0)
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void HelpResolvesEveryCatalogedMultiWordCommandToItsOwnPage()
    {
        var failures = CatalogedCommandNames().Where(IsPlainMultiWord)
            .Where(code => !HelpOpensWith(code, []))
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void ACommandFollowedByAnArgumentIsNotShadowedByAShorterCommand()
    {
        var names = CatalogedCommandNames();
        var failures = names.Where(IsPlainMultiWord)
            .Select(code => (Code: code, Shorter: code[..code.LastIndexOf(' ')]))
            .Where(pair => names.Contains(pair.Shorter, StringComparer.Ordinal))
            .Where(pair => !HelpOpensWith(pair.Code, []) || !HelpOpensWith(pair.Shorter, ["notaword"]))
            .Select(pair => pair.Code)
            .ToArray();

        Assert.Empty(failures);
        Assert.Contains(names, name => IsPlainMultiWord(name) && names.Contains(name[..name.LastIndexOf(' ')], StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("suffix")]
    [InlineData("prefix")]
    public void HelpForAGroupSubcommandShowsTheGroupHelp(string subcommand)
    {
        var result = Run("help", "group", subcommand);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Group words by stem", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpForTheTrialListsVerbose()
    {
        var result = Run("help", "trial");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--verbose", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpListsReleasedCommandsWithTitles()
    {
        var result = Run("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^Motif \(tech demo\) (?:—|-) report problems at https://github\.com/sillsdev/motif/issues", result.Output);
        Assert.Contains("Open a project", result.Output, StringComparison.Ordinal);
        Assert.Contains("Measure a Selection", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Create a Draft", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerHelpOpensWithTheEncodingGuideLine()
    {
        var line = HelpCatalog.Load().Find(HelpEntryKind.Command, "compose-author-phonological-rule")!.GuideLine;
        Assert.Contains("nasal-assimilation", line, StringComparison.Ordinal);

        foreach (var arguments in new[] { new[] { "help", "compose-author-phonological-rule" }, ["help", "compose-author-phonological-rule", "--full"] })
        {
            var result = Run(true, arguments);
            Assert.Equal(0, result.ExitCode);
            var lines = result.Output.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Assert.Equal(line, lines[1]);
            Assert.Equal(HelpCatalog.Load().Find(HelpEntryKind.Command, "compose-author-phonological-rule")!.Redirect, lines[2]);
        }
    }

    [Fact]
    public void HelpListsAndShowsTheEncodingGuides()
    {
        var list = Run("help");
        Assert.Equal(0, list.ExitCode);
        Assert.Contains("Encoding guides", list.Output, StringComparison.Ordinal);
        Assert.Contains("guide:encoding/nasal-assimilation", list.Output, StringComparison.Ordinal);

        var guide = Run("help", "guide:encoding/nasal-assimilation", "--full");
        Assert.Equal(0, guide.ExitCode);
        Assert.Contains("Segmental nasal assimilation", guide.Output, StringComparison.Ordinal);
        Assert.Contains(
            HelpCatalog.Load().GetHelpPage(HelpEntryKind.Guide, "encoding/nasal-assimilation")!.Split('\n')[0].TrimEnd('\r'),
            guide.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpDiscoversAdvancedAiAndDeveloperCommandsWhenBothModesAreOff()
    {
        var result = Run("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Advanced AI commands", result.Output, StringComparison.Ordinal);
        Assert.Contains("Advanced AI commands (enabled)", result.Output, StringComparison.Ordinal);
        Assert.Contains("label", result.Output, StringComparison.Ordinal);
        Assert.Contains("parsimony show", result.Output, StringComparison.Ordinal);
        Assert.Contains("store delete-refused", result.Output, StringComparison.Ordinal);
        Assert.Contains("Developer commands", result.Output, StringComparison.Ordinal);
        Assert.Contains("MOTIF_DEVELOPER_COMMANDS=1", result.Output, StringComparison.Ordinal);
        Assert.Contains("pending-changes", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpDiscoveryListsCompleteCatalogNamesOnEachHiddenSurface()
    {
        var result = Run("help");

        Assert.Equal(0, result.ExitCode);
        var lines = result.Output.Split(Environment.NewLine);
        var advancedHeader = Array.FindIndex(lines, line => line.StartsWith("Advanced AI commands", StringComparison.Ordinal));
        var developerHeader = Array.FindIndex(lines, line => line.StartsWith("Developer commands", StringComparison.Ordinal));
        Assert.True(advancedHeader >= 0 && developerHeader > advancedHeader);

        var advancedNames = lines[advancedHeader + 1].Split("  ", StringSplitOptions.RemoveEmptyEntries);
        var developerNames = lines[developerHeader + 1].Split("  ", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(CommandsForSurface(CommandSurface.AdvancedAi), advancedNames);
        Assert.Equal(CommandsForSurface(CommandSurface.Developer), developerNames);

        var projectCreate = CommandCatalog.All.SingleOrDefault(command => command.Name == "project create");
        if (projectCreate is not null)
        {
            Assert.Equal(CommandSurface.Autonomous, projectCreate.Surface);
            Assert.Equal(AgentClass.HumanOnly, projectCreate.Agent);
            Assert.False(projectCreate.IsAgentTool);
            Assert.DoesNotContain("project create", developerNames, StringComparer.Ordinal);
            var help = Run("help", "project", "create");
            Assert.Equal(0, help.ExitCode);
            Assert.Contains("MOTIF_AUTONOMOUS=1", help.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HelpDocumentsTheMcpEntryPointAndAdvancedAiModeStatus()
    {
        var list = Run("help");
        Assert.Contains("Other entry points:", list.Output, StringComparison.Ordinal);
        Assert.Contains("mcp", list.Output, StringComparison.Ordinal);
        Assert.Contains("settings advanced-ai", list.Output, StringComparison.Ordinal);

        var mcp = Run("help", "mcp", "--full");
        Assert.Equal(0, mcp.ExitCode);
        Assert.Contains("Usage: motif mcp", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("motif settings advanced-ai", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("complete initialization", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("zero tools", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("MOTIF_ADVANCED_AI_MODE_PATH", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("restart the Motif MCP server and reconnect the assistant", mcp.Output,
            StringComparison.Ordinal);
        Assert.Contains("tools/list result stays empty", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("motif_advanced_ai_setup", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("without reading project data or changing settings", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("A clean exit after standard input reaches EOF means the session ended", mcp.Output,
            StringComparison.Ordinal);
        Assert.Contains("by itself, it does not show that startup failed", mcp.Output, StringComparison.Ordinal);
        Assert.Contains("MCP launch log", mcp.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pending-changes", "MOTIF_DEVELOPER_COMMANDS=1", "")]
    public void HelpForAnUnavailableCommandExplainsItsEnablement(string command, string instruction, string pathHint)
    {
        var result = Run("help", command);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(command, result.Output, StringComparison.Ordinal);
        Assert.Contains(instruction, result.Output, StringComparison.Ordinal);
        if (pathHint.Length > 0)
            Assert.Contains(pathHint, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpShowsAdvancedAiAndDeveloperCommandsWhenBothModesAreEnabled()
    {
        var result = Run(true, true, "help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Advanced AI commands (enabled)", result.Output, StringComparison.Ordinal);
        Assert.Contains("Developer commands (enabled)", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("Start a Proposal", result.Output, StringComparison.Ordinal);
        Assert.Contains("parsimony show", result.Output, StringComparison.Ordinal);
        Assert.Contains("store delete-refused", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AppViewsAndCliHelpContainNoUserVisibleBetaCopy()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var viewsRoot = Path.Combine(repositoryRoot, "src", "SIL.Motif.App", "Views");
        var betaViews = Directory.EnumerateFiles(viewsRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\bbeta\b", RegexOptions.IgnoreCase))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();
        Assert.Empty(betaViews);

        var result = Run("help");
        Assert.Equal(0, result.ExitCode);
        Assert.False(Regex.IsMatch(result.Output, @"\bbeta\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void HelpForACommandPrintsItsTitleDescriptionUsageAndUrl()
    {
        var result = Run("help", "assess");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Measure a Selection", result.Output, StringComparison.Ordinal);
        Assert.Contains("PanGloss", result.Output, StringComparison.Ordinal);
        Assert.Contains("Usage: motif assess", result.Output, StringComparison.Ordinal);
        Assert.Contains("https://motif-docs.pages.dev/reference/commands/assess/", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void FullHelpPrintsTheHelpPageAndResolvesCommandLinks()
    {
        var result = Run("help", "assess", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("When to use", result.Output, StringComparison.Ordinal);
        Assert.Contains("motif help overview", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void FullHelpResolvesAQualifiedNestedGuide()
    {
        var result = Run("help", "guide:agents/start-here", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Using Motif from an agent", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Expanded help: motif help guide:agents/start-here --full", result.Output, StringComparison.Ordinal);
        Assert.Contains("Call Motif as", result.Output, StringComparison.Ordinal);
        Assert.Contains("https://motif-docs.pages.dev/guide/agents/start-here/", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonHelpExportsAQualifiedGuideWithItsHierarchicalCodeAndFullPage()
    {
        var result = Run("help", "guide:learn/stems-and-the-lexicon", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var entry = json.RootElement;

        Assert.Equal("guide", entry.GetProperty("kind").GetString());
        Assert.Equal("learn/stems-and-the-lexicon", entry.GetProperty("code").GetString());
        Assert.Equal("learn/stems-and-the-lexicon", entry.GetProperty("slug").GetString());
        Assert.Contains(
            "# Stems and the lexicon", entry.GetProperty("helpPage").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("/learn/stems-and-the-lexicon/", entry.GetProperty("url").GetString());
    }

    [Fact]
    public void GuideQualifierSelectsTheGuideWhenItsCodeMatchesAnotherEntry()
    {
        var result = Run("help", "guide:overview", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("guide", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("overview", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Progress and project health", json.RootElement.GetProperty("title").GetString());
        Assert.EndsWith("/guide/overview/", json.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void FullHelpAcceptsACommandCodeThatContainsAFlagLikePart()
    {
        var result = Run("help", "apply", "--all-pending", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("When to use it", result.Output, StringComparison.Ordinal);
        Assert.Contains("What it prints", result.Output, StringComparison.Ordinal);
        Assert.Contains("motif help overview", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsimonyHelpShowsTheBaselineReportPathAndAssessmentInputBoundary()
    {
        var parsimony = Run(true, "help", "parsimony", "--full");

        Assert.True(parsimony.ExitCode == 0, parsimony.Error);
        Assert.Contains("motif baseline capture --project \"<fwdata>\" --json", parsimony.Output,
            StringComparison.Ordinal);
        Assert.Contains("motif parsimony --project \"<fwdata>\" --measure P-adhoc-duplicate " +
            "--evidence-scope project-approved --wait --json", parsimony.Output, StringComparison.Ordinal);
        Assert.Contains("motif parsimony show --project \"<fwdata>\" \"<reportId>\" --json", parsimony.Output,
            StringComparison.Ordinal);

        var report = Run("help", "report", "--full");
        Assert.Equal(0, report.ExitCode);
        Assert.Contains("Supplying that Assessment to", report.Output, StringComparison.Ordinal);
        Assert.Contains("report --kind parsimony", report.Output, StringComparison.Ordinal);

        var kinds = Run("help", "report", "--list-kinds", "--full");
        Assert.Equal(0, kinds.ExitCode);
        Assert.Contains("the Parsimony producer needs a frozen Baseline evidence bundle", kinds.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedHelpCallUsesTheInvocationErrorExitCode()
    {
        var result = Run("help", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif help", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownHelpNameKeepsTheUnknownNameExitCode()
    {
        var result = Run("help", "not-a-released-command");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("No released command, glossary term, or Guide", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HandoffUsageIncludesTheTextsOption()
    {
        var descriptor = Assert.Single(CliVerbCatalog.All, item => item.CommandName == "handoff");

        var usage = Assert.Single(descriptor.UsageLines);
        Assert.Contains("OR motif handoff", usage, StringComparison.Ordinal);
        Assert.Contains("--no-assess [--texts <id,…>]", usage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dry-run --wait")]
    [InlineData("trial --wait")]
    public void JobWaitUsageIncludesTheWaitTimeoutOption(string command)
    {
        var descriptor = Assert.Single(CliVerbCatalog.All, item => item.CommandName == command);

        Assert.Contains("--wait-timeout-ms <ms>", Assert.Single(descriptor.UsageLines), StringComparison.Ordinal);
    }

    [Fact]
    public void CompoundCommandUsageDoesNotRepeatTheExecutableName()
    {
        var result = Run("help", "jobs", "move");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: motif jobs move", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Usage: motif motif", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonHelpEmitsOneCompleteCommandEntry()
    {
        var result = Run("help", "assess", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var entry = json.RootElement;

        Assert.Equal("command", entry.GetProperty("kind").GetString());
        Assert.Equal("assess", entry.GetProperty("code").GetString());
        Assert.Equal("assess", entry.GetProperty("slug").GetString());
        Assert.Equal("Measure a Selection", entry.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()));
        Assert.Contains("PanGloss", entry.GetProperty("helpPage").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(entry.GetProperty("usage").EnumerateArray());
        Assert.Equal("Released", entry.GetProperty("surface").GetString());
        Assert.EndsWith("/reference/commands/assess/", entry.GetProperty("url").GetString());
    }

    [Fact]
    public void AllJsonExportsEveryHelpEntryInTheSharedShape()
    {
        var result = Run("help", "--all", "--json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal("en", root.GetProperty("locale").GetString());
        Assert.Equal("https://motif-docs.pages.dev", root.GetProperty("siteRoot").GetString());

        var entries = root.GetProperty("entries").EnumerateArray().ToArray();
        var commands = entries.Where(entry => entry.GetProperty("kind").GetString() == "command").ToArray();
        var guides = entries.Where(entry => entry.GetProperty("kind").GetString() == "guide").ToArray();
        Assert.Equal(
            CommandCatalog.All.Where(command => command.Surface is CommandSurface.Released or CommandSurface.AdvancedAi)
                .Select(command => command.Name).Order(StringComparer.Ordinal),
            commands.Select(entry => entry.GetProperty("code").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(entries, entry =>
        {
            Assert.True(entry.TryGetProperty("kind", out _));
            Assert.True(entry.TryGetProperty("code", out _));
            Assert.True(entry.TryGetProperty("slug", out _));
            Assert.True(entry.TryGetProperty("title", out _));
            Assert.True(entry.TryGetProperty("description", out _));
            Assert.True(entry.TryGetProperty("helpPage", out _));
            Assert.True(entry.TryGetProperty("url", out _));
        });
        Assert.All(commands, entry =>
        {
            Assert.True(entry.TryGetProperty("usage", out _));
            var code = entry.GetProperty("code").GetString();
            Assert.Equal(CommandCatalog.All.Single(command => command.Name == code).Surface.ToString(),
                entry.GetProperty("surface").GetString());
        });
        var catalogGuides = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"))
            .Entries.Where(entry => entry.Kind == HelpEntryKind.Guide)
            .Select(entry => entry.Code).Order(StringComparer.Ordinal);
        Assert.Equal(catalogGuides, guides.Select(entry => entry.GetProperty("code").GetString()!)
            .Order(StringComparer.Ordinal));
        Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "guide"
            && entry.GetProperty("code").GetString() == "agents/start-here"
            && entry.GetProperty("url").GetString() == "https://motif-docs.pages.dev/guide/agents/start-here/");
        Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "guide"
            && entry.GetProperty("code").GetString() == "learn/index"
            && entry.GetProperty("url").GetString() == "https://motif-docs.pages.dev/learn/");
    }

    [Fact]
    public void HelpForThePhonologyExamplesParentTopicNamesEverySubcommand()
    {
        var subcommands = new[] { "save", "list", "show", "check", "results" };
        var brief = Run("help", "phonology-examples");
        var full = Run("help", "phonology-examples", "--full");

        Assert.Equal(0, brief.ExitCode);
        Assert.Equal(0, full.ExitCode);
        foreach (var subcommand in subcommands)
        {
            Assert.Contains(subcommand, brief.Output, StringComparison.Ordinal);
            Assert.Contains("phonology-examples " + subcommand, full.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OffModeHelpAndReleasedCatalogMatchCurrentGolden()
    {
        var result = Run("help", "--all", "--json");
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var goldenRoot = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.Cli", "Golden", "help");
        var expected = Directory.EnumerateFiles(goldenRoot, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(goldenRoot, file).Replace('\\', '/'),
            file => NormalizeNewlines(File.ReadAllText(file)),
            StringComparer.Ordinal);
        var actual = SplitHelpExport(NormalizeNewlines(result.Output));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(actual.Where(file => expected[file.Key] != file.Value).Select(file => file.Key).Order(StringComparer.Ordinal));
        var releasedNames = actual.Keys.Where(path => path.StartsWith("command/", StringComparison.Ordinal))
            .Select(path => JsonDocument.Parse(actual[path]).RootElement.GetProperty("code").GetString()!)
            .Order(StringComparer.Ordinal);
        Assert.Equal(releasedNames, CommandCatalog.All.Where(command => command.Surface is CommandSurface.Released or CommandSurface.AdvancedAi)
            .Select(command => command.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AdvancedAiHelpListsEveryCommandWithAUsageLine()
    {
        var result = Run(true, "help", "--all", "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        using var help = JsonDocument.Parse(result.Output);
        var commands = help.RootElement.GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("kind").GetString() == "command")
            .ToDictionary(entry => entry.GetProperty("code").GetString()!, StringComparer.Ordinal);
        foreach (var name in new[] { "dry-run --wait", "parsimony --dry-run --wait" })
            Assert.Contains("--wait", commands[name].GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("assess")]
    [InlineData("dry-run")]
    [InlineData("trial")]
    [InlineData("baseline", "capture")]
    [InlineData("compose-author-lexical-entry")]
    public void AVerbFollowedByHelpFlagPrintsTheSameHelpAndExitsZero(params string[] verb)
    {
        var viaFlag = Run(true, true, [.. verb, "--help"]);
        var viaHelp = Run(true, true, ["help", .. verb]);

        Assert.Equal(0, viaFlag.ExitCode);
        Assert.Equal(0, viaHelp.ExitCode);
        Assert.Equal(viaHelp.Output, viaFlag.Output);
        Assert.NotEmpty(viaFlag.Output);
    }

    [Fact]
    public void TheHelpFlagAfterOtherFlagsStillFindsTheVerb()
    {
        var viaFlag = Run(true, true, "trial", "--project", "x.fwdata", "--help");

        Assert.Equal(0, viaFlag.ExitCode);
        Assert.Equal(Run(true, true, "help", "trial").Output, viaFlag.Output);
    }

    [Fact]
    public void TheBareHelpFlagListsTheCommandsAndExitsZero()
    {
        var result = Run("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Run("help").Output, result.Output);
    }

    [Fact]
    public void HelpForATopicWithoutACommandPrintsThePageAndExitsZero()
    {
        var catalog = HelpCatalog.Load();
        var commandNames = CommandCatalog.All.Select(command => command.Name).ToHashSet(StringComparer.Ordinal);
        var topics = catalog.Entries
            .Where(entry => entry.Kind == HelpEntryKind.Command && !commandNames.Contains(entry.Code))
            .Select(entry => entry.Code)
            .ToArray();
        Assert.Contains("lexicon-import", topics);

        var failures = topics
            .SelectMany(topic => new[] { new[] { "help", topic }, ["help", topic, "--full"], ["help", topic, "--json"] })
            .Where(arguments => Run(true, true, arguments).ExitCode != 0)
            .Select(arguments => string.Join(' ', arguments))
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void ComposeHelpTellsWhereTheIntentSchemaAndTheCitationLimitAre()
    {
        var result = Run(true, true, "compose-author-lexical-entry", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--intent", result.Output, StringComparison.Ordinal);
        Assert.Contains("find-composer", result.Output, StringComparison.Ordinal);
        Assert.Contains("--full", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryHelpPageThatNamesTheCitationLocatorStatesItsLimit()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src", "SIL.Motif.Help", "Content", "en", "commands");
        var missing = Directory.GetFiles(root, "*.md")
            .Where(file => File.ReadAllText(file).Contains("--cite-locator", StringComparison.Ordinal))
            .Where(file => !File.ReadAllText(file).Contains("at most 300 characters", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("trial")]
    [InlineData("dry-run")]
    public void WaitHelpStatesTheDefaultTimeoutAndWhatItsExpiryMeans(string command)
    {
        var result = Run(true, true, "help", command, "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("120 seconds", result.Output, StringComparison.Ordinal);
        Assert.Contains("job.wait-timeout", result.Output, StringComparison.Ordinal);
        Assert.Contains("code 3", result.Output, StringComparison.Ordinal);
        Assert.Contains("jobs wait", result.Output, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "plugin", "skills"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not find the repository root from the test output directory.");
    }

    private static string[] CatalogedCommandNames() => HelpCatalog.Load().Entries
        .Where(entry => entry.Kind == HelpEntryKind.Command)
        .Select(entry => entry.Code)
        .Concat(CommandCatalog.All.Select(command => command.Name))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static bool IsPlainMultiWord(string code) =>
        code.Contains(' ', StringComparison.Ordinal) && !code.Split(' ').Any(word => word.StartsWith('-'));

    private static bool HelpOpensWith(string code, string[] trailing)
    {
        var result = Run(true, true, ["help", .. code.Split(' '), .. trailing]);
        var title = HelpCatalog.Load().Find(HelpEntryKind.Command, code)?.Title ?? code;
        return result.ExitCode == 0 && result.Output.Split('\n')[0].TrimEnd('\r') == title;
    }

    private static CliRun Run(params string[] arguments) => Run(false, arguments);

    private static CliRun Run(bool advancedAi, params string[] arguments) => Run(advancedAi, false, arguments);

    private static CliRun Run(bool advancedAi, bool developerCommands, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli);
        if (developerCommands)
            start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        else
            start.Environment.Remove(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable);
        var preferencePath = Path.Combine(Path.GetTempPath(), "motif-help-preferences", Guid.NewGuid().ToString("N"),
            "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(advancedAi);
        start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath;

        try
        {
            var result = CliInProcess.Run(start, arguments);
            return new CliRun(result.ExitCode, result.Output, result.Error);
        }
        finally
        {
            var directory = Path.GetDirectoryName(preferencePath)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string[] CommandsForSurface(CommandSurface surface) => CliVerbCatalog.All
        .Where(verb => CommandCatalog.All.Any(command =>
            command.Name == verb.CommandName && command.Surface == surface))
        .Select(verb => verb.CommandName)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private sealed record CliRun(int ExitCode, string Output, string Error);

    /// <summary>Splits the Help export into the golden folder's files, as tools/MotifHelpGolden.psm1 does.</summary>
    private static Dictionary<string, string> SplitHelpExport(string json)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(json);
        var envelope = document.RootElement.EnumerateObject().Where(property => property.Name != "entries")
            .Select(property => "  " + JsonSerializer.Serialize(property.Name) + ": " + property.Value.GetRawText());
        files["_envelope.json"] = "{\n" + string.Join(",\n", envelope) + "\n}\n";
        foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            var path = entry.GetProperty("kind").GetString() + "/" +
                entry.GetProperty("code").GetString()!.Replace(' ', '_').Replace('/', '~') + ".json";
            Assert.True(files.Keys.All(existing => !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)),
                "Two Help entries share the golden file " + path + ", ignoring case.");
            files[path] = Regex.Replace(entry.GetRawText(), "^    ", "", RegexOptions.Multiline) + "\n";
        }
        return files;
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
