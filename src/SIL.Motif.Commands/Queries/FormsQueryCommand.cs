using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>One request for read-only Trial facts.</summary>
public sealed record FormsQueryRequest(string FwDataPath, string ProductVersion, QueryRecord Query);

/// <summary>A query result and the identity and freshness of its source.</summary>
public sealed record FormsQueryResponse(string TrialId, string GrammarHash, string BundleId, bool IsStale,
    int UnresolvedVariants, QueryResult Result);

/// <summary>Runs a query over the forms, stems, uses or objects source of the last completed Trial's materialized facts.</summary>
public static class FormsQueryCommand
{
    /// <summary>Reads the current Trial and evaluates a closed query record.</summary>
    public static CommandOutcome<FormsQueryResponse> Run(FormsQueryRequest request, FormsQuerySnapshotCache? cache = null) =>
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
            CommandOutcome<FormsQueryResponse> Evaluate(QuerySnapshotStore.Stored snapshot)
            {
                try
                {
                    var query = request.Query.Fold?.Split(',', StringSplitOptions.TrimEntries).Contains(QueryFold.ProjectToken) == true
                        ? request.Query with { Fold = QueryFold.ResolveProject(request.Query.Fold, ProjectFold(project)) }
                        : request.Query;
                    QueryResult result;
                    if (query.From == QuerySources.Forms)
                    {
                        if ((query.Where?.Any(condition => condition?.Col == "tokens") == true ||
                             query.Group?.Contains("tokens") == true) &&
                            snapshot.Snapshot.Forms.All(form => form.Tokens is null))
                            return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryInvalid,
                                FailureReason.InvalidArgument, "Token frequency is not available in this evidence."));
                        result = FormQuery.Run(query, snapshot.Snapshot.Forms, snapshot.Snapshot.Graphemes);
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
                    using var latest = connection.CreateCommand();
                    latest.Transaction = transaction;
                    latest.CommandText = "SELECT JobId FROM Jobs WHERE Kind='trial' AND Status='completed' " +
                        "ORDER BY CreatedUtc DESC, QueueOrder DESC, JobId DESC LIMIT 1";
                    var latestTrial = latest.ExecuteScalar() as string;
                    var stale = (latestTrial is not null && latestTrial != snapshot.TrialId) ||
                        (File.Exists(project.FullFwDataPath) &&
                         File.GetLastWriteTimeUtc(project.FullFwDataPath).Ticks > snapshot.SourceWriteTicks);
                    return CommandOutcome<FormsQueryResponse>.Success(new(snapshot.TrialId, snapshot.GrammarHash,
                        snapshot.BundleId, stale, snapshot.Snapshot.UnresolvedVariants, result));
                }
                catch (QueryTooManyGroupsException error)
                {
                    return CommandOutcome<FormsQueryResponse>.Refused(new Refusal(RefusalCodes.QueryTooManyGroups,
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

    // A target-kind selector narrows the uses rows decoded; the query still applies every selector itself.
    private static string[]? UseTargetKinds(QueryRecord query) => query.From != QuerySources.Uses ? null :
        query.Where?.FirstOrDefault(c => c?.Col == "target-kind" && c.Op is "is" or "in" && c.Value is not null)
            ?.Value.Split(',', StringSplitOptions.TrimEntries);

    private static IReadOnlyList<FoldRule> ProjectFold(SIL.Motif.Contract.Projects.ProjectLocator project)
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
