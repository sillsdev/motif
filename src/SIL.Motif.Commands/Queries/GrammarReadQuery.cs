using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Projection.Grammar;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads typed, authored project grammar objects from the current Baseline.</summary>
public static class GrammarReadQuery
{
    private const int MaximumOffset = 100_000;
    private const int MaximumPageSize = 100;

    /// <summary>Reads one kind of grammar object without opening or changing the live project.</summary>
    public static CommandOutcome<GrammarReadResponse> Query(GrammarReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind))
            return Invalid("Choose a supported grammar object kind.");
        if (request.Id is not null && (string.IsNullOrWhiteSpace(request.Id) ||
                !CanonicalId.TryParse(request.Id, out _)))
            return Invalid("An exact id must be a canonical entity id.");
        if (request.Id is not null && request.Query is not null)
            return Invalid("Choose either an exact id or a name query, not both.");
        if (request.Id is not null && (request.Exact is not null || request.MorphType is not null))
            return Invalid("An exact id is read on its own; drop the exact form and morph type filters.");
        if (request.Exact is not null && request.Query is not null)
            return Invalid("Choose either an exact form or a name query, not both.");
        if (request.Exact is not null && string.IsNullOrWhiteSpace(request.Exact))
            return Invalid("An exact form cannot be empty.");
        if (request.MorphType is not null && request.Kind != GrammarReadKind.lexical_entry)
            return Invalid("A morph type filter applies to lexical entries only.");
        var proposalId = string.IsNullOrWhiteSpace(request.At) || request.At == "baseline" ? null : request.At;
        if (request.At is not null && string.IsNullOrWhiteSpace(request.At))
            return Invalid("--at cannot be empty; give 'baseline' or a Proposal id.");
        if (proposalId is not null && !CanonicalId.TryParse(proposalId, out _))
            return Invalid($"'{request.At}' is neither 'baseline' nor a Proposal id.");
        if (request.Offset is < 0 or > MaximumOffset)
            return Invalid($"The offset must be between 0 and {MaximumOffset}.");
        if (request.Limit is < 1 or > MaximumPageSize)
            return Invalid($"The limit must be between 1 and {MaximumPageSize}.");

        return ProjectStoreCommand.ReadExisting(request.ProjectPath,
            CommandOutcome<GrammarReadResponse>.Refused(new Refusal("grammar-read.baseline-missing", FailureReason.NotFound, "Capture a Baseline before reading project grammar objects.")),
            (database, project) =>
        {
            if (proposalId is not null)
            {
                var traced = TracedGrammar.Resolve(database, project, proposalId);
                if (!traced.Succeeded) return CommandOutcome<GrammarReadResponse>.Refused(traced.Refusal!);
                using var trialReader = BaselineReadCache.Open(traced.Value!.FwDataPath);
                return Read(trialReader.Cache, request).Map(page => page with
                {
                    At = traced.Value.TracedAgainst,
                    AtLabel = $"the grammar the latest Trial of this Proposal ran against (Trial job {traced.Value.TrialJobId}), " +
                        "with the Proposal's edits applied; not the Baseline",
                });
            }
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refused("grammar-read.baseline-missing", FailureReason.NotFound,
                    "Capture a Baseline before reading project grammar objects.");
            if (!File.Exists(baseline.FwDataPath))
                return Refused("grammar-read.baseline-unavailable", FailureReason.StoreInconsistent,
                    "The current Baseline file is missing. Capture a new Baseline before reading grammar objects.");

            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            var read = Read(reader.Cache, request);
            if (!read.Succeeded) return read;
            var response = read.Value!;
            var projectSave = File.Exists(project.FullFwDataPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero)
                : (DateTimeOffset?)null;
            return CommandOutcome<GrammarReadResponse>.Success(response with
            {
                Baseline = baseline.Token,
                SourceLastWriteUtc = baseline.SourceLastWriteUtc,
                ProjectLastWriteUtc = projectSave,
                IsStale = projectSave > baseline.SourceLastWriteUtc,
                At = "baseline",
            });
        });
    }

    private static CommandOutcome<GrammarReadResponse> Read(LcmCache cache, GrammarReadRequest request)
    {
        if (request.MorphType is not null &&
            GrammarReadProjectionBuilder.UnknownMorphTypes(cache, request.MorphType) is { Count: > 0 } unknown)
            return Invalid($"Unknown morph type {string.Join(", ", unknown.Select(name => $"'{name}'"))}. " +
                $"Morph types are: {string.Join(", ", GrammarReadProjectionBuilder.MorphTypeNames(cache))}.");
        return CommandOutcome<GrammarReadResponse>.Success(GrammarReadProjectionBuilder.Build(cache, request));
    }

    private static CommandOutcome<GrammarReadResponse> Invalid(string message) =>
        Refused("grammar-read.invalid-request", FailureReason.InvalidArgument, message);

    private static CommandOutcome<GrammarReadResponse> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<GrammarReadResponse>.Refused(new Refusal(code, reason, message));
}
