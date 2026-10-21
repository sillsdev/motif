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
        var codeParts = arguments.Where(argument => argument is not "--all" and not "--json" and not "--full").ToArray();

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
                         entry.Kind == HelpEntryKind.Guide && entry.Code.StartsWith("encoding/", StringComparison.Ordinal)))
                output.WriteLine($"  {guide.Code["encoding/".Length..],-28}  {guide.Title}  (motif help guide:{guide.Code} --full)");
            output.WriteLine();
            PrintDiscoverySection(output, "Advanced AI commands", CommandSurface.AdvancedAi, policy);
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
        if (code == "mcp")
        {
            if (json)
                return Usage(error);
            PrintMcpHelp(full, output);
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
            var helpEntry = BuildEntry(entry, entryKind == HelpEntryKind.Command);
            output.WriteLine(JsonSerializer.Serialize(unavailable
                ? new { entry = helpEntry, available = false, enablement = EnablementText(command!) }
                : helpEntry, JsonOptions));
            return 0;
        }

        PrintEntry(entry, entryKind, catalog, output);
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

    private static object BuildExport(HelpCatalog catalog, CommandSurfacePolicy policy) => new
    {
        locale = catalog.Locale,
        siteRoot = HelpCatalog.SiteRoot,
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
        var enabled = surface == CommandSurface.AdvancedAi
            ? policy.AdvancedAiModeEnabled
            : policy.DeveloperCommandsEnabled;
        var instruction = surface switch
        {
            CommandSurface.AdvancedAi => "run 'motif settings advanced-ai on'; in a sandbox, set " +
                "MOTIF_ADVANCED_AI_MODE_PATH to a writable preference file",
            CommandSurface.Developer => "set MOTIF_DEVELOPER_COMMANDS=1",
            _ => string.Empty,
        };
        output.WriteLine($"{title} ({(enabled ? "enabled" : instruction)}):");
        output.WriteLine(string.Join("  ", commands));
    }

    private static string EnablementText(CommandDescriptor command) =>
        CommandAvailabilityText.Render(CommandAvailabilityFacts.For(command));

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
        output.WriteLine(entry.Description);
        if (kind == HelpEntryKind.Command)
        {
            foreach (var usage in UsageLines(entry.Code))
                output.WriteLine(NormalizeUsage(usage));
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
