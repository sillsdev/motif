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
/// a stable, non-colliding refusal-code vocabulary. Also pins the command/CLI source boundary: no
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
    public void CliVerbCatalogPreservesTheCommandSurfacePartition()
    {
        var surfaceByCommand = CommandCatalog.All.ToDictionary(command => command.Name);

        foreach (var surface in Enum.GetValues<CommandSurface>())
        {
            var commands = CommandCatalog.All
                .Where(command => command.Surface == surface)
                .Select(command => command.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var verbs = CliVerbCatalog.All
                .Where(verb => surfaceByCommand[verb.CommandName].Surface == surface)
                .Select(verb => verb.CommandName)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(commands, verbs);
        }
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
        "apply.applied-content-mismatch", "apply.change-no-longer-fits", "apply.change-uncertain", "apply.drift", "apply.dry-run-missing",
        "apply.changes-changed", "apply.not-ready", "apply.project-in-use",
        "apply.regression",
        "apply.reopen-failed",
        "apply.reconciliation-needed",
        "assess.baseline-changed", "assess.invalid-limit", "assess.invalid-replacement", "assess.invocation-inconsistent", "assess.measurements-incomplete",
        "assess.parser-unavailable", "assess.unsupported-kind",
        "selection.default-missing", "selection.invalid", "selection.retry-source-required", "selection.retry-source-not-found", "selection.retry-source-invalid",
        "selection.retry-source-mismatch", "selection.retry-source-project-mismatch", "selection.retry-source-without-retry",
        "assessment.aggregate-unavailable", "assessment.baseline-unavailable", "assessment.cancelled", "assessment.invalid-evidence", "assessment.invalid-id", "assessment.not-found",
        "baseline.busy", "baseline.copy-unloadable", "baseline.owned-root-violation",
        "baseline.source-incomplete", "baseline.text-words-unreadable",
        "change.analysis-identity-required", "change.analysis-owner-invalid",
        "change.assessment-incomplete", "change.assessment-kind", "change.assessment-missing",
        "change.assessment-required",
        "change.assessment-stale",
        "change.assessment-word-missing",
        "change.baseline-missing", "change.cannot-compose", "change.invalid-identity", "change.no-effect",
        "change.not-found", "change.occurrence-unavailable", "change.occurrence-wordform-mismatch",
        "change.project-saving", "change.reading-missing", "change.reconfirm-not-allowed", "change.reconfirm-unneeded", "change.refresh-required", "change.revision-conflict", "change.scope-invalid", "change.slot-occupied",
        "change.stored-analysis-missing", "change.text-unavailable", "change.wordform-ambiguous", "change.wordform-changed",
        "change.wordform-missing",
        "review.assessment-not-found", "review.numbers-unavailable",
        "review.wrong-assessment-kind",
        "selection-reader.busy", "selection-reader.cancelled", "selection-reader.disposed", "selection-reader.invalid",
        "selection-reader.no-baseline", "selection-reader.ownership-limit", "selection-reader.presentation-changed",
        "selection-reader.project-path-required",
        "selection-reader.range-invalid",
        "comparison.assessment-not-found", "comparison.refused",
        "config.invalid",
        "corpus.bundle-invalid", "corpus.document-invalid", "corpus.document-not-found", "corpus.invalid",
        "corpus.not-found",
        "current-evidence.baseline-unavailable",
        "draft.invalid", "draft.name-collision", "draft.not-found", "draft.revision-conflict",
        "handoff.cancelled", "handoff.destination-exists", "handoff.invocation-mismatch", "handoff.invocation-not-found",
        "handoff.invocation-required", "handoff.parser-unavailable", "handoff.source-unavailable",
        "handoff.statistics-unavailable", "handoff.text-not-found",
        "handoff.trace-baseline-unavailable", "handoff.trace-unavailable",
        "job.already-finished", "job.dry-run-incomplete", "job.invalid-id", "job.invalid-move", "job.invalid-words",
        "job.assessments-inconsistent", "job.move-target-not-found", "job.no-assessments", "job.not-finished", "job.not-found",
        "job.wait-cancelled", "job.wait-timeout",
        "operation.cascading-delete", "operation.invalid-dependency", "operation.invalid-id",
        "operation.invalid-target", "operation.invalid-writing-system",
        "initialization.analysis-writing-system-required", "initialization.cancelled", "initialization.confirmation-required", "initialization.not-saved",
        "initialization.outcome-unknown", "initialization.recovery-copy-failed",
        "judgment.field-ambiguous", "judgment.field-incompatible",
        "parsimony.assessment-baseline-mismatch", "parsimony.bundle-busy", "parsimony.bundle-invalid",
        "parsimony.bundle-not-found",
        "parsimony.candidate-failed",
        "parsimony.candidate-result-invalid", "parsimony.candidate-result-missing",
        "parsimony.decision-already-staged", "parsimony.disposition-request-invalid",
        "parsimony.disposition-retraction-request-invalid", "parsimony.disposition-revision-request-invalid",
        "parsimony.dry-run-evidence-missing", "parsimony.dry-run-required",
        "parsimony.finding-ambiguous", "parsimony.finding-not-in-report", "parsimony.invalid-assessment",
        "parsimony.invalid-catalog-request", "parsimony.invalid-latest-request",
        "parsimony.invalid-measure",
        "parsimony.invalid-scope", "parsimony.invalid-view-request", "parsimony.job-failed",
        "parsimony.lease-lost", "parsimony.measure-unavailable", "parsimony.negative-confirmation-required", "parsimony.negative-request-invalid",
        "parsimony.no-report", "parsimony.record-type-id-invalid", "parsimony.report-baseline-mismatch",
        "parsimony.report-bundle-mismatch",
        "parsimony.report-damaged", "parsimony.report-id-required", "parsimony.report-not-found",
        "parsimony.result-invalid", "parsimony.result-missing",
        "parsimony.retirement-expectations-unavailable", "parsimony.selection-unavailable",
        "parsimony.view-item-not-found",
        "project.busy", "project.in-use", "project.invalid", "project.not-found", "project.operation-io", "project.refused",
        "project.saving", "project.unloadable", "project.unreadable",
        "project.store-io",
        "preflight.unavailable",
        "proposal.already-applied", "proposal.inconsistent", "proposal.invalid-id", "proposal.invalid-status",
        "proposal.not-found",
        "proposal.split-duplicate-operation",
        "report.assessment-not-found", "report.invalid-kind", "report.refused",
        "retirement-review.draft-ambiguous", "retirement-review.draft-not-found",
        "retirement-review.evidence-inconsistent", "retirement-review.invalid-draft", "retirement-review.stale-dry-run",
        "selection.empty", "selection.invalid-limits", "selection.revision-conflict", "selection.text-not-found",
        "stats.invalid-evidence", "stats.no-evidence", "stats.wrong-kind",
        "stats.cancelled", "stats.format-conflict", "stats.no-assessment",
        "stats.no-cache", "stats.parser-refused", "stats.parser-unavailable",
        "stats.timed-out",
        "timing.invalid-override", "timing.invalid-request", "timing.invalid-word-set",
        "timing.no-assessment", "timing.no-baseline", "timing.override-not-found",
        "timing.wrong-kind", "timing.word-set-not-found",
        "trial.changes-changed", "trial.measurement-incomplete", "trial.nothing-pending",
        "inspect.invalid-request",
        "uses.invalid-request", "uses.no-assessment",
        "store.inconsistent", "store.other-version", "store.unsupported",
        "texts.words-cancelled",
        "texts.evidence-changed", "texts.occurrence-not-found",
        "grammarcheck.cancelled", "grammarcheck.malformed-findings", "grammarcheck.unsupported-schema", "grammarcheck.parser-refused",
        "grammarcheck.parser-unavailable", "grammarcheck.timed-out",
        "wordtrace.cancelled", "wordtrace.diagnostic-unreadable", "wordtrace.malformed-diagnostic", "wordtrace.malformed-output", "wordtrace.no-baseline", "wordtrace.parser-refused",
        "wordtrace.parser-unavailable",
        "word.read-state-cancelled",
        "word.read-state-invalid",
        "word-context.baseline-changed", "word-context.baseline-unavailable", "word-context.empty-word",
    };

    /// <summary>
    /// Every refusal code a command declares is registered here, and nothing is registered that no command
    /// declares.
    /// </summary>
    /// <remarks>
    /// Literals ending in a file extension are skipped, because a filename a command writes is shaped
    /// exactly like a refusal code. That filter is safe in the direction that matters: a genuine code
    /// wrongly skipped still fails this test, since <see cref="ExpectedRefusalCodes"/> would then name a
    /// code the scan no longer finds.
    /// </remarks>
    [Fact]
    public void RefusalCodesDeclaredInCommandsAreUniqueAndWellFormed()
    {
        var codes = RefusalCodeLiteralsIn("SIL.Motif.Commands");

        Assert.Equal(
            ExpectedRefusalCodes.Order(StringComparer.Ordinal),
            codes.Order(StringComparer.Ordinal));
    }

    // A filename is shaped like a refusal code, so the scan below would report "instructions.md" as one.
    private static readonly HashSet<string> FileExtensions = new(StringComparer.Ordinal)
    {
        "bak", "db", "fwdata", "json", "jsonl", "ldml", "lock", "md", "py", "sqlite", "txt", "xml", "zip",
    };

    // Dotted command codes and the reserved initialization codes in a project's source.
    private static HashSet<string> RefusalCodeLiteralsIn(string projectName)
    {
        var codePattern = new Regex(
            "\"((?:[a-z][a-z-]*\\.)+[a-z][a-z-]*)\"",
            RegexOptions.None);
        var root = Path.Combine(RepoPaths.FindRepoRoot(), "src", projectName);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        foreach (Match match in codePattern.Matches(File.ReadAllText(file)))
        {
            var literal = match.Groups[1].Value;
            if (!FileExtensions.Contains(literal[(literal.LastIndexOf('.') + 1)..])) codes.Add(literal);
        }
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

    /// <summary>The CLI assembly does not directly reference SQLite storage or LibLCM.</summary>
    [Fact]
    public void CliAssemblyDoesNotReferenceSqliteOrLibLcm()
    {
        var assembly = System.Reflection.Assembly.Load("motif");
        var referencedNames = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

        Assert.DoesNotContain("Microsoft.Data.Sqlite", referencedNames);
        Assert.DoesNotContain("SIL.LCModel", referencedNames);
    }
}
