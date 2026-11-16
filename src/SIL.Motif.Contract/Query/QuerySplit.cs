using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query;

/// <summary>One column value of a split and what it explains about the deviants.</summary>
/// <param name="Column">The context column, such as <c>final:2</c> or <c>category</c>.</param>
/// <param name="Value">The column value the context names.</param>
/// <param name="Scope">The universe rows that have the value.</param>
/// <param name="Deviants">The deviants among them.</param>
/// <param name="Lower">The lower confidence limit of deviants over scope; null when the scope is too small to judge.</param>
/// <param name="Covers">Whether the deviants the context misses are tolerable; null when not judged.</param>
/// <param name="Reliable">Whether the non-deviants inside the context are tolerable; null when not judged.</param>
/// <param name="Smaller">Whether the context and its residual tags are fewer objects and references than listing the deviants; null when not judged.</param>
/// <param name="Expressible"><c>yes</c>, <c>no</c> or <c>not-checked</c>: whether FieldWorks can state the context as a rule condition.</param>
/// <param name="HeldOut"><c>holds</c>, <c>fails</c>, <c>too-few</c> or <c>not-checked</c>; counts only, never words.</param>
/// <param name="Status"><c>A</c> (sub-rule), <c>B</c> (predictable, not a rule), <c>not-checked</c>, <c>partial</c>, <c>refuted</c> or <c>too-few-to-tell</c>.</param>
public sealed record QueryContext(string Column, string Value, long Scope, long Deviants, double? Lower, bool? Covers,
    bool? Reliable, bool? Smaller, string Expressible, string HeldOut, string Status)
{
    /// <summary>1 for a single context, 2 for a context found among the rows the first one leaves.</summary>
    public int Depth { get; init; } = 1;

    /// <summary>For depth 2, the first context whose rows this one lies outside; null at depth 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Outside { get; init; }

    /// <summary>Objects and references before and after the encoding this context proposes; null when not judged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, long>? Size { get; init; }

    /// <summary>Held-out deviants and conformers inside the context; present only when at least twenty held-out words were counted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, long>? HeldOutCounts { get; init; }

    /// <summary>Rows that have the context and are not deviants, most frequent first.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CounterExamples { get; init; }

    /// <summary>The context as <c>column=value</c>.</summary>
    [JsonIgnore]
    public string Label => Column + "=" + Value;
}

/// <summary>One example row of an evidence screen.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Text">The form shown.</param>
/// <param name="Tokens">Token frequency, or null when the evidence does not carry it.</param>
public sealed record QueryExample(string Id, string Text, long? Tokens);

/// <summary>A word question for a speaker: a row the evidence cannot decide.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Text">The question, in plain words.</param>
public sealed record QueryQuestion(string Id, string Text);

/// <summary>How many members share one shape and what the hypothesis ledger already says about it.</summary>
/// <param name="Key">The shape key.</param>
/// <param name="Count">The members of the largest group of one shape.</param>
/// <param name="RuleAttemptRequired">Whether the group is large enough that a rule attempt must be on record.</param>
/// <param name="Change">The change the ledger is asked about.</param>
public sealed record QuerySameShape(string Key, long Count, bool RuleAttemptRequired, string Change)
{
    /// <summary>What the ledger holds for the shape; null until a front end that holds the ledger fills it in.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Responses.HypothesisNeighboursResponse? Ledger { get; init; }
}

/// <summary>The one-screen answer to "what would make me think this grouping is a rule".</summary>
/// <param name="Source"><c>stems</c> or <c>forms</c>.</param>
/// <param name="Members">The declared units of the grouping.</param>
/// <param name="Universe">The declared units of the whole source the grouping is compared with.</param>
/// <param name="Shared">The contexts at least half the members share, best first, with their counter-examples.</param>
/// <param name="TooFew">How many other contexts matched fewer than twenty units and are not judged.</param>
/// <param name="Scores">The scorers that could be measured: split, tolerance, reliability, control and size.</param>
/// <param name="Examples">The members to quote, most frequent first.</param>
/// <param name="Questions">Members the evidence cannot decide, as questions for a speaker.</param>
/// <param name="SameShape">The same-shape count; null when the grouping has no shape.</param>
/// <param name="NotMeasured">Each part that was not measured and why; a part listed here is never shown as 0 or as complete.</param>
public sealed record QueryEvidence(string Source, long Members, long Universe, IReadOnlyList<QueryContext> Shared, long TooFew,
    IReadOnlyList<QueryScore> Scores, IReadOnlyList<QueryExample> Examples, IReadOnlyList<QueryQuestion> Questions,
    QuerySameShape? SameShape, IReadOnlyList<string> NotMeasured);

/// <summary>What the split of one universe found, before it is shaped for a front end.</summary>
internal sealed record SplitOutcome(long N, long E, string? Verdict, IReadOnlyList<QueryContext> Contexts,
    IReadOnlyList<QueryContext> Judged, long TooFew, IReadOnlyList<string> Notes);

/// <summary>What the split needs to know about its rows and about FieldWorks.</summary>
/// <param name="Rows">The universe.</param>
/// <param name="Deviant">Whether a row is a deviant of the universe's rule.</param>
/// <param name="Columns">The context columns tallied.</param>
/// <param name="Expressible">Whether a column value is expressible: <c>yes</c>, <c>no</c> or <c>not-checked</c>.</param>
/// <param name="HeldOut">0 when a row has no held-out evidence, 1 when its predicted form is a held-out hit, 2 when only its unchanged form is; null when held-out is not checked.</param>
/// <param name="StemKey">Identifies the stem of a row, so a stem counts once per context in the held-out tally.</param>
/// <param name="HeldOutWhy">Why held-out is not checked, when <paramref name="HeldOut"/> is null.</param>
/// <param name="Frequency">The token frequency of a row; null when not measured.</param>
/// <param name="Label">The form shown for a row.</param>
internal sealed record SplitInput(IReadOnlyList<IQueryRowView> Rows, Func<IQueryRowView, bool> Deviant,
    IReadOnlyList<string> Columns, Func<string, string, string> Expressible, Func<IQueryRowView, int>? HeldOut,
    Func<IQueryRowView, string>? StemKey, string? HeldOutWhy, Func<IQueryRowView, long?>? Frequency,
    Func<IQueryRowView, string> Label)
{
    public Func<IQueryRowView, string> UnitKey { get; init; } = row => row.Id;
}

/// <summary>
/// One pass tallies every context value over a universe and its deviants; a context is judged by the criteria of the
/// rule-with-deviants decision: coverage, reliability, size, expressibility and held-out. Depth is two: single contexts,
/// then one more context among the rows the most covering reliable one leaves.
/// </summary>
internal sealed class SplitAnalysis
{
    public const int MinimumScope = 20;
    public const string Version = "split/2 declared units; unknown contexts excluded; coverage e-h<e/ln e; reliability n-h<n/ln n; size; expressible; held-out; depth 2; scope under 20 is too few to tell";
    public const string SizeVersion = "size/2 declared reference units; objects and tag references, one unit each (the units of the grammar size delta)";
    private const string TooFewToTell = "too-few-to-tell";
    private const string Rejected = "refuted";

    private readonly SplitInput _input;
    private readonly bool[] _deviant;
    private readonly int[][] _keys;
    private readonly sbyte[] _held;
    private readonly List<(string Column, string Value)> _names = [];
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _expressible = [];
    private readonly bool _heldOutOn;
    private readonly Dictionary<string, HashSet<string>> _missing = new(StringComparer.Ordinal);

    public SplitAnalysis(SplitInput input)
    {
        _input = input;
        var rows = input.Rows;
        _deviant = new bool[rows.Count];
        _keys = new int[rows.Count][];
        _held = new sbyte[rows.Count];
        _heldOutOn = input.HeldOut is not null && input.StemKey is not null;
        var deviantUnits = rows.Where(input.Deviant).Select(input.UnitKey).ToHashSet(StringComparer.Ordinal);
        var scratch = new List<int>(32);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            _deviant[i] = deviantUnits.Contains(input.UnitKey(row));
            scratch.Clear();
            foreach (var column in input.Columns)
            {
                var values = row.Get(column);
                if (values.Count == 0)
                {
                    if (column is "features" or "environments") Add(scratch, column, "none");
                    continue;
                }
                foreach (var value in values)
                    if (value == "not-measured")
                    {
                        if (!_missing.TryGetValue(column, out var units)) _missing[column] = units = [];
                        units.Add(input.UnitKey(row));
                    }
                    else Add(scratch, column, value);
            }
            _keys[i] = [.. scratch];
            if (_heldOutOn) _held[i] = (sbyte)input.HeldOut!(row);
        }
        N = rows.Select(input.UnitKey).Distinct().LongCount();
        E = deviantUnits.Count;
    }

    public long N { get; }
    public long E { get; }

    private void Add(List<int> keys, string column, string value)
    {
        var key = column + "\0" + value;
        if (!_ids.TryGetValue(key, out var id))
        {
            id = _names.Count;
            _ids.Add(key, id);
            _names.Add((column, value));
        }
        if (!keys.Contains(id)) keys.Add(id);
    }

    private sealed record Tally(long[] Scope, long[] Deviants, long[] HeldDeviants, long[] HeldRule);

    private Tally Count(Func<int, bool> include)
    {
        var size = _names.Count;
        var tally = new Tally(new long[size], new long[size], new long[size], new long[size]);
        var scopes = new HashSet<(int, string)>();
        var deviants = new HashSet<(int, string)>();
        var seen = _heldOutOn ? new HashSet<(int, string)>() : null;
        for (var i = 0; i < _keys.Length; i++)
        {
            if (!include(i)) continue;
            var stem = _held[i] != 0 ? _input.StemKey!(_input.Rows[i]) : null;
            foreach (var key in _keys[i])
            {
                if (scopes.Add((key, _input.UnitKey(_input.Rows[i])))) tally.Scope[key]++;
                if (_deviant[i] && deviants.Add((key, _input.UnitKey(_input.Rows[i])))) tally.Deviants[key]++;
                if (stem is null || !seen!.Add((key, stem))) continue;
                if (_held[i] == 1) tally.HeldRule[key]++; else tally.HeldDeviants[key]++;
            }
        }
        return tally;
    }

    private static double? Limit(long n) => n >= 2 ? n / Math.Log(n) : null;

    private string Expressible(int key)
    {
        if (!_expressible.TryGetValue(key, out var value))
            _expressible[key] = value = _input.Expressible(_names[key].Column, _names[key].Value);
        return value;
    }

    private string HeldOutOf(long deviants, long rule, bool deviantsAreEvidence, out IReadOnlyDictionary<string, long>? counts)
    {
        counts = null;
        if (!_heldOutOn || !deviantsAreEvidence) return "not-checked";
        var total = deviants + rule;
        if (total < MinimumScope) return "too-few";
        counts = new Dictionary<string, long> { ["deviants"] = deviants, ["rule"] = rule };
        return rule < total / Math.Log(total) ? "holds" : "fails";
    }

    private static (long Objects, long References) ListSize(long e) => (e > 0 ? 1 : 0, e);

    private static (long Objects, long References) ContextSize(int contexts, long uncovered, long counter) =>
        (contexts + (uncovered > 0 ? 1 : 0) + (counter > 0 ? 1 : 0), uncovered + counter);

    private static string Judge(bool tooFew, bool covers, bool reliable, bool smaller, string expressible, string heldOut)
    {
        if (tooFew) return TooFewToTell;
        if (!covers) return reliable ? "partial" : Rejected;
        if (!reliable || !smaller) return Rejected;
        if (expressible == "no") return "B";
        if (expressible == "yes" && heldOut == "holds") return "A";
        return expressible == "yes" && heldOut == "fails" ? Rejected : "not-checked";
    }

    private static string CombineExpressible(string a, string b) =>
        a == "no" || b == "no" ? "no" : a == "not-checked" || b == "not-checked" ? "not-checked" : "yes";

    private static string CombineHeldOut(string a, string b) =>
        a == "fails" || b == "fails" ? "fails" : a == "not-checked" || b == "not-checked" ? "not-checked"
            : a == "too-few" || b == "too-few" ? "too-few" : "holds";

    private static int Rank(string status) => status switch
        { "A" => 0, "B" => 1, "not-checked" => 2, "partial" => 3, Rejected => 4, _ => 5 };

    private IEnumerable<QueryContext> Ranked(IEnumerable<QueryContext> contexts) => contexts
        .OrderBy(c => Rank(c.Status)).ThenByDescending(c => c.Lower ?? -1).ThenByDescending(c => c.Deviants)
        .ThenBy(c => c.Column, StringComparer.Ordinal).ThenBy(c => c.Value, StringComparer.Ordinal);

    /// <summary>Judges every context and returns the best <paramref name="limit"/> beside a few too-few ones.</summary>
    /// <param name="limit">How many judged contexts to return.</param>
    /// <param name="deviantsAreEvidence">Whether the deviants are rows whose unchanged form alone is attested, which is what held-out can recount.</param>
    public SplitOutcome Run(int limit, bool deviantsAreEvidence)
    {
        var notes = _missing.Select(m => $"Not measured: {m.Key} is missing for {m.Value.Count} units; excluded from context judging.").ToList();
        if (E == 0)
        {
            notes.Add("There are no deviants, so there is nothing to split.");
            return new SplitOutcome(N, E, null, [], [], 0, notes);
        }
        if (E == 1) notes.Add("One deviant is a lexical entry, not a pattern: coverage needs at least two.");
        var coverageLimit = Limit(E) ?? 0;
        var listing = ListSize(E);
        var first = Count(_ => true);
        var judged = new List<QueryContext>();
        long tooFew = 0;
        var tooFewList = new List<QueryContext>();
        for (var key = 0; key < _names.Count; key++)
        {
            var scope = first.Scope[key];
            if (scope == N || scope == 0) continue;
            var deviants = first.Deviants[key];
            if (scope < MinimumScope)
            {
                tooFew++;
                tooFewList.Add(TooFewContext(key, scope, deviants, 1, null));
                continue;
            }
            var covers = E - deviants < coverageLimit;
            var reliable = scope - deviants < scope / Math.Log(scope);
            var size = ContextSize(1, E - deviants, scope - deviants);
            var smaller = size.Objects + size.References < listing.Objects + listing.References;
            var expressible = Expressible(key);
            var heldOut = HeldOutOf(first.HeldDeviants[key], first.HeldRule[key], deviantsAreEvidence, out var counts);
            judged.Add(new QueryContext(_names[key].Column, _names[key].Value, scope, deviants, Reliability(deviants, scope),
                covers, reliable, smaller, expressible, heldOut, Judge(false, covers, reliable, smaller, expressible, heldOut))
            { Size = SizeCounts(listing, size), HeldOutCounts = counts });
        }
        if (!judged.Any(c => c.Covers == true))
            judged.AddRange(SecondSplit(first, judged, coverageLimit, listing, deviantsAreEvidence, ref tooFew, tooFewList));
        var ranked = Ranked(judged).ToList();
        var contexts = ranked.Take(limit).Concat(tooFewList.OrderByDescending(c => c.Deviants)
            .ThenBy(c => c.Column, StringComparer.Ordinal).ThenBy(c => c.Value, StringComparer.Ordinal).Take(3)).ToList();
        var verdict = ranked.Any(c => c.Status == "A") ? "A" : ranked.Any(c => c.Status == "B") ? "B"
            : ranked.Any(c => c.Status == "not-checked") ? "not-checked" : "C";
        if (verdict == "C" && judged.Any(c => c.Covers == true && c.Reliable == true))
            notes.Add("A context covers and predicts the deviants but encoding it is not smaller than listing them.");
        if (tooFew > 0)
            notes.Add($"{tooFew} contexts matched fewer than {MinimumScope} units: too few to tell, not judged.");
        if (judged.Any(c => c.Status == "not-checked"))
            notes.Add("not-checked: covers, reliable and smaller, but whether FieldWorks can state it, or whether held-out confirms it, was not measured.");
        return new SplitOutcome(N, E, E < 2 ? null : verdict, contexts, ranked, tooFew, notes);
    }

    private static double? Reliability(long hits, long scope) => FormQuery.ReliabilityOf(hits, scope).Lower;

    private QueryContext TooFewContext(int key, long scope, long deviants, int depth, string? outside) =>
        new(_names[key].Column, _names[key].Value, scope, deviants, null, null, null, null, "n/a", "n/a", TooFewToTell)
        { Depth = depth, Outside = outside };

    private static IReadOnlyDictionary<string, long> SizeCounts((long Objects, long References) before, (long Objects, long References) after) =>
        new Dictionary<string, long>
        {
            ["before_objects"] = before.Objects, ["before_references"] = before.References,
            ["after_objects"] = after.Objects, ["after_references"] = after.References,
        };

    private List<QueryContext> SecondSplit(Tally first, List<QueryContext> judged, double coverageLimit,
        (long Objects, long References) listing, bool deviantsAreEvidence, ref long tooFew, List<QueryContext> tooFewList)
    {
        var found = new List<QueryContext>();
        var carve = judged.Where(c => c.Reliable == true).OrderByDescending(c => c.Deviants)
            .ThenByDescending(c => c.Lower ?? -1).ThenBy(c => c.Column, StringComparer.Ordinal)
            .ThenBy(c => c.Value, StringComparer.Ordinal).FirstOrDefault();
        if (carve is null) return found;
        var carveKey = _ids[carve.Column + "\0" + carve.Value];
        var carvedUnits = Enumerable.Range(0, _keys.Length).Where(i => _keys[i].Contains(carveKey))
            .Select(i => _input.UnitKey(_input.Rows[i])).ToHashSet(StringComparer.Ordinal);
        var outside = Count(i => !carvedUnits.Contains(_input.UnitKey(_input.Rows[i])));
        var remaining = N - carve.Scope;
        var label = carve.Label;
        for (var key = 0; key < _names.Count; key++)
        {
            var scope = outside.Scope[key];
            if (key == carveKey || scope == remaining || scope == 0) continue;
            var deviants = outside.Deviants[key];
            if (scope < MinimumScope)
            {
                tooFew++;
                tooFewList.Add(TooFewContext(key, scope, deviants, 2, label));
                continue;
            }
            var covers = E - carve.Deviants - deviants < coverageLimit;
            var reliable = scope - deviants < scope / Math.Log(scope);
            var size = ContextSize(2, E - carve.Deviants - deviants, carve.Scope - carve.Deviants + scope - deviants);
            var smaller = size.Objects + size.References < listing.Objects + listing.References;
            var expressible = CombineExpressible(carve.Expressible, Expressible(key));
            var heldOut = CombineHeldOut(carve.HeldOut,
                HeldOutOf(outside.HeldDeviants[key], outside.HeldRule[key], deviantsAreEvidence, out var counts));
            found.Add(new QueryContext(_names[key].Column, _names[key].Value, scope, deviants, Reliability(deviants, scope),
                covers, reliable, smaller, expressible, heldOut, Judge(false, covers, reliable, smaller, expressible, heldOut))
            { Depth = 2, Outside = label, Size = SizeCounts(listing, size), HeldOutCounts = counts });
        }
        return found;
    }

    /// <summary>The rows that have a context and are not deviants, most frequent first, as the forms they show.</summary>
    public IReadOnlyList<string> CounterExamples(QueryContext context, int count)
    {
        var key = _ids[context.Column + "\0" + context.Value];
        var label = _input.Label;
        var frequency = _input.Frequency;
        var rows = new List<(string Label, long Tokens)>();
        for (var i = 0; i < _keys.Length; i++)
            if (!_deviant[i] && _keys[i].Contains(key))
                rows.Add((label(_input.Rows[i]), frequency?.Invoke(_input.Rows[i]) ?? 0));
        return [.. rows.OrderByDescending(r => r.Tokens).ThenBy(r => r.Label, StringComparer.Ordinal).Take(count).Select(r => r.Label)];
    }

    /// <summary>The objects and references of listing the deviants against one context and its residual tags, or against tagging the other polarity.</summary>
    public static QueryScore SizeScore(long n, long e, QueryContext? context, long contextScope, long contextDeviants, string beforeNote, bool hasContext = false, string unit = "row")
    {
        var before = ListSize(e);
        (long Objects, long References) after;
        string note;
        if (hasContext || context is not null || contextScope > 0)
        {
            after = ContextSize(1, e - contextDeviants, contextScope - contextDeviants);
            note = "after: one context, then a tag for each deviant outside it and for each non-deviant inside it.";
        }
        else
        {
            after = ListSize(n - e);
            note = "after: tag the non-deviants instead of the deviants, the other polarity.";
        }
        var beforeTotal = before.Objects + before.References;
        var afterTotal = after.Objects + after.References;
        return new QueryScore("size", SizeVersion, unit,
            new Dictionary<string, long>
            {
                ["before_objects"] = before.Objects, ["before_references"] = before.References, ["before_total"] = beforeTotal,
                ["after_objects"] = after.Objects, ["after_references"] = after.References, ["after_total"] = afterTotal,
                ["saved"] = beforeTotal - afterTotal,
            },
            new Dictionary<string, double?>(), afterTotal < beforeTotal ? "smaller" : afterTotal > beforeTotal ? "larger" : "equal",
            [beforeNote, note, $"References count {unit} units; objects count grammar objects."]);
    }
}
