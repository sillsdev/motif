using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Help;

namespace SIL.Motif.Cli;

/// <summary>Prints the shared command and glossary help content from the CLI.</summary>
public static class HelpCommand
{
    private static readonly Regex MarkdownLinkPattern = new(
        @"\[(?<text>[^\]]+)\]\((?<kind>cmd|term|ui):(?<code>[^)]+)\)", RegexOptions.CultureInvariant);
    private static readonly Regex ScreenshotPattern = new(
        @"!\[(?<alt>[^\]]*)\]\(shot:(?<id>[^/)]+)/(?<step>[^)]+)\)", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
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
            foreach (var commandEntry in catalog.Entries.Where(entry => entry.Kind == HelpEntryKind.Command))
                output.WriteLine($"{commandEntry.Title,-30}  {commandEntry.Code}");
            output.WriteLine();
            output.WriteLine("Use 'motif help <command> --full' for examples and related commands.");
            return 0;
        }

        var code = string.Join(' ', codeParts);
        var entryKind = catalog.Find(HelpEntryKind.Command, code) is not null
            ? HelpEntryKind.Command
            : HelpEntryKind.Term;
        var entry = catalog.Find(entryKind, code);
        if (entry is null)
        {
            error.WriteLine($"No released command or glossary term named '{code}'. Run 'motif help' for commands.");
            return 2;
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(BuildEntry(entry, entryKind == HelpEntryKind.Command), JsonOptions));
            return 0;
        }

        PrintEntry(entry, entryKind, catalog, output);
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
            .Where(entry => entry.Kind != HelpEntryKind.Command
                || CommandCatalog.All.Any(command => command.Name == entry.Code
                    && policy.IsAvailable(command)
                    && command.Surface == CommandSurface.Released))
            .Select(entry => BuildEntry(entry, entry.Kind == HelpEntryKind.Command)),
    };

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
            .Where(line => line.Contains(commandName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(NormalizeUsage)
            .ToArray();
        if (lines.Length == 0)
            throw new InvalidDataException($"No CLI usage line is catalogued for '{commandName}'.");
        return lines;
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
        output.WriteLine("Expanded help: motif help " + entry.Code + " --full");
    }

    private static string ResolvePage(string page, HelpCatalog catalog)
    {
        var resolved = MarkdownLinkPattern.Replace(page, match =>
        {
            var text = match.Groups["text"].Value;
            var linkKind = match.Groups["kind"].Value;
            var code = Uri.UnescapeDataString(match.Groups["code"].Value);
            return linkKind == "cmd"
                ? $"{text} (motif help {code})"
                : $"{text} ({catalog.BuildUrl(ParseKind(linkKind), code)})";
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string KindCode(HelpEntryKind kind) => kind switch
    {
        HelpEntryKind.Command => "command",
        HelpEntryKind.Ui => "ui",
        HelpEntryKind.Term => "term",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Usage: motif help [<command> [--full | --json]] | --all --json");
        return 1;
    }
}
