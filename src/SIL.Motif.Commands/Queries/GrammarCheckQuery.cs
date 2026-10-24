using SIL.Motif.Host.Baselines;
using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Host.Preferences;
using SIL.Motif.Commands.Store;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;
using SIL.Motif.Projection.Grammar;
using SIL.LCModel;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Checks a project's grammar as a whole through <c>pangloss grammar-health</c>, reading findings from the
/// project's current Baseline. Never involves a Text, a word, or an Assessment.
/// </summary>
/// <remarks>The report carries its own subject names and FieldWorks links and is stored with the Baseline.</remarks>
public static class GrammarCheckQuery
{
    /// <summary>Checks the project's current Baseline grammar with the installed PanGloss executable.</summary>
    public static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request, CancellationToken cancellationToken = default) =>
        Query(request, PanGlossExecutable.TryLocate(), cancellationToken);

    /// <summary>Checks the project's current Baseline grammar with an explicitly selected parser.</summary>
    /// <param name="request">The project whose Baseline grammar to check.</param>
    /// <param name="cancellationToken">Cancels the grammar check.</param>
    /// <param name="parserPath">The parser to run, or <see langword="null"/> when none is available.</param>
    public static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request, string? parserPath, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker(parserPath);
        return Query(request, invoker, cancellationToken, ParserStamp(parserPath));
    }

    /// <summary>Checks through an explicitly supplied invoker, which allows tests to stand in for PanGloss.</summary>
    /// <param name="request">The project whose Baseline grammar to check.</param>
    /// <param name="invoker">Runs the grammar check.</param>
    /// <param name="cancellationToken">Cancels the grammar check.</param>
    /// <param name="parserStamp">Identifies the parser build recorded with the findings.</param>
    /// <param name="defaultChecks">
    /// The per-user Default proposal checks the request falls back to, or null for the installed user's.
    /// </param>
    internal static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request,
        IPanGlossInvoker invoker,
        CancellationToken cancellationToken,
        string? parserStamp = null,
        IDefaultProposalChecksStore? defaultChecks = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invoker);

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrentPublished(workspaceKey);
            if (baseline is null)
                return CommandOutcome<GrammarCheckResponse>.Refused(
                    BaselineMissingRefusal.ForProject(request.ProjectPath));

            var storedAnalyses = request.StoredAnalyses ??
                (defaultChecks ?? FileDefaultProposalChecksStore.ForInstallation()).StoredAnalyses;
            var outcome = invoker.RunAsync(
                    new PanGlossRequest.GrammarHealth(baseline.FwDataPath, project.FieldWorksProjectIdentity,
                        storedAnalyses),
                    "grammar-check:" + workspaceKey,
                    cancellationToken)
                .GetAwaiter().GetResult();
            if (outcome is PanGlossOutcome.Cancelled)
                return CommandOutcome<GrammarCheckResponse>.Refused(Cancelled(request.ProjectPath));
            if (outcome is PanGlossOutcome.Refused compileFailure &&
                ParserExecutionRefusal.From("grammarcheck.parser-refused", request.ProjectPath,
                    compileFailure) is { ParserDiagnostic: not null } compileRefusal)
            {
                new ParserRefusalRepository(database).Save(baseline.Token, compileRefusal);
                return CommandOutcome<GrammarCheckResponse>.Refused(compileRefusal);
            }
            var parserExitedNonzero = outcome is PanGlossOutcome.Refused;
            var output = outcome switch
            {
                PanGlossOutcome.Completed completed => completed.Output,
                PanGlossOutcome.Refused refused when !string.IsNullOrWhiteSpace(refused.StandardOutput) =>
                    refused.StandardOutput,
                _ => null,
            };
            if (output is null)
            {
                var refusal = ParserRefusal(outcome, request.ProjectPath);
                if (refusal.Reason != FailureReason.Cancelled)
                    new ParserRefusalRepository(database).Save(baseline.Token, refusal);
                return CommandOutcome<GrammarCheckResponse>.Refused(refusal);
            }

            GrammarWarning[] findings;
            GrammarWarningSummary[] summary;
            string locale;
            try
            {
                var parsed = GrammarHealthReportReader.ReadResponse(output);
                findings = parsed.Findings.ToArray();
                summary = parsed.Summary.ToArray();
                locale = parsed.Locale!;
            }
            catch (GrammarHealthReportReader.UnsupportedGrammarHealthSchemaException exception)
            {
                return CommandOutcome<GrammarCheckResponse>.Refused(new Refusal(
                    "grammarcheck.unsupported-schema", FailureReason.Refused,
                    $"PanGloss grammar-health schema version {exception.Version} is unsupported; this Motif build " +
                        $"expects version {PanGlossInterfaceVersions.GrammarHealthSchemaVersion}. Update PanGloss and Motif together.",
                    Fact(("projectPath", request.ProjectPath),
                        ("actualSchemaVersion", exception.Version.ToString(CultureInfo.InvariantCulture)),
                        ("expectedSchemaVersion", PanGlossInterfaceVersions.GrammarHealthSchemaVersion.ToString(CultureInfo.InvariantCulture)))));
            }
            catch (JsonException exception)
            {
                return CommandOutcome<GrammarCheckResponse>.Refused(new Refusal(
                    "grammarcheck.malformed-findings", FailureReason.Refused,
                    $"pangloss grammar-health wrote a report Motif could not read: {exception.Message}",
                    Fact(("projectPath", request.ProjectPath),
                        ("parserSourceTag", PanGlossInterfaceVersions.SourceTag),
                        ("expectedSchemaVersion", PanGlossInterfaceVersions.GrammarHealthSchemaVersion.ToString(CultureInfo.InvariantCulture)),
                        ("parseError", exception.Message))));
            }

            if (parserExitedNonzero && !findings.Any(finding => finding.Severity == GrammarDiagnosticLevel.Error))
                return CommandOutcome<GrammarCheckResponse>.Refused(ParserRefusal(outcome, request.ProjectPath));

            BaselineReadCache? reader = null;
            GrammarCheckResponse response;
            try
            {
                LcmCache Cache() => (reader ??= BaselineReadCache.Open(baseline.FwDataPath)).Cache;
                var inventory = ProjectGrammarInventoryReader.Read(Cache());
                var projected = new GrammarCheckResponse(findings, HasBaseline: true)
                {
                    Summary = summary,
                    Locale = locale,
                    CheckedLexemeCount = inventory.LexemeCount,
                    CheckedRuleCount = inventory.RuleCount,
                    Advisories = EmptyLexiconAdvisory(inventory),
                };
                projected = RequiredSlotWarnings.AddTo(projected, Cache());
                response = projected with
                {
                    Findings = projected.Findings.Select(finding => WithReach(finding, Cache)).ToArray(),
                };
                var baselineToken = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
                var selectionSha256 = SelectionDigest(database, Cache, baselineToken);
                response = new GrammarCheckRepository(database).Save(baselineToken, selectionSha256, parserStamp,
                    response, baseline.SourceLastWriteUtc);
            }
            finally
            {
                reader?.Dispose();
            }
            return CommandOutcome<GrammarCheckResponse>.Success(
                WarningWordsQuery.WithYourWords(database, project, response, baseline.Token));
        });
    }

    // Reach is read from the checked Baseline once, so a later read joins it to words without opening the project.
    private static GrammarWarning WithReach(GrammarWarning finding, Func<LcmCache> cache) => finding with
    {
        Subject = finding.Subject.Select(part => part with { Reach = WarningReachReader.Reach(part, cache) }).ToArray(),
    };

    private static IReadOnlyList<GrammarCheckAdvisory> EmptyLexiconAdvisory(ProjectGrammarInventory inventory) =>
        inventory.LexemeCount == 0 && inventory.StemLexemeCount == 0
            ? [new GrammarCheckAdvisory(GrammarCheckAdvisoryCodes.EmptyLexicon,
                "This Baseline has 0 lexical entries and 0 stem lexemes; add lexical entries before interpreting " +
                "parser findings as evidence about how words parse.")
            { LexemeCount = inventory.LexemeCount, StemLexemeCount = inventory.StemLexemeCount }]
            : [];

    private static string SelectionDigest(SIL.Motif.Host.Store.MotifDatabase database, Func<LcmCache> cache,
        string baselineToken)
    {
        var saved = new NamedSelectionRepository(database).GetDefault();
        if (saved is null) return string.Empty;
        var request = new SelectionRequest(false, saved.TextIds, saved.AddedWords, false, null);
        var composed = SelectionComposer.Compose(cache(), request, new AssessmentRepository(database), baselineToken);
        return composed.Succeeded ? composed.Value!.Selection.Sha256 : string.Empty;
    }

    private static string? ParserStamp(string? parserPath)
    {
        if (parserPath is not { } exe || !File.Exists(exe)) return null;
        // Path, size, and write time distinguish a rebuilt or replaced parser executable.
        var info = new FileInfo(exe);
        return string.Create(CultureInfo.InvariantCulture, $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}");
    }

    private static Refusal Cancelled(string projectPath) => new(
        "grammarcheck.cancelled", FailureReason.Cancelled, "The grammar check was cancelled.",
        Fact(("projectPath", projectPath)));

    private static Refusal ParserRefusal(PanGlossOutcome outcome, string projectPath) => outcome switch
    {
        PanGlossOutcome.Unavailable unavailable => new Refusal(
            "grammarcheck.parser-unavailable", FailureReason.Refused, unavailable.Message,
            ParserNotFoundFact.Mark(Fact(("projectPath", projectPath)), unavailable.ExecutableMissing)),
        PanGlossOutcome.MemoryLimit memoryLimit => ParserMemoryRefusal.From(projectPath, memoryLimit.Kill),
        PanGlossOutcome.TimedOut timedOut => new Refusal(
            "grammarcheck.timed-out", FailureReason.Refused, timedOut.Message,
            Fact(("projectPath", projectPath))),
        PanGlossOutcome.Refused refused => ParserExecutionRefusal.From(
            "grammarcheck.parser-refused", projectPath, refused),
        _ => new Refusal(
            "grammarcheck.parser-unavailable", FailureReason.Refused, outcome.Message,
            Fact(("projectPath", projectPath))),
    };

    private static Dictionary<string, string> Fact(params (string Key, string Value)[] facts)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in facts) dictionary[key] = value;
        return dictionary;
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;
}
