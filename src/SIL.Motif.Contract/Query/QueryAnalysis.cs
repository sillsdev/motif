using SIL.Motif.Contract.Parsimony.RuleAttempts;

namespace SIL.Motif.Contract.Query;

/// <summary>What a source tells the split scorers about its rows.</summary>
/// <param name="Source"><c>stems</c> or <c>forms</c>.</param>
/// <param name="Columns">The context columns of the source.</param>
/// <param name="Expressible">Whether FieldWorks can state a column value as a rule condition.</param>
/// <param name="Compile">Compiles a selector over the rows of the source.</param>
/// <param name="DefaultDeviant">The deviants when the query names none; null when the source has no default.</param>
/// <param name="DefaultDeviantWhy">Why the default deviants are not measured; null when they are.</param>
/// <param name="HeldOut">The held-out reading of a row, or null when held-out is not checked.</param>
/// <param name="StemKey">Identifies the stem of a row for the held-out tally.</param>
/// <param name="HeldOutWhy">Why held-out is not checked.</param>
/// <param name="Frequency">The token frequency of a row, or null when the evidence carries none.</param>
/// <param name="Label">The form shown for a row.</param>
/// <param name="Question">The speaker question for a row the evidence cannot decide; null result when it is decided; null function when questions are not measured.</param>
/// <param name="QuestionWhy">Why questions are not measured.</param>
/// <param name="SameShape">The same-shape group of the members; null function when the source has no shape.</param>
/// <param name="PhaseThree">Runs one of tolerance, reliability and control over the members; null for a source without a hypothesised change.</param>
internal sealed record AnalysisSetup(string Source, IReadOnlyList<string> Columns, Func<string, string, string> Expressible,
    Func<QueryCondition, Func<IQueryRowView, bool>> Compile, Func<IQueryRowView, bool>? DefaultDeviant,
    string? DefaultDeviantWhy, Func<IQueryRowView, int>? HeldOut, Func<IQueryRowView, string>? StemKey, string? HeldOutWhy,
    Func<IQueryRowView, long?>? Frequency, Func<IQueryRowView, string> Label, Func<IQueryRowView, string?>? Question,
    string? QuestionWhy, Func<IReadOnlyList<IQueryRowView>, QuerySameShape?>? SameShape,
    Func<string, QueryScore>? PhaseThree);

/// <summary>Runs the split, size and evidence parts of a query over rows a source has already selected.</summary>
internal static class QueryAnalysis
{
    public static readonly IReadOnlyList<string> StemColumns =
        ["initial:1", "initial:2", "final:1", "final:2", "final:3", "shape", "syllables", "category", "features", "msa-count", "alternates"];

    public static readonly IReadOnlyList<string> FormColumns =
        ["kind", "morph-type", "variant-type", "environments", "category", "initial:1", "initial:2", "final:1", "final:2", "final:3",
            "shape", "syllables", "change.at", "change.left", "change.right", "change.abstract"];

    public static bool IsAnalysisScorer(string name) => name is "split" or "size";

    public static bool Wants(QueryRecord query) => query.Evidence || query.Score?.Any(IsAnalysisScorer) == true;

    /// <summary>Checks the parts of the record that only the split scorers and the evidence screen read.</summary>
    public static void Validate(QueryRecord query)
    {
        var wants = Wants(query);
        if ((query.Members is { Count: > 0 } || query.Context is { Count: > 0 }) && !wants)
            throw new ArgumentException("Members and a context belong to --score split or size, or to evidence.");
        if (query.Context is { Count: > 0 } && query.Score?.Contains("size") != true)
            throw new ArgumentException("A context is what the size scorer weighs; add --score size.");
        if (!wants) return;
        if (query.Group.Count > 0 || query.Ids)
            throw new ArgumentException("Split, size and evidence run over the whole selection; remove --group and --ids.");
        if (!query.Evidence) return;
        if (query.Score is { Count: > 0 }) throw new ArgumentException("Evidence runs every scorer itself; remove --score.");
        if (query.Context is { Count: > 0 }) throw new ArgumentException("Evidence chooses its own contexts; remove the context.");
        if (query.Where.Count > 0 == query.Members is { Count: > 0 })
            throw new ArgumentException("Evidence needs one grouping: --where selectors or --members, not both and not neither.");
    }

    /// <summary>The rows the grouping selects: the members of an evidence query, the universe of a split.</summary>
    public static QueryOutcome Select(QueryRecord query, IReadOnlyList<IQueryRowView> views, QuerySpec spec, List<IQueryRowView> matched)
    {
        if (query.Evidence && query.Members is { Count: > 0 } members)
        {
            var set = new HashSet<string>(members, StringComparer.Ordinal);
            matched.AddRange(views.Where(v => set.Contains(v.Id)));
            return new QueryOutcome([], [], []);
        }
        return QueryEngine.Execute(query, views, spec, matched.Add);
    }

    public static Func<string, string, string> ExpressibleFor(IReadOnlyList<string> graphemes, bool raw)
    {
        var known = new HashSet<string>(raw ? [] : graphemes.Where(g => !string.IsNullOrEmpty(g)), StringComparer.Ordinal);
        var index = FormQuery.Index(raw ? [] : graphemes);
        return (column, value) =>
        {
            if (column is "category" or "features" or "environments") return value == "none" ? "not-checked" : "yes";
            if (QuerySources.IsEdge(column, out _, out _) || column is "change.left" or "change.right")
                return value.Length > 0 && known.Count > 0 && TextColumns.Tokenize(value, index).All(known.Contains) ? "yes" : "not-checked";
            return column is "collides" or "msa-count" or "alternates" ? "no" : "not-checked";
        };
    }

    private static Func<IQueryRowView, bool> Deviants(QueryRecord query, AnalysisSetup setup, out bool fromEvidence)
    {
        fromEvidence = false;
        if (query.Members is { Count: > 0 } members)
        {
            var set = new HashSet<string>(members, StringComparer.Ordinal);
            return row => set.Contains(row.Id);
        }
        if (query.Exceptions is { Count: > 0 } exceptions)
        {
            var tests = exceptions.Select(setup.Compile).ToArray();
            return row => tests.All(t => t(row));
        }
        if (setup.DefaultDeviantWhy is { } why) throw new QueryNotMeasuredException(why);
        if (setup.DefaultDeviant is null)
            throw new ArgumentException("Split and size need the deviants: --exception selectors, --members, or --change with --wordset.");
        fromEvidence = true;
        return setup.DefaultDeviant;
    }

    private static SplitInput InputFor(AnalysisSetup setup, IReadOnlyList<IQueryRowView> rows, Func<IQueryRowView, bool> deviant) =>
        new(rows, deviant, setup.Columns, setup.Expressible, setup.HeldOut, setup.StemKey, setup.HeldOutWhy, setup.Frequency, setup.Label);

    private static QueryScore SplitScore(SplitOutcome outcome, string unit, IReadOnlyList<string> extraNotes)
    {
        var notes = new List<string>(outcome.Notes) { "Counts are rows (one per " + unit + "); a context is judged against the universe the query selected." };
        notes.AddRange(extraNotes);
        var limit = outcome.E >= 2 ? outcome.E / Math.Log(outcome.E) : (double?)null;
        var threshold = outcome.N >= 2 ? outcome.N / Math.Log(outcome.N) : (double?)null;
        if (threshold is { } t && outcome.E >= t)
            notes.Add("The rule itself does not survive tolerance here (e is not below N/ln N); contexts are reported for information.");
        return new QueryScore("split", SplitAnalysis.Version, "row",
            new Dictionary<string, long>
                { ["universe"] = outcome.N, ["deviants"] = outcome.E, ["judged"] = outcome.Judged.Count, ["too_few"] = outcome.TooFew },
            new Dictionary<string, double?> { ["coverage_limit"] = limit, ["tolerance_threshold"] = threshold }, outcome.Verdict, notes)
        { Contexts = outcome.Contexts };
    }

    /// <summary>Runs the split and size scorers a split query names.</summary>
    public static List<QueryScore> Scores(QueryRecord query, IReadOnlyList<IQueryRowView> universe, AnalysisSetup setup)
    {
        var scores = new List<QueryScore>();
        var deviant = Deviants(query, setup, out var fromEvidence);
        // A column the deviants were chosen by would explain them by definition.
        if (query.Exceptions is { Count: > 0 } chosen)
            setup = setup with { Columns = [.. setup.Columns.Where(c => chosen.All(e => e.Col != c))] };
        var unit = setup.Source == QuerySources.Stems ? "MSA" : "form";
        foreach (var name in query.Score!.Where(IsAnalysisScorer))
        {
            if (name == "split")
            {
                var outcome = new SplitAnalysis(InputFor(setup, universe, deviant)).Run(10, fromEvidence);
                scores.Add(SplitScore(outcome, unit, []));
                continue;
            }
            long n = universe.Count, e = universe.Count(deviant);
            if (query.Context is { Count: > 0 } selectors)
            {
                var tests = selectors.Select(setup.Compile).ToArray();
                long scope = 0, deviants = 0;
                foreach (var row in universe.Where(r => tests.All(t => t(r)))) { scope++; if (deviant(row)) deviants++; }
                scores.Add(SplitAnalysis.SizeScore(n, e, null, scope, deviants, "before: each deviant listed or tagged one by one."));
            }
            else scores.Add(SplitAnalysis.SizeScore(n, e, null, 0, 0, "before: each deviant listed or tagged one by one."));
        }
        return scores;
    }

    /// <summary>The evidence screen: members against the whole source.</summary>
    public static QueryEvidence Evidence(QueryRecord query, IReadOnlyList<IQueryRowView> universe, IReadOnlyList<IQueryRowView> members,
        AnalysisSetup setup)
    {
        if (members.Count == 0) throw new ArgumentException("No row matches the grouping, so there is nothing to explain.");
        var memberSet = new HashSet<IQueryRowView>(members);
        var analysis = new SplitAnalysis(InputFor(setup, universe, memberSet.Contains));
        var outcome = analysis.Run(int.MaxValue, deviantsAreEvidence: false);
        var notMeasured = new List<string>();
        var shared = outcome.Judged.Where(c => c.Deviants * 2 >= outcome.E).Take(5)
            .Select(c => c with { CounterExamples = analysis.CounterExamples(c, 3) }).ToList();
        var unit = setup.Source == QuerySources.Stems ? "MSA" : "form";
        var scores = new List<QueryScore> { SplitScore(outcome, unit, []) with { Contexts = null } };
        if (shared.Count == 0 && outcome.E >= 2) notMeasured.Add("No single context is shared by half of the members.");
        if (setup.PhaseThree is { } phaseThree)
            foreach (var name in FormQuery.PhaseThreeScorers)
            {
                try { scores.Add(phaseThree(name)); }
                catch (Exception error) when (error is QueryNotMeasuredException or QueryMixedUnitsException)
                { notMeasured.Add($"{name}: {error.Message}"); }
            }
        else notMeasured.Add("tolerance, reliability and control: not measured without --change on the stems source.");
        var best = outcome.Judged.FirstOrDefault(c => c.Depth == 1 && c.Status is "A" or "B" or "not-checked" or "partial");
        scores.Add(best is null
            ? SplitAnalysis.SizeScore(outcome.N, outcome.E, null, 0, 0, "before: each member listed or tagged one by one.")
            : SplitAnalysis.SizeScore(outcome.N, outcome.E, best, best.Scope, best.Deviants, "before: each member listed or tagged one by one."));
        var count = Math.Max(0, query.Examples);
        long? Frequency(IQueryRowView row) => setup.Frequency?.Invoke(row);
        if (setup.Frequency is null) notMeasured.Add("Token frequency is not measured here, so examples and questions are in alphabetical order.");
        var examples = members.OrderByDescending(m => Frequency(m) ?? 0).ThenBy(m => m.SortKey, StringComparer.Ordinal)
            .ThenBy(m => m.Id, StringComparer.Ordinal).Take(count).Select(m => new QueryExample(m.Id, setup.Label(m), Frequency(m))).ToList();
        var questions = new List<QueryQuestion>();
        if (setup.Question is { } ask)
            questions = [.. members.Select(m => (Row: m, Text: ask(m))).Where(x => x.Text is not null)
                .OrderByDescending(x => Frequency(x.Row) ?? 0).ThenBy(x => x.Row.SortKey, StringComparer.Ordinal)
                .ThenBy(x => x.Row.Id, StringComparer.Ordinal).Take(count).Select(x => new QueryQuestion(x.Row.Id, x.Text!))];
        else notMeasured.Add("Speaker questions: " + (setup.QuestionWhy ?? "not measured for this source."));
        QuerySameShape? shape = null;
        if (setup.SameShape is { } sameShape) shape = sameShape(members);
        else notMeasured.Add("Same-shape count: not measured for this source.");
        return new QueryEvidence(setup.Source, members.Count, universe.Count, shared, outcome.TooFew, scores, examples, questions,
            shape, notMeasured);
    }

    /// <summary>The largest group of members with one variant shape, keyed like a package's same-shape groups.</summary>
    public static QuerySameShape? FormShape(IReadOnlyList<IQueryRowView> members)
    {
        var groups = members.GroupBy(m => (Type: Join(m.Get("variant-type")), Env: Join(m.Get("environments").Order(StringComparer.Ordinal)),
                Change: Join(m.Get("change.abstract"))))
            .Select(g => (g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ThenBy(g => g.Key.Change, StringComparer.Ordinal).ToList();
        if (groups.Count == 0) return null;
        var top = groups[0];
        return new QuerySameShape($"variant|{top.Key.Type}|env:{top.Key.Env}|{top.Key.Change}", top.Count,
            top.Count >= SameShapeGroups.RuleAttemptThreshold, top.Key.Change);
    }

    private static string Join(IEnumerable<string> values) => string.Join(',', values);
}
