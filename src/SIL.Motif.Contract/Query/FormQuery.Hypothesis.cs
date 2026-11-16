using System.Globalization;

namespace SIL.Motif.Contract.Query;

public static partial class FormQuery
{
    /// <summary>The scorer names a hypothesised-change query accepts.</summary>
    public static IReadOnlyList<string> Scorers { get; } = ["tolerance", "reliability", "control"];

    /// <summary>The formula version printed with <c>reliability</c>; its exact convention is unresolved, so it is a sort key.</summary>
    public const string ReliabilityVersion = "reliability/1 p=(h+0.5)/(n+1) lower=p-0.674*sqrt(p*(1-p)/n); sort key, not a verdict";

    /// <summary>The formula version printed with <c>tolerance</c>.</summary>
    public const string ToleranceVersion = "tolerance/1 productive iff e < N/ln N (strict)";

    /// <summary>The thresholds of <c>control</c>, provisional as in the rule-hypothesis-test skill.</summary>
    public const string ControlVersion = "control/1 min-hits=20 ratio=2 provisional; counts only";

    private const int ControlMinimumHits = 20;
    private const double ControlRatio = 2.0;

    private static void RejectHypothesisFields(QueryRecord query, string where)
    {
        if (query.Change is not null || query.Score is { Count: > 0 } || query.Exceptions is { Count: > 0 } || query.Unit is not null)
            throw new ArgumentException($"A hypothesised change, scorers, exceptions and a unit apply to the stems source only, not {where}.");
    }

    /// <summary>
    /// Evaluates a hypothesised change over the stems source: adds <c>predicted</c>, <c>evidence</c> and <c>collides</c>, then
    /// runs the requested scorers over every selected row. A row whose form does not end (or start) with the dropped letters
    /// is outside the hypothesis and is never selected.
    /// </summary>
    public static QueryResult RunHypothesis(QueryRecord query, IReadOnlyList<QueryRow> rows, IReadOnlyList<string> graphemes,
        QueryHypothesisInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(graphemes);
        ArgumentNullException.ThrowIfNull(inputs);
        if (query.From != QuerySources.Stems || query.Change is not { } change)
            throw new ArgumentException("A hypothesised change applies to the stems source.");
        var scores = query.Score ?? [];
        if (scores.Any(s => !Scorers.Contains(s)) || scores.Distinct(StringComparer.Ordinal).Count() != scores.Count)
            throw new ArgumentException("Unknown or repeated scorer; use tolerance, reliability or control.");
        if (scores.Count > 0 && query.Group.Count > 0)
            throw new ArgumentException("Scorers run over the whole selected universe; remove --group.");
        if (query.Exceptions is { Count: > 0 } && !scores.Contains("tolerance"))
            throw new ArgumentException("Exceptions are what tolerance counts; add --score tolerance.");
        if (query.Unit is not (null or "msa" or "stem")) throw new ArgumentException("The unit is msa or stem.");
        var fold = QueryFold.Compile(query.Fold);
        var context = new HypothesisContext(change, fold, inputs);
        if (!context.EvidenceMeasured && query.Where.Any(c => c.Col == "evidence"))
            throw new QueryNotMeasuredException("The evidence column is not measured without an evidence word set (--wordset), so a selector on it cannot run.");
        var segmentIndex = Index(query.RawLetters ? [] : graphemes);
        var stored = QuerySources.StoredColumns(QuerySources.Stems);
        var spec = new QuerySpec(c => HypothesisContext.IsHypothesisColumn(c) || QuerySources.IsColumn(QuerySources.Stems, c),
            c => HypothesisContext.IsHypothesisSet(c) || QuerySources.IsStoredSet(QuerySources.Stems, c),
            c => QuerySources.IsNumeric(QuerySources.Stems, c));
        var views = rows.Select(row => new HypothesisRow(new StoredValues(QuerySources.Stems, stored, row, segmentIndex, fold), context))
            .Where(view => view.Applicable);
        var matched = new List<HypothesisRow>();
        var outcome = QueryEngine.Execute(query, views, spec, view => matched.Add((HypothesisRow)view));
        var results = scores.Count == 0 ? null : scores.Select(name => context.Score(name, query, matched, spec)).ToList();
        QueryItem Item(IQueryRowView view) => ((HypothesisRow)view).Project();
        var items = scores.Count == 0 ? outcome.Selected
            : [.. matched.OrderBy(v => v.SortKey, StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal)
                .Take(query.Examples).Cast<IQueryRowView>()];
        return new QueryResult([], [.. outcome.Groups.Select(g => new QueryGroup(g.Key, g.Count, g.Tokens, [])
            { Samples = [.. g.Examples.Select(Item)] })], outcome.Ids)
        {
            Items = [.. items.Select(Item)],
            Scores = results,
            Notes = context.Notes(),
        };
    }

    private sealed class HypothesisContext
    {
        private static readonly HashSet<string> Columns = new(StringComparer.Ordinal) { "predicted", "evidence", "collides" };
        private readonly Func<QueryParseEvidence?>? _parse;
        private QueryParseEvidence? _parsed;
        private bool _parseRead;
        private int _collidesRows, _collidesUnmeasured;

        public HypothesisContext(QueryHypothesis change, Func<string, string> fold, QueryHypothesisInputs inputs)
        {
            if (string.IsNullOrWhiteSpace(change.Dropped) || change.Dropped.Length > 8 || change.Gained is null ||
                change.Gained.Length > 8 || change.At is not ("final" or "initial") || change.Affixes is null ||
                change.Affixes.Count is < 1 or > 10 || change.Controls is < 1 or > 9)
                throw new ArgumentException("A change needs dropped letters (1 to 8), gained letters (0 to 8), at final or initial, " +
                    "1 to 10 affixes and 1 to 9 controls.");
            Fold = fold;
            Change = change;
            AtFinal = change.At == "final";
            Dropped = fold(change.Dropped);
            Gained = fold(change.Gained);
            Affixes = [.. change.Affixes.Select(a => fold(a.Trim('-'))).Where(a => a.Length > 0).Distinct(StringComparer.Ordinal)];
            if (Affixes.Length == 0 || Affixes.Any(a => a.Length > 16)) throw new ArgumentException("An affix is 1 to 16 letters.");
            EvidenceMeasured = change.WordSets.Count > 0;
            Evidence = FoldAll(inputs.Evidence);
            Discovery = FoldAll(inputs.Discovery);
            HeldOut = FoldAll(inputs.HeldOut);
            foreach (var (form, entry) in inputs.Forms)
            {
                var key = fold(form);
                if (!Owners.TryGetValue(key, out var owner)) Owners[key] = (entry, false);
                else if (owner.First != entry) Owners[key] = (owner.First, true);
            }
            _parse = inputs.Parse;
        }

        public Func<string, string> Fold { get; }
        public QueryHypothesis Change { get; }
        public bool AtFinal { get; }
        public string Dropped { get; }
        public string Gained { get; }
        public string[] Affixes { get; }
        public bool EvidenceMeasured { get; }
        public HashSet<string> Evidence { get; }
        public HashSet<string> Discovery { get; }
        public HashSet<string> HeldOut { get; }
        public Dictionary<string, (string First, bool Many)> Owners { get; } = new(StringComparer.Ordinal);

        public static bool IsHypothesisColumn(string column) => Columns.Contains(column);
        public static bool IsHypothesisSet(string column) => column is "predicted" or "collides";

        private HashSet<string> FoldAll(IReadOnlyCollection<string> words)
        {
            var set = new HashSet<string>(words.Count, StringComparer.Ordinal);
            foreach (var word in words) set.Add(Fold(word));
            return set;
        }

        public QueryParseEvidence? Parse()
        {
            if (!_parseRead)
            {
                _parseRead = true;
                var read = _parse?.Invoke();
                _parsed = read is null ? null : new QueryParseEvidence(
                    read.Parses.GroupBy(p => Fold(p.Key), StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Any(p => p.Value), StringComparer.Ordinal), read.Stale);
            }
            return _parsed;
        }

        public string[] Form(string stem, string gained, bool unchanged) =>
            [.. Affixes.Select(affix => AtFinal
                ? (unchanged ? stem : stem[..^Dropped.Length] + gained) + affix
                : affix + (unchanged ? stem : gained + stem[Dropped.Length..]))];

        public string[] CollidesOf(string stem, string entry, string[] predicted)
        {
            _collidesRows++;
            var hit = new List<string>();
            if (predicted.Any(form => Owners.TryGetValue(form, out var owner) && (owner.Many || owner.First != entry)))
                hit.Add("entry");
            var parse = Parse();
            var unknown = false;
            if (parse is not null)
            {
                var parses = false;
                foreach (var form in predicted)
                    if (parse.Parses.TryGetValue(form, out var does)) parses |= does; else unknown = true;
                if (parses) hit.Add("parses");
            }
            if (hit.Count > 0) return [.. hit];
            if (parse is null || unknown || parse.Stale)
            {
                if (parse is null || unknown) _collidesUnmeasured++;
                return ["not-measured"];
            }
            return ["none"];
        }

        public IReadOnlyList<string>? Notes()
        {
            var notes = new List<string>();
            if (!EvidenceMeasured)
                notes.Add("Not measured: no --wordset, so evidence reads not-measured and reliability, tolerance without --exception and the evidence selectors cannot run.");
            if (_collidesRows > 0)
            {
                var parse = Parse();
                if (parse is null)
                    notes.Add("Not measured: the project has no current parser evidence, so collides cannot say whether a predicted form already parses; " +
                        "it shows not-measured unless a predicted form is another entry's form.");
                else
                {
                    if (parse.Stale)
                        notes.Add("Not measured in full: the parser evidence is stale (the project was saved after the run), so a row with no parse in the old run reads not-measured, not none.");
                    if (_collidesUnmeasured > 0)
                        notes.Add($"Not measured: {_collidesUnmeasured} of {_collidesRows} rows evaluated for collides have a predicted form the parser run did not measure; " +
                            "they read not-measured, not none.");
                }
            }
            return notes.Count == 0 ? null : notes;
        }

        public QueryScore Score(string name, QueryRecord query, List<HypothesisRow> matched, QuerySpec spec)
        {
            var unit = query.Unit;
            return name switch
            {
                "reliability" => Reliability(matched, unit ?? "msa"),
                "tolerance" => Tolerance(query, matched, spec, unit ?? "msa"),
                _ => Control(matched, unit),
            };
        }

        private static IEnumerable<HypothesisRow> InUnit(List<HypothesisRow> matched, string unit) =>
            unit == "stem" ? matched.GroupBy(r => r.Folded, StringComparer.Ordinal).Select(g => g.First()) : matched;

        private QueryScore Reliability(List<HypothesisRow> matched, string unit)
        {
            if (!EvidenceMeasured)
                throw new QueryNotMeasuredException("Reliability needs an evidence word set (--wordset); without one the evidence of every row is not measured.");
            var rows = InUnit(matched, unit).ToList();
            long hits = rows.Count(r => r.Evidence is "rule" or "both");
            long scope = rows.Count(r => r.Evidence != "none");
            double? ratio = null, lower = null, p = null;
            var notes = new List<string> { $"Counts are {unit}s; a row with no evidence is outside the scope, not a miss." };
            if (scope > 0)
            {
                ratio = (double)hits / scope;
                p = (hits + 0.5) / (scope + 1);
                lower = p - 0.674 * Math.Sqrt(p.Value * (1 - p.Value) / scope);
            }
            else notes.Add("No row has evidence, so the limit is undefined.");
            return new QueryScore("reliability", ReliabilityVersion, unit,
                new Dictionary<string, long> { ["hits"] = hits, ["scope"] = scope, ["universe"] = rows.Count },
                new Dictionary<string, double?> { ["ratio"] = ratio, ["adjusted"] = p, ["lower"] = lower }, null, notes);
        }

        private QueryScore Tolerance(QueryRecord query, List<HypothesisRow> matched, QuerySpec spec, string unit)
        {
            var notes = new List<string>();
            long e;
            var rows = InUnit(matched, unit).ToList();
            if (query.Exceptions is { Count: > 0 } exceptions)
            {
                var tests = exceptions.Select(c => QueryEngine.Compile(c, spec)).ToArray();
                if (unit == "stem")
                {
                    e = matched.Where(r => tests.All(t => t(r))).Select(r => r.Folded).Distinct(StringComparer.Ordinal).Count();
                    notes.Add("Exceptions are the distinct stems with a row the --exception selectors match.");
                }
                else
                {
                    e = rows.Count(r => tests.All(t => t(r)));
                    notes.Add("Exceptions are the universe rows the --exception selectors match, counted in MSAs.");
                }
            }
            else
            {
                if (!EvidenceMeasured)
                    throw new QueryNotMeasuredException("Tolerance counts the rows whose evidence contradicts the change unless --exception names them; " +
                        "give an evidence word set (--wordset) or --exception.");
                e = rows.Count(r => r.Evidence == "base");
                notes.Add("Exceptions are the rows whose unchanged form is attested and whose predicted form is not (evidence base).");
            }
            long n = rows.Count;
            double? threshold = n >= 2 ? n / Math.Log(n) : null;
            string? verdict = null;
            if (threshold is { } limit) verdict = e < limit ? "productive" : "not-productive";
            else notes.Add("N is below 2, so N/ln N is undefined.");
            return new QueryScore("tolerance", ToleranceVersion, unit,
                new Dictionary<string, long> { ["N"] = n, ["e"] = e },
                new Dictionary<string, double?> { ["threshold"] = threshold }, verdict, notes);
        }

        private QueryScore Control(List<HypothesisRow> matched, string? unit)
        {
            if (unit == "msa")
                throw new QueryMixedUnitsException("Control counts distinct stem forms against word sets, not MSAs; use --unit stem or no --unit.");
            if (HeldOut.Count == 0 || Discovery.Count == 0)
                throw new QueryNotMeasuredException("Control needs a discovery word set and a held-out word set (--discovery, --heldout); " +
                    "without them its hits are not measured.");
            var stems = matched.Select(r => r.Folded).Distinct(StringComparer.Ordinal).ToList();
            var gained = Gained;
            (int Predicted, int Hits) Hits(string letters)
            {
                var predicted = 0;
                var hits = 0;
                foreach (var stem in stems)
                    foreach (var form in Form(stem, letters, unchanged: false))
                    {
                        predicted++;
                        if (HeldOut.Contains(form) && !Discovery.Contains(form)) hits++;
                    }
                return (predicted, hits);
            }
            var (count, rule) = Hits(gained);
            var letters = PickControls(Dropped, gained, Change.Controls, HeldOut.Union(Discovery), Change.ControlLetters?.Select(Fold).ToList());
            var controls = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var letter in letters) controls[letter] = Hits(letter).Hits;
            var best = controls.Values.DefaultIfEmpty(0).Max();
            return new QueryScore("control", ControlVersion, "stem",
                new Dictionary<string, long> { ["predicted"] = count, ["rule_hits"] = rule, ["margin"] = rule - best, ["stems"] = stems.Count },
                new Dictionary<string, double?>(), Verdict(rule, controls.Values.ToList()),
                ["Held-out results are counts only; no word or form from the held-out set is returned."]) { Controls = controls };
        }

        private static string Verdict(long ruleHits, List<long> controlHits)
        {
            var best = controlHits.Count == 0 ? 0 : controlHits.Max();
            if (ruleHits <= best) return best >= ControlMinimumHits ? "FAIL" : "UNDECIDED";
            if (ruleHits < ControlMinimumHits) return "UNDECIDED";
            return ruleHits >= ControlRatio * Math.Max(best, 1) ? "PASS" : "UNDECIDED";
        }

        private static readonly string[] Groups = ["pbf", "td", "kgqc", "szxj", "mn", "lr", "wy", "h"];

        // Same class as the gained letters, same place or manner first, then by frequency.
        private static List<string> PickControls(string dropped, string gained, int n, IEnumerable<string> types, IReadOnlyList<string>? explicitLetters)
        {
            if (explicitLetters is { Count: > 0 }) return [.. explicitLetters.Take(n)];
            var frequency = new Dictionary<char, int>();
            foreach (var word in types)
                foreach (var ch in word)
                    if (char.IsLetter(ch)) frequency[ch] = frequency.GetValueOrDefault(ch) + 1;
            var vowel = gained.Length == 1 && "aeiou".Contains(gained[0], StringComparison.Ordinal);
            var group = Groups.FirstOrDefault(g => g.Contains(gained, StringComparison.Ordinal)) ?? "";
            return [.. frequency.Keys
                .Where(c => !(dropped.Length == 1 && dropped[0] == c) && !(gained.Length == 1 && gained[0] == c) &&
                    "aeiou".Contains(c, StringComparison.Ordinal) == vowel)
                .OrderBy(c => group.Length > 0 && group.Contains(c, StringComparison.Ordinal) ? 0 : 1)
                .ThenByDescending(c => frequency[c]).ThenBy(c => c)
                .Take(n).Select(c => c.ToString())];
        }
    }

    private sealed class HypothesisRow : IQueryRowView
    {
        private readonly StoredValues _inner;
        private readonly HypothesisContext _context;
        private string[]? _predicted, _unchanged, _collides;
        private string? _evidence;

        public HypothesisRow(StoredValues inner, HypothesisContext context)
        {
            _inner = inner;
            _context = context;
            Folded = context.Fold(inner.Get("form") is { Count: > 0 } form ? form[0] : "");
            Applicable = context.AtFinal ? Folded.EndsWith(context.Dropped, StringComparison.Ordinal)
                : Folded.StartsWith(context.Dropped, StringComparison.Ordinal);
        }

        public string Folded { get; }
        public bool Applicable { get; }
        public string Id => _inner.Id;
        public string SortKey => _inner.SortKey;
        public long? Tokens => null;
        private string[] Predicted => _predicted ??= _context.Form(Folded, _context.Gained, unchanged: false);

        public string Evidence => _evidence ??= !_context.EvidenceMeasured ? "not-measured" : (
            Predicted.Any(_context.Evidence.Contains), (_unchanged ??= _context.Form(Folded, "", unchanged: true)).Any(_context.Evidence.Contains))
            switch { (true, true) => "both", (true, false) => "rule", (false, true) => "base", _ => "none" };

        private string[] Collides => _collides ??= _context.CollidesOf(Folded, _inner.Get("entry") is { Count: > 0 } e ? e[0] : "", Predicted);

        public IReadOnlyList<string> Get(string column) => column switch
        {
            "predicted" => Predicted,
            "evidence" => [Evidence],
            "collides" => Collides,
            _ => _inner.Get(column),
        };

        public QueryItem Project()
        {
            var item = _inner.Project();
            var values = new Dictionary<string, string[]>(item.Values, StringComparer.Ordinal)
            {
                ["predicted"] = Predicted, ["evidence"] = [Evidence], ["collides"] = Collides,
            };
            return new QueryItem(item.Id, values);
        }
    }
}
