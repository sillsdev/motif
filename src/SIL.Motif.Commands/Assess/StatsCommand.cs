using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// Passes a statistics query through to PanGloss's own <c>stats</c> command (design decision 5): resolves
/// which grammar and which cache to ask about, then forwards the caller's remaining arguments byte-for-byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>Motif never parses, reshapes, or second-guesses PanGloss's stats vocabulary.</b> This command
/// contributes exactly two arguments of its own — the grammar path and the cache path — and hands
/// <see cref="StatsRequest.ForwardedArguments"/> to <see cref="IPanGlossStatsQuery"/> untouched. A new
/// PanGloss filter or grouping is usable through Motif the day it ships, with no change here.
/// </para>
/// <para>
/// <b>The grammar path always comes from the project's current Baseline</b> — never from a Trial's own
/// candidate copy, even when <see cref="StatsRequest.ProposalId"/> selects a Trial's Assessment. A Trial's
/// candidate is a throwaway scratch directory deleted once its job finishes; only the per-object stats
/// cache a Trial produced is kept, keyed durably by the grammar digest it measured. The Baseline's own
/// <c>.fwdata</c> is the one grammar file this project retains on disk for PanGloss to read alongside it.
/// </para>
/// </remarks>
public static class StatsCommand
{
    /// <summary>Queries statistics, resolving the managed root the real installation uses.</summary>
    public static CommandOutcome<StatsCommandResponse> Stats(
        StatsRequest request, CancellationToken cancellationToken = default) =>
        Run(request, () => new PanGlossStatsQueryProcess(), cancellationToken);

    /// <summary>Queries statistics against an explicitly supplied collaborator — a fake stands in for PanGloss in tests.</summary>
    internal static CommandOutcome<StatsCommandResponse> Run(
        StatsRequest request, Func<IPanGlossStatsQuery> statsQueryFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(statsQueryFactory);
        ArgumentNullException.ThrowIfNull(request.ForwardedArguments);

        if (request.Output == StatsOutputKind.JsonRows && ContainsFormatFlag(request.ForwardedArguments))
        {
            return CommandOutcome<StatsCommandResponse>.Refused(new Refusal(
                "stats.format-conflict", FailureReason.InvalidArgument,
                "The forwarded arguments already name --format; --json requests PanGloss's JSONL rows " +
                "itself and cannot override the caller's own choice.",
                Fact(("projectPath", request.ProjectPath))));
        }

        CanonicalId? proposalId = null;
        if (request.ProposalId is not null)
        {
            if (!CanonicalId.TryParse(request.ProposalId, out var parsed, out var idError))
            {
                return CommandOutcome<StatsCommandResponse>.Refused(new Refusal(
                    "stats.invalid-proposal-id", FailureReason.InvalidArgument, idError!,
                    Fact(("proposalId", request.ProposalId))));
            }
            proposalId = parsed;
        }

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is null)
            {
                return CommandOutcome<StatsCommandResponse>.Refused(new Refusal(
                    "stats.no-baseline", FailureReason.NotFound,
                    "No Baseline has been captured for this project yet; there is no grammar to query.",
                    Fact(("projectPath", request.ProjectPath))));
            }

            var assessments = new AssessmentRepository(database);
            var kind = AssessmentKind.ObjectTiming.ToStoredKind();
            var candidates = proposalId is null
                ? assessments.ListBaselineAssessments(kind)
                : assessments.ListByProposal(proposalId.Value)
                    .Where(record => record.Kind.IsStoredKind(AssessmentKind.ObjectTiming)).ToList();

            var assessment = candidates.Count > 0 ? candidates[^1] : null;
            if (assessment is null)
            {
                return CommandOutcome<StatsCommandResponse>.Refused(new Refusal(
                    "stats.no-assessment", FailureReason.NotFound,
                    proposalId is null
                        ? "No Baseline Assessment with per-object statistics has been recorded yet; run " +
                          "`motif assess` first."
                        : $"No Trial Assessment with per-object statistics has been recorded for Proposal " +
                          $"'{request.ProposalId}'.",
                    Fact(("projectPath", request.ProjectPath))));
            }

            if (assessment.CachePath is null)
            {
                return CommandOutcome<StatsCommandResponse>.Refused(new Refusal(
                    "stats.no-cache", FailureReason.Refused,
                    "The resolved Assessment was recorded without a per-object statistics cache and cannot " +
                    "be queried.",
                    Fact(("assessmentId", assessment.AssessmentId))));
            }

            IPanGlossStatsQuery statsQuery;
            try
            {
                statsQuery = statsQueryFactory();
            }
            catch (ParserUnavailableException ex)
            {
                return CommandOutcome<StatsCommandResponse>.Refused(new Refusal(
                    "stats.parser-unavailable", FailureReason.Refused, ex.Message,
                    Fact(("projectPath", request.ProjectPath))));
            }

            var forwarded = request.Output == StatsOutputKind.JsonRows
                ? AppendFormatJsonl(request.ForwardedArguments)
                : request.ForwardedArguments;

            PanGlossStatsOutput output;
            try
            {
                output = statsQuery.QueryAsync(baseline.FwDataPath, assessment.CachePath, forwarded, cancellationToken)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<StatsCommandResponse>.Refused(new Refusal(
                    "stats.cancelled", FailureReason.Refused, "The statistics query was cancelled.",
                    Fact(("projectPath", request.ProjectPath))));
            }

            return CommandOutcome<StatsCommandResponse>.Success(request.Output == StatsOutputKind.Text
                ? new StatsCommandResponse(
                    assessment.AssessmentId, baseline.FwDataPath, assessment.CachePath, output.StandardOutput, null)
                : new StatsCommandResponse(
                    assessment.AssessmentId, baseline.FwDataPath, assessment.CachePath, null,
                    ParseJsonRows(output.StandardOutput)));
        });
    }

    // PanGloss owns --format's meaning; Motif checks only for its presence, to refuse rather than override.
    private static bool ContainsFormatFlag(IReadOnlyList<string> forwardedArguments) =>
        forwardedArguments.Any(argument => argument == "--format");

    private static IReadOnlyList<string> AppendFormatJsonl(IReadOnlyList<string> forwarded) =>
        [.. forwarded, "--format", "jsonl"];

    // One row per line, cloned so each JsonElement outlives the JsonDocument that produced it.
    private static IReadOnlyList<JsonElement> ParseJsonRows(string jsonl)
    {
        var rows = new List<JsonElement>();
        using var reader = new StringReader(jsonl);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            rows.Add(document.RootElement.Clone());
        }
        return rows;
    }

    private static Dictionary<string, string> Fact(params (string Key, string Value)[] facts)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in facts) dictionary[key] = value;
        return dictionary;
    }

    private static string ResolveProductVersion() =>
        typeof(StatsCommand).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
