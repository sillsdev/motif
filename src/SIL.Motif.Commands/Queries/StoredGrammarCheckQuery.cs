using System;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Store;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The grammar check stored for the current Baseline, or null when nobody has requested one.
/// </summary>
public sealed record StoredGrammarCheckResponse(GrammarCheckResponse? Check, Refusal? LastParserRefusal = null);

/// <summary>
/// Reads grammar findings from the Motif store without starting PanGloss, each with the Selection's words it
/// touches in the stored Parse all words.
/// </summary>
public static class StoredGrammarCheckQuery
{
    /// <summary>Reads the most recent check for the project's current Baseline.</summary>
    public static CommandOutcome<StoredGrammarCheckResponse> Query(GrammarCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            // The project's own check needs only the current Baseline; resolving a set would re-read its summary.
            if (request.ProposalId is null) return QueryCurrentWithoutAssessment(database, project);
            var selected = EvidenceSets.ForProposal(database, project, request.ProposalId);
            if (!selected.Succeeded)
                return CommandOutcome<StoredGrammarCheckResponse>.Refused(selected.Refusal!);
            return selected.Value is { } set
                ? Query(database, project, set)
                : QueryCurrentWithoutAssessment(database, project);
        });
    }

    /// <summary>Reads a grammar check from the named Baseline and Proposal.</summary>
    public static CommandOutcome<StoredGrammarCheckResponse> Query(MotifDatabase database, ProjectLocator project,
        EvidenceSet set)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(set);
        var token = JsonSerializer.Serialize(set.Baseline, MotifJson.CreateOptions());
        var repository = new GrammarCheckRepository(database);
        GrammarCheckResponse? check;
        if (set.Origin.Kind == EvidenceOriginKind.Trial)
        {
            if (set.Origin.ProposalId is not { } proposalId || set.Origin.IntentDigest is not { } intentDigest)
                return CommandOutcome<StoredGrammarCheckResponse>.Refused(new Refusal(
                    "assessment.invalid-evidence", FailureReason.Refused,
                    "The Trial has no Proposal revision identity for its grammar check."));
            check = repository.GetLatest(token, proposalId, intentDigest);
        }
        else
        {
            check = repository.GetLatest(token);
        }
        if (check is null) return CommandOutcome<StoredGrammarCheckResponse>.Success(new(null));
        if (set.Origin.Kind == EvidenceOriginKind.Trial)
        {
            if (set.RootAssessmentId is null)
                return CommandOutcome<StoredGrammarCheckResponse>.Success(new(check));
            try
            {
                var records = ReadTrialComponents(database, set);
                var evidence = AssessmentEvidenceSet.Create(records[0], records.Skip(1).ToArray());
                check = WarningWordsQuery.WithYourWords(check,
                    evidence.Words.Select(AssessmentWordRows.FromRecorded).ToArray(), evidence.ObjectTimings);
                return CommandOutcome<StoredGrammarCheckResponse>.Success(new(check));
            }
            catch (Exception exception) when (exception is InvalidDataException or KeyNotFoundException)
            {
                return CommandOutcome<StoredGrammarCheckResponse>.Refused(new Refusal(
                    "assessment.invalid-evidence", FailureReason.Refused,
                    "The Trial's grammar check does not match its named evidence set."));
            }
        }
        return CommandOutcome<StoredGrammarCheckResponse>.Success(new(
            WarningWordsQuery.WithYourWords(database, project, check, set.Baseline),
            new ParserRefusalRepository(database).Get(set.Baseline)));
    }

    private static CommandOutcome<StoredGrammarCheckResponse> QueryCurrentWithoutAssessment(MotifDatabase database,
        ProjectLocator project)
    {
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
        if (baseline is null)
            return CommandOutcome<StoredGrammarCheckResponse>.Success(
                new(new GrammarCheckResponse([], HasBaseline: false)));
        var token = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
        var check = new GrammarCheckRepository(database).GetLatest(token);
        return CommandOutcome<StoredGrammarCheckResponse>.Success(new(
            check is null ? null : WarningWordsQuery.WithYourWords(database, project, check, baseline.Token),
            new ParserRefusalRepository(database).Get(baseline.Token)));
    }

    private static IReadOnlyList<AssessmentRecord> ReadTrialComponents(MotifDatabase database, EvidenceSet set)
    {
        var repository = new AssessmentRepository(database);
        var ids = new[] { set.RootAssessmentId }.Concat(set.ReplacementAssessmentIds).ToArray();
        var records = ids.Select(id => repository.Get(id!)).ToArray();
        if (records.Any(record => record.ProposalId?.Value != set.Origin.ProposalId ||
            record.ProposalIntentDigest != set.Origin.IntentDigest ||
            record.BaselineToken != JsonSerializer.Serialize(set.Baseline, MotifJson.CreateOptions())))
            throw new InvalidDataException("A Trial grammar check does not match its named evidence set.");
        return records;
    }
}
