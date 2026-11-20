using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>One request for read-only Trial facts.</summary>
public sealed record FormsQueryRequest(string FwDataPath, string ProductVersion, QueryRecord Query);

/// <summary>A query result and the identity and freshness of its source.</summary>
public sealed record FormsQueryResponse(string TrialId, string GrammarHash, string BundleId, bool IsStale,
    int UnresolvedVariants, QueryResult Result)
{
    /// <summary>What the facts did not measure that this query's source would otherwise present as complete.</summary>
    public IReadOnlyList<string> NotMeasured { get; init; } = [];
}

/// <summary>Runs a query over the forms, stems, uses or objects source of the last completed Trial's materialized facts.</summary>
public static class FormsQueryCommand
{
    /// <summary>Reads the current Trial and evaluates a closed query record.</summary>
    public static CommandOutcome<FormsQueryResponse> Run(FormsQueryRequest request, FormsQuerySnapshotCache? cache = null,
        string? parserPath = null) =>
        ProjectStoreCommand.ReadExisting(request.FwDataPath,
            CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryNoTrialRows, FailureReason.NotFound, "Query rows are built by the Parsimony evidence job; run a Trial and wait for evidence.")),
            (database, project) =>
        {
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: true);
            var identity = QuerySnapshotStore.ReadIdentity(connection, transaction);
            if (identity is null || (request.Query.TrialId is { } requested && requested != identity.Value.TrialId))
            {
                if (identity is null) cache?.Clear();
                return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryNoTrialRows, FailureReason.NotFound,
                    "Query rows are built by the Parsimony evidence job; run a Trial and wait for evidence."));
            }
            var formEntries = request.Query.Change is null ? null
                : (IReadOnlyCollection<(string Form, string Entry)>?)QuerySnapshotStore.ReadFormEntries(connection, transaction);
            CommandOutcome<FormsQueryResponse> Evaluate(QuerySnapshotStore.Stored snapshot)
            {
                try
                {
                    var query = request.Query.Fold?.Split(',', StringSplitOptions.TrimEntries).Contains(QueryFold.ProjectToken) == true
                        ? request.Query with { Fold = QueryFold.ResolveProject(request.Query.Fold, ProjectFold(project)) }
                        : request.Query;
                    var coverage = snapshot.Snapshot.Coverage ?? QueryCoverage.Complete;
                    if (NotMeasuredRefusal(query, coverage) is { } notMeasured)
                        return CommandOutcome<FormsQueryResponse>.Refused(notMeasured);
                    QueryResult result;
                    IReadOnlyList<string> hypothesisNotes = [];
                    if (query.From == QuerySources.Forms)
                    {
                        if ((query.Where?.Any(condition => condition?.Col == "tokens") == true ||
                             query.Group?.Contains("tokens") == true) &&
                            snapshot.Snapshot.Forms.All(form => form.Tokens is null))
                            return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryInvalid,
                                FailureReason.InvalidArgument, "Token frequency is not available in this evidence."));
                        result = FormQuery.Run(query, snapshot.Snapshot.Forms, snapshot.Snapshot.Graphemes);
                    }
                    else if (query.Change is not null)
                    {
                        if (query.From != QuerySources.Stems)
                            return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryInvalid,
                                FailureReason.InvalidArgument, "A hypothesised change applies to the stems source."));
                        var inputs = QueryHypothesisInputsReader.Read(query, project, database, formEntries);
                        result = FormQuery.RunHypothesis(query, snapshot.Snapshot.Stems ?? [], snapshot.Snapshot.Graphemes, inputs);
                        hypothesisNotes = result.Notes ?? [];
                    }
                    else
                    {
                        var rows = query.From switch
                        {
                            QuerySources.Stems => snapshot.Snapshot.Stems,
                            QuerySources.Uses => snapshot.Snapshot.Uses,
                            QuerySources.Objects => snapshot.Snapshot.Objects,
                            _ => throw new ArgumentException($"Unknown query source '{query.From}'."),
                        };
                        result = FormQuery.RunRows(query, rows ?? [], snapshot.Snapshot.Graphemes);
                    }
                    if (result.Evidence?.SameShape is { } shape)
                    {
                        var ledger = HypothesisCommands.NeighboursOf(new HypothesisRepository(database), $"change={shape.Change}",
                            shape.Change, "", "", "");
                        result = result with { Evidence = result.Evidence with { SameShape = shape with { Ledger = ledger } } };
                    }
                    var trial = new JobRepository(database).Get(snapshot.TrialId);
                    var stale = trial is null || trial.ProjectKey != ProjectWorkspaceKey.Compute(project) ||
                        trial.Kind != TrialJobHandler.TrialKind ||
                        new TrialReviewCommands(database, parserPath).EvidenceFreshness(trial, Trials.ProposalOf(trial))
                            .Relation != FreshnessRelation.Current ||
                        (File.Exists(project.FullFwDataPath) &&
                         File.GetLastWriteTimeUtc(project.FullFwDataPath).Ticks > snapshot.SourceWriteTicks);
                    return CommandOutcome<FormsQueryResponse>.Success(new(snapshot.TrialId, snapshot.GrammarHash,
                        snapshot.BundleId, stale, snapshot.Snapshot.UnresolvedVariants, result) { NotMeasured = [.. NotMeasuredNotes(query.From, coverage), .. hypothesisNotes] });
                }
                catch (QueryTooManyGroupsException error)
                {
                    return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryTooManyGroups,
                        FailureReason.InvalidArgument, error.Message));
                }
                catch (QueryMixedUnitsException error)
                {
                    return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryMixedUnits,
                        FailureReason.InvalidArgument, error.Message));
                }
                catch (QueryNotMeasuredException error)
                {
                    return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryNotMeasured,
                        FailureReason.InvalidArgument, error.Message));
                }
                catch (ArgumentException error)
                {
                    return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryInvalid, FailureReason.InvalidArgument,
                        error.Message));
                }
            }
            if (cache is not null)
                return cache.Use(project.FullFwDataPath, identity.Value.TrialId + "\0" + identity.Value.BundleId,
                    () => QuerySnapshotStore.Read(connection, transaction)!, Evaluate);
            return Evaluate(QuerySnapshotStore.Read(connection, transaction, [request.Query.From], UseTargetKinds(request.Query))!);
        });

    private static readonly string[] AllKinds = ["environment", "natural-class", "exception-feature", "category", "slot", "inflection-class"];

    // Values the is, in and not selectors of a column leave selectable.
    private static HashSet<string> Selected(QueryRecord query, string column)
    {
        var selected = new HashSet<string>(AllKinds, StringComparer.Ordinal);
        foreach (var condition in query.Where ?? [])
        {
            if (condition?.Col != column || condition.Value is null) continue;
            var values = condition.Value.Split(',', StringSplitOptions.TrimEntries);
            if (condition.Op == "is") selected.IntersectWith([condition.Value]);
            else if (condition.Op == "in") selected.IntersectWith(values);
            else if (condition.Op == "not") selected.Remove(condition.Value);
        }
        return selected;
    }

    private static Refusal? NotMeasuredRefusal(QueryRecord query, QueryCoverage coverage)
    {
        if (query.From == QuerySources.Uses && coverage.MissingUseKinds.Count > 0 &&
            (query.Where ?? []).Any(c => c?.Col == "target-kind" && c.Op is "is" or "in" or "not"))
        {
            var missing = Selected(query, "target-kind").Intersect(coverage.MissingUseKinds).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                return new Refusal(RefusalCodes.QueryNotMeasured, FailureReason.InvalidArgument,
                    $"These facts did not measure uses of target kind {string.Join(", ", missing)}; a result would read as none. " +
                    "Run a Trial on full facts, or select only environment or natural-class targets.");
        }
        if (query.From == QuerySources.Objects && coverage.MissingObjectClasses.Count > 0 &&
            ((query.Where ?? []).Any(c => c?.Col is "uses" or "applied") || (query.Group ?? []).Any(g => g is "uses" or "applied") ||
             query.Distinct is "uses" or "applied"))
        {
            var missing = Selected(query, "class").Intersect(coverage.MissingObjectClasses).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                return new Refusal(RefusalCodes.QueryNotMeasured, FailureReason.InvalidArgument,
                    $"These facts did not measure uses or applied of {string.Join(", ", missing)} objects; a result would read as zero. " +
                    "Select class environment or natural-class, or run a Trial on full facts.");
        }
        return null;
    }

    private static string[] NotMeasuredNotes(string source, QueryCoverage coverage) => source switch
    {
        QuerySources.Uses when coverage.MissingUseKinds.Count > 0 =>
            [$"Not measured: no uses rows for target kinds {string.Join(", ", coverage.MissingUseKinds)}."],
        QuerySources.Objects when coverage.MissingObjectClasses.Count > 0 =>
            [$"Not measured: uses and applied of {string.Join(", ", coverage.MissingObjectClasses)} objects (shown empty, not zero)."],
        QuerySources.Stems when coverage.Light => ["Not measured: a gloss that exists only as a definition reads none."],
        _ => [],
    };

    // A target-kind selector narrows the uses rows decoded; the query still applies every selector itself.
    private static string[]? UseTargetKinds(QueryRecord query) => query.From != QuerySources.Uses ? null :
        query.Where?.FirstOrDefault(c => c?.Col == "target-kind" && c.Op is "is" or "in" && c.Value is not null)
            ?.Value.Split(',', StringSplitOptions.TrimEntries);

    internal static IReadOnlyList<FoldRule> ProjectFold(SIL.Motif.Contract.Projects.ProjectLocator project)
    {
        try
        {
            return [.. new ProjectConfigurationReader().Read(project).ParsimonyFold
                .Select(text => FoldRule.TryParse(text, out var rule) ? rule : null).OfType<FoldRule>()];
        }
        catch (ProjectConfigurationException)
        {
            return [];
        }
    }
}

/// <summary>Keeps at most one decoded Trial while serializing readers that use it.</summary>
public sealed class FormsQuerySnapshotCache
{
    private readonly object _gate = new();
    private string? _project;
    private string? _trial;
    private QuerySnapshotStore.Stored? _snapshot;

    internal void Clear()
    {
        lock (_gate) { _snapshot = null; _project = null; _trial = null; }
    }

    internal T Use<T>(string project, string trial, Func<QuerySnapshotStore.Stored> load,
        Func<QuerySnapshotStore.Stored, T> use)
    {
        lock (_gate)
        {
            if (_snapshot is null || _project != project || _trial != trial)
            {
                _snapshot = null;
                _project = null;
                _trial = null;
                _snapshot = load();
                _project = project;
                _trial = trial;
            }
            return use(_snapshot);
        }
    }
}
