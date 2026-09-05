using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Enforces ADR 0043's third invariant — every catalogued command has a CLI verb, and the reverse — by
/// pinning <see cref="CommandCatalog.All"/> and <see cref="CliVerbCatalog.All"/> equal by command name,
/// alongside the shape both catalogs depend on holding: unique command names, unique request types, and
/// a stable, non-colliding refusal-code vocabulary. Also pins the source boundary Task 7 draws: no
/// command file renders to the console or crosses into a CLI namespace, and the CLI never reaches into
/// the store or LibLCM directly.
/// </summary>
public sealed class CommandCatalogParityTests
{
    [Fact]
    public void EveryCataloguedCommandHasExactlyOneCliVerb()
    {
        var commands = CommandCatalog.All.Select(command => command.Name).Order().ToArray();
        var verbs = CliVerbCatalog.All.Select(verb => verb.CommandName).Order().ToArray();
        Assert.Equal(commands, verbs);
    }

    [Fact]
    public void CommandCatalogNamesAreUnique()
    {
        var names = CommandCatalog.All.Select(command => command.Name).ToList();
        Assert.Equal(names.Distinct(StringComparer.Ordinal).Count(), names.Count);
    }

    [Fact]
    public void CommandCatalogRequestTypesAreUnique()
    {
        var requestTypes = CommandCatalog.All.Select(command => command.RequestType).ToList();
        Assert.Equal(requestTypes.Distinct().Count(), requestTypes.Count);
    }

    [Fact]
    public void CliVerbCatalogCommandNamesAreUnique()
    {
        var commandNames = CliVerbCatalog.All.Select(verb => verb.CommandName).ToList();
        Assert.Equal(commandNames.Distinct(StringComparer.Ordinal).Count(), commandNames.Count);
    }

    // Pinned so a colliding or typo'd near-copy of an existing refusal code shows up as a named diff.
    private static readonly string[] ExpectedRefusalCodes =
    {
        "apply.drift", "apply.dry-run-missing", "apply.not-ready", "apply.project-in-use",
        "apply.reconciliation-needed",
        "assessment.invalid-id", "assessment.not-found",
        "baseline.busy", "baseline.copy-unloadable", "baseline.owned-root-violation",
        "baseline.source-incomplete",
        "comparison.assessment-not-found", "comparison.refused",
        "config.invalid",
        "corpus.bundle-invalid", "corpus.document-invalid", "corpus.document-not-found", "corpus.invalid",
        "corpus.not-found",
        "draft.invalid", "draft.name-collision", "draft.not-found",
        "job.already-finished", "job.dry-run-incomplete", "job.invalid-id", "job.invalid-move",
        "job.move-target-not-found", "job.no-assessments", "job.not-finished", "job.not-found",
        "job.wait-timeout",
        "operation.cascading-delete", "operation.invalid-dependency", "operation.invalid-id",
        "operation.invalid-target", "operation.invalid-writing-system",
        "project.busy", "project.invalid", "project.not-found", "project.refused",
        "proposal.inconsistent", "proposal.invalid-id", "proposal.invalid-status", "proposal.not-found",
        "proposal.split-duplicate-operation",
        "report.assessment-not-found", "report.invalid-kind", "report.refused",
        "selection.empty", "selection.text-not-found",
        "store.inconsistent", "store.unsupported",
    };

    [Fact]
    public void RefusalCodesDeclaredInCommandsAreUniqueAndWellFormed()
    {
        var codes = RefusalCodeLiteralsIn("SIL.Motif.Commands");

        Assert.Equal(
            ExpectedRefusalCodes.Order(StringComparer.Ordinal),
            codes.Order(StringComparer.Ordinal));
    }

    // Every dotted, lower-case, hyphenated code literal (e.g. "draft.not-found") in a project's source.
    private static HashSet<string> RefusalCodeLiteralsIn(string projectName)
    {
        var codePattern = new Regex(@"""([a-z][a-z]*(?:\.[a-z][a-z-]*)+)""", RegexOptions.None);
        var root = Path.Combine(RepoPaths.FindRepoRoot(), "src", projectName);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        foreach (Match match in codePattern.Matches(File.ReadAllText(file)))
            codes.Add(match.Groups[1].Value);
        return codes;
    }

    /// <summary>
    /// No command file renders to the console, serializes JSON itself, or crosses into a CLI namespace
    /// (ADR 0043 decision 1 and 2) — the <c>CommandResult</c>/<c>error: </c> guard for the same boundary
    /// lives beside <see cref="SIL.Motif.Tests.Commands.ProposalCommandOutcomeTests.CommandsRendersNothingItself"/>.
    /// </summary>
    [Fact]
    public void CommandsSourceHasNoConsoleSerializationOrCliReference()
    {
        var commandsRoot = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.Commands");
        var offendingLines = Directory.EnumerateFiles(commandsRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("Console.", StringComparison.Ordinal)
                || line.Contains("ProjectionJson.Serialize", StringComparison.Ordinal)
                || line.Contains("SIL.Motif.Cli", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offendingLines);
    }

    /// <summary>The CLI never opens the store or touches LibLCM directly; only Commands may (ADR 0043).</summary>
    [Fact]
    public void CliSourceHasNoStoreOrLibLcmReference()
    {
        var cliRoot = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.Cli");
        var offendingLines = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("Microsoft.Data.Sqlite", StringComparison.Ordinal)
                || line.Contains("SIL.LCModel", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offendingLines);
    }
}
