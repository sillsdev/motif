using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Help;

namespace SIL.Motif.Cli;

/// <summary>Prints the shared command, glossary, and Guide Help content from the CLI.</summary>
public static class HelpCommand
{
    private static readonly Regex MarkdownLinkPattern = new(
        @"\[(?<text>[^\]]+)\]\((?<kind>cmd|term|ui|guide):(?<code>[^)]+)\)", RegexOptions.CultureInvariant);
    private static readonly Regex ScreenshotPattern = new(
        @"!\[(?<alt>[^\]]*)\]\(shot:(?<id>[^/)]+)/(?<step>[^)]+)\)", RegexOptions.CultureInvariant);
    private const string EncodingGuidePrefix = "encoding/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>Runs the help front end for the supplied help arguments.</summary>
    /// <param name="arguments">Arguments following the <c>help</c> verb.</param>
    /// <param name="policy">The command availability policy for this invocation.</param>
    /// <param name="output">The destination for help output.</param>
    /// <param name="error">The destination for errors.</param>
    public static int Run(
        string[] arguments,
        CommandSurfacePolicy policy,
        TextWriter output,
        TextWriter error)
    {
        var catalog = HelpCatalog.Load();
        var all = arguments.Contains("--all", StringComparer.Ordinal);
        var json = arguments.Contains("--json", StringComparer.Ordinal);
        var full = arguments.Contains("--full", StringComparer.Ordinal);
        var codeParts = arguments.Where(argument => argument is not "--all" and not "--json" and not "--full" and not CommandSurfacePolicy.AutonomousFlag).ToArray();

        if (all)
        {
            if (!json || codeParts.Length > 0 || full)
                return Usage(error);
            output.WriteLine(JsonSerializer.Serialize(BuildExport(catalog, policy), JsonOptions));
            return 0;
        }

        if (codeParts.Length == 0)
        {
            if (json || full)
                return Usage(error);
            output.WriteLine("Motif (tech demo) — report problems at https://github.com/sillsdev/motif/issues");
            output.WriteLine();
            foreach (var commandEntry in catalog.Entries.Where(entry =>
                         entry.Kind == HelpEntryKind.Command && IsAvailableHelpEntry(entry, policy)))
                output.WriteLine($"{commandEntry.Title,-30}  {commandEntry.Code}");
            output.WriteLine();
            output.WriteLine("Encoding guides (read the one for the phenomenon before composing rules):");
            foreach (var guide in catalog.Entries.Where(entry =>
                         entry.Kind == HelpEntryKind.Guide && entry.Code.StartsWith(EncodingGuidePrefix, StringComparison.Ordinal)))
                output.WriteLine($"  guide:{guide.Code,-34}  {guide.Title}");
            output.WriteLine();
            PrintDiscoverySection(output, "Advanced AI commands", CommandSurface.AdvancedAi, policy);
            output.WriteLine();
            PrintDiscoverySection(output, "Autonomous commands", CommandSurface.Autonomous, policy);
            output.WriteLine();
            PrintDiscoverySection(output, "Developer commands", CommandSurface.Developer, policy);
            output.WriteLine();
            output.WriteLine("Other entry points:");
            output.WriteLine("  mcp                         Start Motif's MCP server over standard input and output");
            output.WriteLine("  settings advanced-ai       Read the mode status; add on or off to change it");
            output.WriteLine();
            output.WriteLine("Use 'motif help <command> --full' for examples and related commands.");
            return 0;
        }

        var code = string.Join(' ', codeParts);
        if (!code.StartsWith("guide:", StringComparison.Ordinal) && !IsKnownEntry(catalog, code))
        {
            // A trailing word may be an argument of the command, as in "group suffix".
            for (var length = codeParts.Length - 1; length >= 1; length--)
            {
                var prefix = string.Join(' ', codeParts.Take(length));
                if (!IsKnownCommand(catalog, prefix) && !IsVerbGroup(prefix, policy)) continue;
                code = prefix;
                break;
            }
        }

        if (code == "mcp")
        {
            if (json)
                return Usage(error);
            PrintMcpHelp(full, output);
            return 0;
        }

        if (code == "autonomous")
        {
            if (json)
                return Usage(error);
            PrintAutonomousHelp(output, policy);
            return 0;
        }

        if (!code.StartsWith("guide:", StringComparison.Ordinal) && IsVerbGroup(code, policy) && !IsKnownEntry(catalog, code))
        {
            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(BuildGroup(code, catalog, policy), JsonOptions));
                return 0;
            }
            PrintVerbGroup(code, catalog, policy, output);
            return 0;
        }

        var guidePrefix = "guide:";
        var isGuide = code.StartsWith(guidePrefix, StringComparison.Ordinal);
        var isCommand = catalog.Find(HelpEntryKind.Command, code) is not null ||
            CommandCatalog.All.Any(item => item.Name == code);
        var entryKind = isGuide
            ? HelpEntryKind.Guide
            : isCommand
                ? HelpEntryKind.Command
                : HelpEntryKind.Term;
        var lookupCode = isGuide ? Uri.UnescapeDataString(code[guidePrefix.Length..]) : code;
        var entry = catalog.Find(entryKind, lookupCode);
        var command = entryKind == HelpEntryKind.Command
            ? CommandCatalog.All.SingleOrDefault(item => item.Name == lookupCode)
            : null;
        if (entry is null && command is not null)
        {
            if (json)
                return Usage(error);
            output.WriteLine(command.Name);
            foreach (var usage in UsageLines(command.Name))
                output.WriteLine(NormalizeUsage(usage));
            output.WriteLine(EnablementText(command));
            if (IsVerbGroup(code, policy))
                PrintVerbsSection(code, catalog, policy, output);
            return 0;
        }
        if (entry is null)
        {
            error.WriteLine($"No released command, glossary term, or Guide named '{code}'. Run 'motif help' for commands.");
            return 2;
        }

        var unavailable = command is not null && !policy.IsAvailable(command);
        if (json)
        {
            var helpEntry = BuildEntry(entry, entryKind == HelpEntryKind.Command && IsCataloguedCommand(entry.Code));
            output.WriteLine(JsonSerializer.Serialize(unavailable
                ? new { entry = helpEntry, available = false, enablement = EnablementText(command!) }
                : helpEntry, JsonOptions));
            return 0;
        }

        PrintEntry(entry, entryKind, catalog, output);
        if (entryKind != HelpEntryKind.Guide && IsVerbGroup(entry.Code, policy))
            PrintVerbsSection(entry.Code, catalog, policy, output);
        if (unavailable)
        {
            output.WriteLine();
            output.WriteLine(EnablementText(command!));
        }
        if (full)
        {
            output.WriteLine();
            output.WriteLine(entry.HelpPage is null
                ? "No expanded Help page is available for this entry."
                : ResolvePage(entry.HelpPage, catalog));
        }

        return 0;
    }

    /// <summary>
    /// The verb groups, derived from <see cref="CliVerbCatalog"/>: a first word that heads at least one command whose
    /// next word is a verb rather than a flag (<c>guide list</c>, <c>query pairs</c>), mapped to those commands.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> VerbGroups { get; } = CliVerbCatalog.All
        .Select(verb => string.Join(' ', verb.CommandName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeWhile(word => !word.StartsWith('-'))))
        .Where(name => name.Contains(' ', StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .GroupBy(name => name[..name.IndexOf(' ', StringComparison.Ordinal)], StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    // A group is a topic only while it has a verb this call may list: Developer verbs stay hidden unless enabled.
    private static IReadOnlyList<string> VisibleVerbs(string group, CommandSurfacePolicy policy) =>
        VerbGroups.TryGetValue(group, out var names)
            ? names.Where(name => CommandCatalog.All.FirstOrDefault(item => item.Name == name) is { } command
                && (command.Surface != CommandSurface.Developer || policy.DeveloperCommandsEnabled)).ToArray()
            : Array.Empty<string>();

    private static bool IsVerbGroup(string code, CommandSurfacePolicy policy) => VisibleVerbs(code, policy).Count > 0;

    private static string GateName(CommandSurface? surface) => surface switch
    {
        CommandSurface.Released => "Released",
        CommandSurface.AdvancedAi => "Advanced AI",
        CommandSurface.Autonomous => "Autonomous",
        CommandSurface.Developer => "Developer",
        _ => "Unlisted",
    };

    private static object BuildGroup(string group, HelpCatalog catalog, CommandSurfacePolicy policy) => new
    {
        group,
        verbs = VisibleVerbs(group, policy).Select(name => new
        {
            name,
            summary = Summary(catalog, name),
            gate = GateName(CommandCatalog.All.First(item => item.Name == name).Surface),
            helpTopic = "motif help " + name,
        }).ToArray(),
    };

    private static void PrintVerbGroup(string group, HelpCatalog catalog, CommandSurfacePolicy policy, TextWriter output)
    {
        output.WriteLine($"{group} commands");
        output.WriteLine($"The verbs of 'motif {group}', each with its gate.");
        PrintVerbsSection(group, catalog, policy, output, leadingBlank: false);
    }

    private static void PrintVerbsSection(
        string group, HelpCatalog catalog, CommandSurfacePolicy policy, TextWriter output, bool leadingBlank = true)
    {
        var verbs = VisibleVerbs(group, policy);
        if (leadingBlank)
            output.WriteLine();
        output.WriteLine("Verbs:");
        foreach (var name in verbs)
        {
            var surface = CommandCatalog.All.First(item => item.Name == name).Surface;
            output.WriteLine($"  {name,-34} [{GateName(surface)}] {Summary(catalog, name)}");
        }
        output.WriteLine($"Use 'motif help {group} <verb>' for one verb, for example 'motif help {verbs[0]}'.");
    }

    private static string Summary(HelpCatalog catalog, string name)
    {
        var entry = catalog.Find(HelpEntryKind.Command, name);
        var text = entry?.Description;
        if (string.IsNullOrWhiteSpace(text)) text = entry?.Title;
        if (string.IsNullOrWhiteSpace(text))
        {
            var usage = CliVerbCatalog.All.Where(verb => verb.CommandName == name)
                .SelectMany(verb => verb.UsageLines).FirstOrDefault();
            text = usage is null ? name : "Usage: " + usage.Split(" OR ", StringSplitOptions.TrimEntries)[0];
        }
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        text = (end < 0 ? text : text[..(end + 1)]).Trim();
        return text.Length <= 100 ? text : text[..97].TrimEnd() + "...";
    }

    // A Command-kind Help page can be a topic with no command behind it, such as lexicon-import.
    private static bool IsCataloguedCommand(string code) => CommandCatalog.All.Any(item => item.Name == code);

    private static bool IsKnownCommand(HelpCatalog catalog, string code) =>
        code is "mcp" or "autonomous" ||
        catalog.Find(HelpEntryKind.Command, code) is not null || CommandCatalog.All.Any(item => item.Name == code);

    private static bool IsKnownEntry(HelpCatalog catalog, string code) =>
        IsKnownCommand(catalog, code) || catalog.Find(HelpEntryKind.Term, code) is not null;

    private static object BuildExport(HelpCatalog catalog, CommandSurfacePolicy policy) => new
    {
        locale = catalog.Locale,
        siteRoot = HelpCatalog.SiteRoot,
        verbGroups = VerbGroups.Keys.Order(StringComparer.Ordinal)
            .Where(group => IsVerbGroup(group, policy))
            .Select(group => BuildGroup(group, catalog, policy)),
        entries = catalog.Entries
            .Where(entry => IsAvailableHelpEntry(entry, policy))
            .Select(entry => BuildEntry(entry, entry.Kind == HelpEntryKind.Command)),
    };

    private static void PrintMcpHelp(bool full, TextWriter output)
    {
        output.WriteLine("Start the MCP server");
        output.WriteLine("Starts Motif's MCP server over standard input and output for an assistant connection.");
        output.WriteLine("Usage: motif mcp [--profile <file | shipped name>] [--activity-log <file.jsonl>]");
        output.WriteLine("Advanced AI mode is read when the server starts.");
        output.WriteLine("Read the current mode with 'motif settings advanced-ai'; use 'on' or 'off' to change it.");
        output.WriteLine("Expanded help: motif help mcp --full");
        if (!full) return;

        output.WriteLine();
        output.WriteLine("The MCP server can start and complete initialization while Advanced AI mode is off. " +
            "That connected session exposes zero tools, and a tool call explains that the mode is off. " +
            "The tools/list result stays empty; the inert 'motif_advanced_ai_setup' prompt provides setup steps " +
            "without reading project data or changing settings. " +
            "Keep the stdio connection open while waiting for initialization and tools/list. A clean exit after " +
            "standard input reaches EOF means the session ended; by itself, it does not show that startup failed. " +
            "If initialization fails while the connection remains open, inspect the assistant's MCP launch log.");
        output.WriteLine();
        output.WriteLine("Run 'motif settings advanced-ai on' to enable tools. In a sandbox, set " +
            "MOTIF_ADVANCED_AI_MODE_PATH to a writable preference file before changing the setting. " +
            "Then restart the Motif MCP server and reconnect the assistant so it reads the setting at startup.");
    }

    private static bool IsAvailableHelpEntry(HelpEntry entry, CommandSurfacePolicy policy)
    {
        if (entry.Kind != HelpEntryKind.Command) return true;
        var command = CommandCatalog.All.SingleOrDefault(item => item.Name == entry.Code);
        return command is not null && policy.IsAvailable(command);
    }

    private static void PrintDiscoverySection(
        TextWriter output, string title, CommandSurface surface, CommandSurfacePolicy policy)
    {
        var commands = CliVerbCatalog.All
            .Where(verb => CommandCatalog.All.Any(command =>
                command.Name == verb.CommandName && command.Surface == surface))
            .Select(verb => verb.CommandName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var enabled = surface switch
        {
            CommandSurface.AdvancedAi => policy.AdvancedAiModeEnabled,
            CommandSurface.Autonomous => policy.AutonomousEnabled,
            _ => policy.DeveloperCommandsEnabled,
        };
        var instruction = surface switch
        {
            CommandSurface.AdvancedAi => "run 'motif settings advanced-ai on'; in a sandbox, set " +
                "MOTIF_ADVANCED_AI_MODE_PATH to a writable preference file",
            CommandSurface.Autonomous => "set MOTIF_AUTONOMOUS=1 or pass --autonomous; see 'motif help autonomous'",
            CommandSurface.Developer => "set MOTIF_DEVELOPER_COMMANDS=1",
            _ => string.Empty,
        };
        output.WriteLine($"{title} ({(enabled ? "enabled" : instruction)}):");
        output.WriteLine(string.Join("  ", commands));
    }

    private static void PrintAutonomousHelp(TextWriter output, CommandSurfacePolicy policy)
    {
        output.WriteLine("Autonomous commands exist for an Autonomous-mode run: an AI driving the CLI from source to a working parser.");
        output.WriteLine("They are CLI only and never MCP tools.");
        output.WriteLine();
        output.WriteLine("Enable: set MOTIF_AUTONOMOUS=1, or pass --autonomous on the call. " +
            (policy.AutonomousEnabled ? "Autonomous is enabled for this call." : "Autonomous is not enabled for this call."));
        output.WriteLine("The same switch makes Apply an Autonomous-mode Apply, which needs --message.");
        output.WriteLine();
        output.WriteLine("Commands:");
        var names = CliVerbCatalog.All
            .Select(verb => verb.CommandName)
            .Where(name => CommandCatalog.All.Any(command => command.Name == name && command.Surface == CommandSurface.Autonomous))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var name in names) output.WriteLine("  " + name);
        output.WriteLine();
        output.WriteLine("Probe loop: new, a compose verb, read the result, then discard-draft to leave no Draft behind.");
        output.WriteLine();
        output.WriteLine("Grammar Guide: 'guide show <GDR-n> --format fw-md', edit the file, then 'guide edit <GDR-n> --from-file <file>'");
        output.WriteLine("stages it (add --draft <name> to ship it with a rule's Draft; check it with 'guide show <GDR-n> --as-of <name>').");
        output.WriteLine("Record a judgement call, \"A or B, I am choosing A, because ...\", with 'guide decide <GDR-n> --choosing A --over B --because <reason>';");
        output.WriteLine("each part is one line of inline Markdown, so a form is written [form]{.v} (escape a literal [ * _ ` < or \\ as \\[ \\* \\_ \\` \\< \\\\), and a heading, list or fence is refused.");
        output.WriteLine("Only existing sections can be edited; 'guide add --title <text> [--parent <GDR-n>] [--after <GDR-n>] [--from-file <body.md>]' stages a new one.");
        output.WriteLine("'guide edit' and 'guide add' need Advanced AI mode ('motif settings advanced-ai on'); 'guide show' is a read and needs none; 'guide decide' needs --autonomous.");
        output.WriteLine("First time on a project: 'guide add --title <text>' (creates the list), apply the pending changes, 'baseline capture --project <fwdata>',");
        output.WriteLine("then 'guide check'. Guide reads show nothing until a Baseline is captured. The id of each new section is in the 'guide add --json' answer.");
        output.WriteLine("When the Draft already holds an edit of a section, start from 'guide show <GDR-n> --as-of <name> --format fw-md'.");
        output.WriteLine();
        output.WriteLine("Gate rule: Autonomous Apply needs a green Trial gate, except for a Proposal whose every operation edits a Grammar Guide");
        output.WriteLine("section or the Note of a Text segment. Those change no grammar and no lexicon, so no word can be lost and the Trial is exempt.");
        output.WriteLine("Add any other operation to the Proposal and the gate applies to all of it.");
        output.WriteLine("Use 'motif help <command> --full' for one command.");
    }

    private static string EnablementText(CommandDescriptor command) => command.Surface == CommandSurface.AdvancedAi
        ? command.Agent == AgentClass.HumanOnly
            ? "Available in the CLI. This command is human-only and is never exposed as an MCP tool."
            : "Available in the CLI. Assistants need Advanced AI mode enabled to use this MCP tool."
        : CommandAvailabilityText.Render(CommandAvailabilityFacts.For(command));

    private static object BuildEntry(HelpEntry entry, bool command)
    {
        if (!command)
            return new
            {
                kind = KindCode(entry.Kind),
                entry.Code,
                entry.Slug,
                entry.Title,
                entry.Description,
                helpPage = entry.HelpPage,
                entry.Url,
            };

        var descriptor = CommandCatalog.All.SingleOrDefault(command => command.Name == entry.Code);
        if (descriptor is null)
            throw new InvalidDataException($"Help entry '{entry.Code}' does not name a catalogued command.");
        var usage = UsageLines(entry.Code);
        return new
        {
            kind = KindCode(entry.Kind),
            entry.Code,
            entry.Slug,
            entry.Title,
            entry.Description,
            helpPage = entry.HelpPage,
            usage,
            surface = descriptor.Surface.ToString(),
            entry.Url,
        };
    }

    private static IReadOnlyList<string> UsageLines(string commandName)
    {
        var exact = CliVerbCatalog.All.Where(verb => verb.CommandName == commandName).ToArray();
        var source = exact.Where(verb => verb.UsageLines.Count > 0).ToArray();
        if (source.Length == 0)
            source = CliVerbCatalog.All.Where(verb => exact.Any(item => item.Verb == verb.Verb)
                && verb.UsageLines.Count > 0).ToArray();

        var lines = source.SelectMany(verb => verb.UsageLines)
            .SelectMany(line => line.Split(" OR ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(line => NamesCommand(line, commandName))
            .Distinct(StringComparer.Ordinal)
            .Select(NormalizeUsage)
            .ToArray();
        if (lines.Length == 0)
            throw new InvalidDataException($"No CLI usage line is catalogued for '{commandName}'.");
        return lines;
    }

    // A usage line names a command when the command's words appear in it in order, arguments between them allowed.
    private static bool NamesCommand(string line, string commandName)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = 0;
        foreach (var word in commandName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            at = Array.IndexOf(words, word, at);
            if (at < 0) return false;
            at++;
        }
        return true;
    }

    private static string NormalizeUsage(string usage) =>
        usage.StartsWith("Usage: ", StringComparison.Ordinal)
            ? usage
            : usage.StartsWith("motif ", StringComparison.Ordinal)
                ? "Usage: " + usage
            : "Usage: motif " + usage;

    private static void PrintEntry(HelpEntry entry, HelpEntryKind kind, HelpCatalog catalog, TextWriter output)
    {
        output.WriteLine(entry.Title);
        if (entry.GuideLine is { } guideLine)
            output.WriteLine(guideLine);
        if (entry.Redirect is { } redirect)
            output.WriteLine(redirect);
        output.WriteLine(entry.Description);
        if (kind == HelpEntryKind.Command && IsCataloguedCommand(entry.Code))
        {
            foreach (var usage in UsageLines(entry.Code))
                output.WriteLine(NormalizeUsage(usage));
        }
        if (kind == HelpEntryKind.Command && entry.Code.StartsWith("compose-", StringComparison.Ordinal))
        {
            output.WriteLine("Intent schema: motif find-composer --goal \"<what you want to do>\" --detail detailed --json " +
                "lists this command's closed intent shape; motif help " + entry.Code + " --full gives examples and field rules.");
        }
        output.WriteLine("Online: " + entry.Url);
        var fullCode = kind == HelpEntryKind.Guide ? "guide:" + entry.Code : entry.Code;
        output.WriteLine("Expanded help: motif help " + fullCode + " --full");
    }

    private static string ResolvePage(string page, HelpCatalog catalog)
    {
        var resolved = MarkdownLinkPattern.Replace(page, match =>
        {
            var text = match.Groups["text"].Value;
            var linkKind = match.Groups["kind"].Value;
            var code = Uri.UnescapeDataString(match.Groups["code"].Value);
            return linkKind switch
            {
                "cmd" => $"{text} (motif help {code})",
                "guide" => $"{text} (motif help guide:{code} --full)",
                _ => $"{text} ({catalog.BuildUrl(ParseKind(linkKind), code)})",
            };
        });
        return ScreenshotPattern.Replace(resolved, match =>
        {
            var alt = match.Groups["alt"].Value;
            var url = catalog.BuildWalkthroughUrl(match.Groups["id"].Value)
                + "#" + Uri.EscapeDataString(match.Groups["step"].Value);
            return string.IsNullOrWhiteSpace(alt) ? "Screenshot: " + url : alt + ": " + url;
        });
    }

    private static HelpEntryKind ParseKind(string kind) => kind switch
    {
        "term" => HelpEntryKind.Term,
        "ui" => HelpEntryKind.Ui,
        "guide" => HelpEntryKind.Guide,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string KindCode(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "command",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "term",
        HelpEntryKind.Guide => "guide",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage: motif help [<command> | guide:<code> [--full | --json]] | --all --json");
        return 1;
    }
}
