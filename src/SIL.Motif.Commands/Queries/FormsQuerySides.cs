using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Commands.Queries;

/// <summary>Runs a forms query over the Baseline, a Proposal's Trial copy, or both, and reads no saved Trial evidence.</summary>
internal static class FormsQuerySides
{
    private sealed record Source(string Side, QuerySideState State, string? Path, Refusal? Refusal);

    private sealed record Condition(string? Side, QueryCondition Inner);

    public static CommandOutcome<FormsQueryResponse> RunAsOf(FormsQueryRequest request, string? parserPath)
    {
        if (Invalid(request.Query) is { } invalid) return Refuse(RefusalCodes.QueryInvalid, FailureReason.InvalidArgument, invalid);
        return ProjectStoreCommand.ReadExisting(request.FwDataPath,
            Refuse(RefusalCodes.QueryAsOfUnavailable, FailureReason.Refused,
                "The project has no Motif store, so there is no Baseline or Trial to read as of."),
            (database, project) => Run(request, database, project, parserPath));
    }

    private static CommandOutcome<FormsQueryResponse> Run(FormsQueryRequest request, MotifDatabase database, ProjectLocator project,
        string? parserPath)
    {
        var query = request.Query;
        var asOf = query.AsOf!.Trim();
        var both = query.Sides == QuerySideNames.Both;
        if (asOf != QuerySideNames.Baseline && !CanonicalId.TryParse(asOf, out _))
            return Refuse(RefusalCodes.QueryAsOfUnavailable, FailureReason.Refused,
                $"'{asOf}' is neither 'baseline' nor a Proposal id. Revision digests cannot be resolved yet.");
        if (both && asOf == QuerySideNames.Baseline)
            return Refuse(RefusalCodes.QueryInvalid, FailureReason.InvalidArgument,
                "--sides both compares the Baseline with a Proposal; give the Proposal id to --as-of.");
        var sides = both ? new[] { QuerySideNames.Baseline, QuerySideNames.Proposal }
            : [asOf == QuerySideNames.Baseline ? QuerySideNames.Baseline : QuerySideNames.Proposal];
        var conditions = new List<Condition>();
        foreach (var condition in query.Where ?? [])
        {
            var split = Split(condition);
            if (split.Side is not null && !sides.Contains(split.Side))
                return Refuse(RefusalCodes.QueryInvalid, FailureReason.InvalidArgument,
                    $"The selector '{condition.Col}' names the {split.Side} side, which this query does not read.");
            conditions.Add(split);
        }
        var sort = ParseSort(query.Sort, both, sides, out var sortError);
        if (sortError is not null) return Refuse(RefusalCodes.QueryInvalid, FailureReason.InvalidArgument, sortError);
        var sources = new List<Source>();
        foreach (var side in sides)
        {
            var source = Locate(database, project, side, asOf, parserPath);
            if (source.Refusal is not null) return CommandOutcome<FormsQueryResponse>.Refused(source.Refusal);
            sources.Add(source);
        }
        var folded = query.Fold?.Split(',', StringSplitOptions.TrimEntries).Contains(QueryFold.ProjectToken) == true
            ? query with { Fold = QueryFold.ResolveProject(query.Fold, FormsQueryCommand.ProjectFold(project)) } : query;
        var data = new Dictionary<string, SideCacheReader.Side>(StringComparer.Ordinal);
        try
        {
            foreach (var source in sources.Where(source => source.Path is not null))
            {
                using var reader = BaselineReadCache.Open(source.Path!);
                data[source.Side] = SideCacheReader.Read(reader.Cache);
            }
            return Evaluate(folded, both, sources, data, conditions, sort);
        }
        catch (QueryRefusalException error)
        {
            return CommandOutcome<FormsQueryResponse>.Refused(error.Refusal);
        }
        catch (QueryTooManyGroupsException error)
        {
            return Refuse(RefusalCodes.QueryTooManyGroups, FailureReason.InvalidArgument, error.Message);
        }
        catch (QueryMixedUnitsException error)
        {
            return Refuse(RefusalCodes.QueryMixedUnits, FailureReason.InvalidArgument, error.Message);
        }
        catch (ArgumentException error)
        {
            return Refuse(RefusalCodes.QueryInvalid, FailureReason.InvalidArgument, error.Message);
        }
    }

    private static string? Invalid(QueryRecord query)
    {
        if (query.AsOf is null || string.IsNullOrWhiteSpace(query.AsOf))
            return "--sides and --sort need --as-of baseline or --as-of <proposalId>.";
        if (query.Sides is not null && query.Sides != QuerySideNames.Both)
            return "--sides takes 'both'; name one side with --as-of.";
        if (query.Filter is not null)
            return "--as-of takes a conjunction of is, not, in, has, <, > and ~ selectors; or, not and the other v2 ops read the latest Trial.";
        if (query.From != QuerySources.Forms)
            return "--as-of reads the forms source; stems, uses and objects read the latest Trial's evidence.";
        if (query.Change is not null || query.Score is { Count: > 0 } || query.Evidence || query.Members is { Count: > 0 } ||
            query.Context is { Count: > 0 } || query.Exceptions is { Count: > 0 } || query.Unit is not null || query.TrialId is not null)
            return "--as-of reads rows; it does not combine with --change, --score, evidence, --members, --context, --exception, --unit or --trial.";
        if (query.Sides == QuerySideNames.Both && (query.Group.Count > 0 || query.Distinct is not null || query.Ids))
            return "--sides both lists rows; grouping, --distinct and --ids belong to a single side.";
        return null;
    }

    private static Source Locate(MotifDatabase database, ProjectLocator project, string side, string asOf, string? parserPath)
    {
        var key = ProjectWorkspaceKey.Compute(project);
        if (side == QuerySideNames.Baseline)
        {
            var baseline = new BaselineRepository(database).GetCurrentPublished(key);
            return baseline is null
                ? Failed(side, QuerySideNames.ReasonNoBaseline, "The project has no current Baseline. Run 'motif baseline capture --project <fwdata>' first.")
                : new Source(side, new QuerySideState(QuerySideNames.Baseline, QuerySideNames.Ready, QuerySideNames.Baseline, null, null),
                    baseline.FwDataPath, null);
        }
        var state = (string? trial, string status, string? reason) =>
            new QuerySideState(QuerySideNames.Proposal, status, asOf, trial, reason);
        var latest = Trials.LatestFor(database, project, asOf, parserPath);
        if (latest is null)
            return new Source(side, state(null, QuerySideNames.Pending, QuerySideNames.ReasonNoTrial), null, null);
        if (latest.Status is not (JobStatus.Completed or JobStatus.CompletedWithAssessmentFailure))
            return new Source(side, state(latest.JobId, QuerySideNames.Pending,
                QuerySideNames.ReasonTrialNotCompleted), null, null);
        var scratch = TrialScratchPath.ProjectFile(TrialScratchPath.ForJob(latest.JobId));
        return scratch is null
            ? Failed(side, QuerySideNames.ReasonTrialCleared, "The Trial's details were cleared. Run the Trial again to bring them back.")
            : new Source(side, state(latest.JobId, QuerySideNames.Ready, null), scratch, null);
    }

    private static Source Failed(string side, string reason, string message) =>
        new(side, new QuerySideState(side, QuerySideNames.Pending, side, null, reason),
            null, new Refusal(RefusalCodes.QueryAsOfUnavailable, FailureReason.Refused, message));

    private static Condition Split(QueryCondition condition)
    {
        foreach (var side in new[] { QuerySideNames.Baseline, QuerySideNames.Proposal })
            if (condition.Col.StartsWith(side + ".", StringComparison.Ordinal))
                return new Condition(side, condition with { Col = condition.Col[(side.Length + 1)..] });
        return new Condition(null, condition);
    }

    private static bool IsSideColumn(string column) => column is "parse-state" or "parse-tokens";

    private static CommandOutcome<FormsQueryResponse> Evaluate(QueryRecord query, bool both, List<Source> sources,
        Dictionary<string, SideCacheReader.Side> data, List<Condition> conditions, SortSpec sort)
    {
        var states = sources.Select(source => source.State).ToList();
        var mode = both ? QuerySideNames.Both : sources[0].Side == QuerySideNames.Baseline ? QuerySideNames.Baseline : QuerySideNames.Proposal;
        var proposal = sources.FirstOrDefault(source => source.Side != QuerySideNames.Baseline);
        var trialId = proposal?.State.TrialId ?? "";
        var plain = query with { AsOf = null, Sides = null, Sort = null, Where = [] };
        QueryResult? singleResult = null;
        var matched = new Dictionary<string, HashSet<string>?>(StringComparer.Ordinal);
        var unprefixed = conditions.Where(condition => condition.Side is null).Select(condition => condition.Inner).ToList();
        foreach (var source in sources)
        {
            if (!data.TryGetValue(source.Side, out var side)) continue;
            var own = conditions.Where(condition => condition.Side == source.Side).Select(condition => condition.Inner).ToList();
            if (!both)
            {
                singleResult = Select(plain, side, [.. unprefixed, .. own]);
                matched[source.Side] = [.. singleResult.Rows.Select(row => row.Id)];
                if (query.Group.Count == 0) singleResult = singleResult with { Groups = [], Ids = [] };
                continue;
            }
            matched[source.Side] = own.Count == 0 ? null : [.. Select(plain, side, own).Rows.Select(row => row.Id)];
            matched["any:" + source.Side] = unprefixed.Count == 0 ? null
                : [.. Select(plain, side, unprefixed).Rows.Select(row => row.Id)];
        }
        var rows = new Dictionary<string, (QuerySideRow? Baseline, QuerySideRow? Proposal)>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (!data.TryGetValue(source.Side, out var side)) continue;
            foreach (var row in side.Rows)
            {
                var id = row.Form.Id;
                if (!Keep(id, both, sources, data, matched, unprefixed.Count > 0)) continue;
                var current = rows.GetValueOrDefault(id);
                rows[id] = source.Side == QuerySideNames.Baseline ? (row, current.Proposal) : (current.Baseline, row);
            }
        }
        var paired = (query.Group.Count > 0 && !both) ? []
            : sort.Apply([.. rows.Select(item => new QueryPairedRow(item.Key, item.Value.Baseline, item.Value.Proposal))]);
        var result = singleResult ?? new QueryResult([], [], []);
        var response = new FormsQueryResponse(trialId, "", "", false, 0, result)
            { Sides = new QuerySidesResult(QuerySidesResult.CurrentVersion, mode, states, paired) };
        return CommandOutcome<FormsQueryResponse>.Success(response);
    }

    private static bool Keep(string id, bool both, List<Source> sources, Dictionary<string, SideCacheReader.Side> data,
        Dictionary<string, HashSet<string>?> matched, bool anyConditions)
    {
        if (!both) return matched.TryGetValue(sources[0].Side, out var only) && only!.Contains(id);
        foreach (var source in sources)
            if (data.ContainsKey(source.Side) && matched.GetValueOrDefault(source.Side) is { } own && !own.Contains(id)) return false;
        if (!anyConditions) return true;
        return sources.Any(source => data.ContainsKey(source.Side) && matched["any:" + source.Side]!.Contains(id));
    }

    private static QueryResult Select(QueryRecord template, SideCacheReader.Side side, IReadOnlyList<QueryCondition> conditions)
    {
        var parseConditions = conditions.Where(condition => IsSideColumn(condition.Col)).ToList();
        var formConditions = conditions.Where(condition => !IsSideColumn(condition.Col)).ToList();
        var rows = side.Rows.Where(row => parseConditions.All(condition => ParseMatches(row.Parse, condition)))
            .Select(row => row.Form).ToList();
        return FormQuery.Run(template with { Where = formConditions }, rows, side.Graphemes);
    }

    private static bool ParseMatches(QueryWordParse parse, QueryCondition condition)
    {
        if (condition.Col == "parse-tokens")
        {
            if (!long.TryParse(condition.Value, out var number)) throw new ArgumentException("parse-tokens compares with a whole number.");
            return condition.Op switch
            {
                "is" => parse.Tokens == number, "not" => parse.Tokens != number, "<" => parse.Tokens < number,
                ">" => parse.Tokens > number,
                _ => throw new ArgumentException("parse-tokens takes =, not, < or >."),
            };
        }
        var values = condition.Value.Split(',', StringSplitOptions.TrimEntries);
        return condition.Op switch
        {
            "is" => parse.State == condition.Value,
            "in" => values.Contains(parse.State),
            "not" => parse.State != condition.Value,
            _ => throw new ArgumentException("parse-state takes =, in or not."),
        };
    }

    private sealed record SortSpec(string? Side, string Column, bool Descending)
    {
        public IReadOnlyList<QueryPairedRow> Apply(List<QueryPairedRow> rows)
        {
            string Key(QueryPairedRow row) => Value(Pick(row), Column);
            QuerySideRow? Pick(QueryPairedRow row) => Side switch
            {
                QuerySideNames.Baseline => row.Baseline,
                QuerySideNames.Proposal => row.Proposal,
                _ => row.Baseline ?? row.Proposal,
            };
            var numeric = Column is "tokens" or "parse-tokens";
            var present = rows.Where(row => Pick(row) is not null).ToList();
            var absent = rows.Where(row => Pick(row) is null).OrderBy(row => row.Id, StringComparer.Ordinal);
            IOrderedEnumerable<QueryPairedRow> ordered = numeric
                ? (Descending ? present.OrderByDescending(row => long.Parse(Key(row))) : present.OrderBy(row => long.Parse(Key(row))))
                : (Descending ? present.OrderByDescending(Key, StringComparer.Ordinal) : present.OrderBy(Key, StringComparer.Ordinal));
            return [.. ordered.ThenBy(row => row.Id, StringComparer.Ordinal), .. absent];
        }
    }

    private static readonly string[] SortColumns =
        ["form", "base", "kind", "entry", "id", "morph-type", "variant-type", "environments", "category", "tokens", "parse-state", "parse-tokens"];

    private static string Value(QuerySideRow? row, string column) => row is null ? "" : column switch
    {
        "form" => row.Form.Form, "base" => row.Form.Base, "kind" => row.Form.Kind, "entry" => row.Form.Entry, "id" => row.Form.Id,
        "morph-type" => row.Form.MorphType, "variant-type" => row.Form.VariantType,
        "environments" => string.Join(",", row.Form.Environments), "category" => string.Join(",", row.Form.Category),
        "tokens" => (row.Form.Tokens ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        "parse-state" => row.Parse.State, "parse-tokens" => row.Parse.Tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "",
    };

    private static SortSpec ParseSort(string? text, bool both, string[] sides, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return new SortSpec(both ? QuerySideNames.Baseline : null, "form", false);
        var descending = false;
        var body = text.Trim();
        if (body.EndsWith(":desc", StringComparison.Ordinal)) { descending = true; body = body[..^5]; }
        else if (body.EndsWith(":asc", StringComparison.Ordinal)) body = body[..^4];
        var split = Split(new QueryCondition(body, "is", ""));
        if (!SortColumns.Contains(split.Inner.Col))
        {
            error = $"--sort names one of {string.Join(", ", SortColumns)}, optionally prefixed baseline. or proposal. and suffixed :desc.";
            return new SortSpec(null, "form", false);
        }
        if (both && split.Side is null)
            error = "With --sides both, name the side to sort on, for example --sort proposal.form.";
        else if (split.Side is not null && !sides.Contains(split.Side))
            error = $"The {split.Side} side is not read by this query.";
        return new SortSpec(split.Side, split.Inner.Col, descending);
    }

    private static CommandOutcome<FormsQueryResponse> Refuse(string code, FailureReason reason, string message) =>
        CommandOutcome<FormsQueryResponse>.Refused(new Refusal(code, reason, message));
}
