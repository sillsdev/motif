using System.Globalization;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>One rule environment the planner wants a real neighbour for: a rule at one edge of the morpheme.</summary>
/// <param name="Key">The rule, its subrule and the edge, <c>name#subrule/edge</c>.</param>
/// <param name="Rule">The rule's name.</param>
/// <param name="Edge"><c>left</c> or <c>right</c>.</param>
/// <param name="Pattern">The rule as a <c>match</c> pattern.</param>
internal sealed record PlannerCell(string Key, string Rule, string Edge, string Pattern);

/// <summary>One real neighbour to generate the morpheme with, or the morpheme alone, and what it was chosen for.</summary>
/// <param name="Neighbour">The root or affix the morpheme is generated with; null for the morpheme alone.</param>
/// <param name="Side"><c>left</c> or <c>right</c> of the morpheme, or <c>none</c>.</param>
/// <param name="Cells">The cells the neighbour's edge satisfies, by key; empty for a control or an attested neighbour.</param>
/// <param name="Why"><c>cell</c>, <c>control</c> or <c>attested-neighbour</c>.</param>
internal sealed record PlannedContext(PlannerCandidate? Neighbour, string Side, IReadOnlyList<string> Cells, string Why)
{
    internal string Label => Why == "cell" ? string.Join(",", Cells) : Why;
}

/// <summary>The contexts to generate, and the cells no candidate satisfies.</summary>
/// <param name="Contexts">The neighbours to generate with.</param>
/// <param name="Uncovered">Cells no real neighbour in the lexicon satisfies.</param>
/// <param name="Cells">Every cell the rules gave.</param>
/// <param name="Capped">Cells that had a neighbour but were left out because the context cap was reached.</param>
/// <param name="Ignored">Cells at an edge the planner does not test, such as a suffix's right edge.</param>
internal sealed record RealizesPlan(IReadOnlyList<PlannedContext> Contexts, IReadOnlyList<PlannerCell> Uncovered, IReadOnlyList<PlannerCell> Cells,
    IReadOnlyList<PlannerCell> Capped, IReadOnlyList<PlannerCell> Ignored);

/// <summary>
/// The rule-chosen test-root planner. For each environment of the rules that may change the morpheme it picks real morphemes
/// whose edge satisfies it, by asking PanGloss's own <c>match</c> over the neighbour joined to the morpheme, then covers the
/// environments with as few neighbours as it can, and adds a control that triggers nothing so the default surface is known.
/// </summary>
internal static class RealizesPlanner
{
    internal const int MaxContexts = 8;
    internal const int PoolSize = 300;
    private const int PerEdgeSegment = 6;
    private const string EveryHead = "[Any]";

    public static RealizesPlan Plan(IRealizesGrammar grammar, IReadOnlyList<EffectRow> survivors, IReadOnlyList<EffectsAllomorph> allomorphs,
        bool targetIsStem, string anchor, IReadOnlyList<PlannerCandidate> lexicon, int k, int maxContexts, CancellationToken cancellationToken)
    {
        var sides = targetIsStem || anchor == "none" ? new[] { "left", "right" } : anchor == "end" ? ["left"] : new[] { "right" };
        var everyCell = survivors.Where(row => row.Text is { MatchPattern: not null, BoundaryMarker: null } && row.Rule.Kind == "rewrite" && !row.Text.MatchPattern.StartsWith("/ ", StringComparison.Ordinal))
            .GroupBy(row => (row.Rule.Id, row.Subrule, row.Edge)).Select(group =>
            {
                var first = group.First();
                var name = string.IsNullOrWhiteSpace(first.Rule.Name) ? first.Rule.Id : first.Rule.Name;
                return new PlannerCell(Format($"{name}#{first.Subrule}/{first.Edge}"), name, first.Edge, first.Text!.MatchPattern!);
            }).OrderBy(cell => cell.Key, StringComparer.Ordinal).ToList();
        var cells = everyCell.Where(cell => sides.Contains(cell.Edge)).ToList();
        var ignored = everyCell.Where(cell => !sides.Contains(cell.Edge)).ToList();
        var forms = allomorphs.Select(item => (Text: string.Concat(item.Segments), Count: item.Segments.Count)).Where(form => form.Text.Length > 0)
            .DistinctBy(form => form.Text, StringComparer.Ordinal).Take(3).ToList();
        var covers = new Dictionary<PlannerCandidate, List<string>>();
        var triggers = new HashSet<PlannerCandidate>();
        var uncovered = new List<PlannerCell>();
        foreach (var side in sides)
        {
            var sideCells = cells.Where(cell => cell.Edge == side).ToList();
            if (sideCells.Count == 0 || forms.Count == 0) continue;
            var pool = Pool(lexicon, targetIsStem, side);
            if (pool.Count == 0) { uncovered.AddRange(sideCells); continue; }
            var heads = side == "left" ? Segments(grammar, pool.Select(candidate => candidate.Form).ToList(), cancellationToken) : null;
            foreach (var cell in sideCells)
            {
                var composed = pool.SelectMany(candidate => forms.Select(form => (Candidate: candidate, Word: side == "left" ? candidate.Form + form.Text : form.Text + candidate.Form, form.Count))).ToList();
                var reply = grammar.Match(cell.Pattern, [.. composed.Select(item => item.Word)], cancellationToken);
                var hit = new List<PlannerCandidate>();
                for (var i = 0; i < composed.Count && i < reply.Words.Count; i++)
                {
                    var (candidate, _, formCount) = composed[i];
                    int? at = side == "right" ? formCount : heads is not null && heads.TryGetValue(candidate.Form, out var count) ? count : null;
                    if (at is null || reply.Words[i].Status != "ok") continue;
                    if (!reply.Words[i].Spans.Any(span => span.ReachStart < at && span.ReachEnd > at)) continue;
                    if (!hit.Contains(candidate)) hit.Add(candidate);
                }
                triggers.UnionWith(hit);
                if (hit.Count == 0) { uncovered.Add(cell); continue; }
                foreach (var candidate in Pick(hit, k))
                {
                    if (!covers.TryGetValue(candidate, out var list)) covers[candidate] = list = [];
                    list.Add(cell.Key);
                }
            }
        }
        var contexts = new List<PlannedContext>();
        var capped = new List<PlannerCell>();
        var perCell = cells.ToDictionary(cell => cell.Key, _ => 0);
        while (contexts.Count < maxContexts - 1 && covers.Count > 0)
        {
            var best = covers.OrderByDescending(pair => pair.Value.Count(key => perCell[key] < k)).ThenByDescending(pair => pair.Key.Tokens)
                .ThenBy(pair => pair.Key.Form, StringComparer.Ordinal).First();
            if (best.Value.All(key => perCell[key] >= k)) break;
            var side = cells.First(cell => cell.Key == best.Value[0]).Edge;
            contexts.Add(new PlannedContext(best.Key, side, [.. best.Value.Distinct()], "cell"));
            foreach (var key in best.Value.Distinct()) perCell[key]++;
            covers.Remove(best.Key);
        }
        capped.AddRange(cells.Where(cell => !uncovered.Contains(cell) && perCell[cell.Key] == 0));
        contexts.Add(Control(targetIsStem, anchor, lexicon, triggers));
        return new RealizesPlan(contexts, uncovered, cells, capped, ignored);
    }

    // A stem is generated alone; an affix needs the most frequent stem, which stands for the default surface.
    private static PlannedContext Control(bool targetIsStem, string anchor, IReadOnlyList<PlannerCandidate> lexicon, HashSet<PlannerCandidate> triggers)
    {
        if (targetIsStem) return new PlannedContext(null, "none", [], "control");
        var stem = lexicon.Where(candidate => candidate.Kind == "stem" && !triggers.Contains(candidate))
            .OrderByDescending(candidate => candidate.Tokens).ThenBy(candidate => candidate.Form, StringComparer.Ordinal).FirstOrDefault();
        return new PlannedContext(stem, anchor == "end" ? "left" : "right", [], "control");
    }

    // One affix of each kind is not enough for a stem; a stem is the neighbour of any affix.
    private static List<PlannerCandidate> Pool(IReadOnlyList<PlannerCandidate> lexicon, bool targetIsStem, string side)
    {
        var kind = targetIsStem ? (side == "left" ? "prefix" : "suffix") : "stem";
        var pick = lexicon.Where(candidate => candidate.Kind == kind).ToList();
        var edgeOf = side == "left" ? (Func<PlannerCandidate, string>)(candidate => candidate.Form[^1..]) : candidate => candidate.Form[..1];
        return [.. pick.GroupBy(edgeOf).SelectMany(group => group.OrderByDescending(candidate => candidate.Tokens)
                .ThenBy(candidate => candidate.Form, StringComparer.Ordinal).Take(PerEdgeSegment))
            .OrderByDescending(candidate => candidate.Tokens).ThenBy(candidate => candidate.Form, StringComparer.Ordinal).Take(PoolSize)];
    }

    // K neighbours with distinct edge segments first, then the most frequent.
    private static IEnumerable<PlannerCandidate> Pick(List<PlannerCandidate> hit, int k)
    {
        var chosen = new List<PlannerCandidate>();
        foreach (var candidate in hit.OrderByDescending(item => item.Tokens).ThenBy(item => item.Form, StringComparer.Ordinal))
            if (chosen.Count < k && !chosen.Any(item => item.Form[^1] == candidate.Form[^1] && item.Form[0] == candidate.Form[0])) chosen.Add(candidate);
        foreach (var candidate in hit.OrderByDescending(item => item.Tokens).ThenBy(item => item.Form, StringComparer.Ordinal))
            if (chosen.Count < k && !chosen.Contains(candidate)) chosen.Add(candidate);
        return chosen;
    }

    // How many segments each form has, counted by PanGloss itself with the match of every segment.
    private static Dictionary<string, int>? Segments(IRealizesGrammar grammar, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var reply = grammar.Match(EveryHead, words, cancellationToken);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < words.Count && i < reply.Words.Count; i++)
            if (reply.Words[i].Status == "ok") counts[words[i]] = reply.Words[i].Spans.Count;
        return counts;
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
