using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
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
/// <see cref="StatsRequest.ForwardedArguments"/> to the invocation untouched. A new PanGloss filter or
/// grouping is usable through Motif the day it ships, with no change here.
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
    /// <summary>Queries statistics through a real parser invocation.</summary>
    public static CommandOutcome<StatsCommandResponse> Stats(
        StatsRequest request, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker();
        return Run(request, invoker, cancellationToken);
    }

    /// <summary>
    /// Queries statistics through an explicitly supplied invoker — a fake stands in for PanGloss in tests.
    /// Admission and containment are the invoker's, so this command holds no queue and no governor.
    /// </summary>
    internal static CommandOutcome<StatsCommandResponse> Run(
        StatsRequest request, IPanGlossInvoker invoker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invoker);
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

            var forwarded = request.Output == StatsOutputKind.JsonRows
                ? AppendFormatJsonl(request.ForwardedArguments)
                : request.ForwardedArguments;

            var outcome = invoker.RunAsync(
                    new PanGlossRequest.Stats(baseline.FwDataPath, assessment.CachePath, forwarded),
                    "stats:" + workspaceKey, cancellationToken)
                .GetAwaiter().GetResult();
            if (outcome is not PanGlossOutcome.Completed completed)
                return CommandOutcome<StatsCommandResponse>.Refused(ParserRefusal(outcome, request.ProjectPath));

            return CommandOutcome<StatsCommandResponse>.Success(request.Output == StatsOutputKind.Text
                ? new StatsCommandResponse(
                    assessment.AssessmentId, baseline.FwDataPath, assessment.CachePath, completed.Output, null)
                : new StatsCommandResponse(
                    assessment.AssessmentId, baseline.FwDataPath, assessment.CachePath, null,
                    ParseJsonRows(completed.Output)));
        });
    }

    // PanGloss owns --format's meaning; Motif checks only for its presence, to refuse rather than override.
    private static bool ContainsFormatFlag(IReadOnlyList<string> forwardedArguments) =>
        forwardedArguments.Any(argument =>
            argument == "--format" || argument.StartsWith("--format=", StringComparison.Ordinal));

    private static IReadOnlyList<string> AppendFormatJsonl(IReadOnlyList<string> forwarded) =>
        [.. forwarded, "--format", "jsonl"];

    // One place turns the invocation's outcome into this command's refusal vocabulary.
    private static Refusal ParserRefusal(PanGlossOutcome outcome, string projectPath) => outcome switch
    {
        PanGlossOutcome.Cancelled => new Refusal(
            "stats.cancelled", FailureReason.Refused, "The statistics query was cancelled.",
            Fact(("projectPath", projectPath))),
        PanGlossOutcome.Unavailable unavailable => new Refusal(
            "stats.parser-unavailable", FailureReason.Refused, unavailable.Message,
            Fact(("projectPath", projectPath))),
        PanGlossOutcome.TimedOut timedOut => new Refusal(
            "stats.timed-out", FailureReason.Refused, timedOut.Message,
            Fact(("projectPath", projectPath),
                ("capMinutes", timedOut.Cap.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture)))),
        PanGlossOutcome.Refused refused => new Refusal(
            "stats.parser-refused", FailureReason.Refused, refused.Message,
            Fact(("projectPath", projectPath), ("exitCode", refused.ExitCode.ToString(CultureInfo.InvariantCulture)))),
        _ => new Refusal(
            "stats.parser-unavailable", FailureReason.Refused, outcome.Message,
            Fact(("projectPath", projectPath))),
    };

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
