using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The <c>possible</c> tier: what a morpheme could look like, from the phonological rules PanGloss cannot rule out for it. One
/// <c>effects</c> call names the rules whose target is on the morpheme or may reach it across an edge; each survivor's rule row
/// gives its environment, its match pattern and its replacement. Where the replacement is one literal segment or a deletion the
/// variant is computed from the written form; the rest is searched by the rule's own match pattern, narrowed to the morpheme's
/// context. It is a superset, never an exact list, and says why it is not complete.
/// </summary>
internal static class RealizesPossible
{
    /// <summary>Variants enumerated per allomorph before the set stops combining positions.</summary>
    internal const int MaxVariants = 64;

    internal const string EnvironmentUnchecked = "environment_unchecked";

    private static readonly string[] Examined = [EnvironmentUnchecked];

    public static RealizesVariants Collect(IRealizesGrammar grammar, RealizesTarget target, CancellationToken cancellationToken, string? processes = null)
    {
        if (target.EntryId is null || target.MsaId is null) throw RealizesRefusals.NeedsIdentity(target.AnyId ?? "the morpheme");
        var clock = Stopwatch.StartNew();
        var result = grammar.Effects(new ServeEffectsRequest(new EffectsMorpheme(target.EntryId, target.MsaId)), cancellationToken);
        return Reduce(result, clock.Elapsed.TotalSeconds, processes);
    }

    /// <summary>The reduction of one effects answer; the seam the tests drive with a recorded reply.</summary>
    /// <param name="result">PanGloss's effects answer.</param>
    /// <param name="seconds">How long the effects call took.</param>
    /// <param name="processes">The affix processes the grammar has: null when that is not known, empty for none, else what they are.</param>
    internal static RealizesVariants Reduce(EffectsResult result, double seconds = 0, string? processes = null)
    {
        var allomorphs = result.Allomorphs.ToDictionary(item => item.AllomorphId);
        var survivors = result.Survivors.ToList();
        var allRules = result.Effects.Select(row => row.Rule.Id).Distinct(StringComparer.Ordinal).Count();
        var surviving = survivors.Select(row => row.Rule.Id).Distinct(StringComparer.Ordinal).ToList();
        var reasons = new List<RealizesReason>();
        void Reason(string code, IEnumerable<string> names, string what)
        {
            var list = names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return;
            var shown = string.Join(", ", list.Take(6)) + (list.Count > 6 ? $", and {list.Count - 6} more" : "");
            reasons.Add(new RealizesReason(code, Format($"{what}: {shown}.")));
        }
        if (!result.Complete || result.Partial)
            reasons.Add(new RealizesReason(RealizesReasons.EffectsPartial, $"The effects scan stopped at {result.StopReason ?? "a limit"}, so rules it did not reach are not listed."));
        foreach (var group in survivors.Where(row => !Examined.Contains(row.Reason ?? "")).GroupBy(row => row.Reason ?? "unspecified"))
            Reason("unmodelled:" + group.Key, group.Select(RuleName), $"PanGloss could not model {group.Key} for these rules, so a change to the morpheme through them is not excluded");
        Reason("unprintable", survivors.Where(row => row.Text is { } text && (text.MatchPattern is null || EmptyTarget(text.MatchPattern)))
                .Select(row => RuleName(row) + " (" + (row.Text!.MatchPattern is null ? string.Join(" ", row.Text.Unprintable) : "empty target") + ")"),
            "PanGloss could not print these rules as a pattern, so the corpus search cannot cover them");
        Reason("no_rule_text", survivors.Where(row => row.Text is null).Select(RuleName), "The reply carries no rule text for these rules (it is older than PanGloss 0.33.0)");
        Reason("nonlocal", survivors.Where(row => row.Text?.NonLocal == true).Select(row => RuleName(row) + " (" + string.Join(" ", row.Text!.NonLocalReasons) + ")"),
            "These rules may be non-local, so their match pattern can miss a rewritten position");
        Reason("metathesis", survivors.Where(row => row.Rule.Kind == "metathesis").Select(RuleName), "These are metathesis rules, outside what a match pattern covers");
        Reason("rule_not_compiled", survivors.Where(row => row.Rule.Status != "compiled").Select(RuleName), "These rules are not in the compiled grammar");
        Reason("boundary_rule", survivors.Where(row => row.Text?.BoundaryMarker is not null).Select(RuleName), "These rules need a boundary marker the corpus words do not carry");
        if (result.Allomorphs.Any(item => item.FormPartial))
            reasons.Add(new RealizesReason(RealizesReasons.FormPartial, "An allomorph copies or modifies stem material, so its written form is only the segments it writes."));

        // Effects shows no infix or stem change, so complete needs none in the grammar.
        if (processes is null)
            reasons.Add(new RealizesReason(RealizesReasons.MorphologicalProcess, "Whether the grammar has an infix, reduplication or stem-changing affix could not be checked, and effects does not report them."));
        else if (processes.Length > 0)
            reasons.Add(new RealizesReason(RealizesReasons.MorphologicalProcess, $"The grammar has {processes}, which can split or change the morpheme's material, and effects does not report them."));
        // Contexts vary one edge at a time, so a surviving rule always leaves both-neighbour combinations untested.
        if (survivors.Count > 0)
            reasons.Add(new RealizesReason(RealizesReasons.ContextCombinationsUntested, "A rule can change the morpheme, and its effect with both neighbours at once, or with another rule's output, is not tested."));

        var rules = survivors.GroupBy(row => (row.Rule.Id, row.Subrule)).Select(group =>
        {
            var first = group.First();
            return new RealizesRule(first.Rule.Id, RuleName(first), first.Subrule, [.. group.Select(row => row.Edge).Distinct(StringComparer.Ordinal)],
                first.Text?.Environment, first.Text?.MatchPattern, first.Text?.Rhs,
                string.Join("|", group.Select(row => "unknown/" + (row.Reason ?? "unspecified")).Distinct(StringComparer.Ordinal)),
                group.Any(row => row.Text?.NonLocal == true));
        }).OrderBy(rule => rule.Id, StringComparer.Ordinal).ThenBy(rule => rule.Subrule).ToList();

        var anchor = AnchorOf(result.Allomorphs);
        var forms = new Dictionary<string, (string Source, List<string> Rules)>(StringComparer.Ordinal);
        var capped = false;
        var unrealised = false;
        foreach (var allomorph in result.Allomorphs)
        {
            var written = Written(allomorph);
            if (written.Length > 0) forms.TryAdd(written, ("allomorph", []));
            var changes = survivors.Where(row => row.AllomorphId == allomorph.AllomorphId && row.Reason == EnvironmentUnchecked && Computed(row, allomorph) is not null)
                .SelectMany(row => row.LhsSpans.Select(span => (row, span))).Distinct()
                .Select(item => new Change(item.span.Start, item.span.End, Computed(item.row, allomorph)!, RuleName(item.row))).Distinct().ToList();
            var (variants, more) = Enumerate(allomorph.Segments, changes);
            foreach (var (shown, names) in variants)
            {
                var text = shown.Length == 0 ? Unrealised : shown;
                if (text == Unrealised) unrealised = true;
                if (!forms.TryGetValue(text, out var known)) forms[text] = known = ("rule", []);
                foreach (var name in names) if (!known.Rules.Contains(name)) known.Rules.Add(name);
            }
            capped |= more;
        }
        if (capped)
            reasons.Add(new RealizesReason(RealizesReasons.VariantsCapped, Format($"More than {MaxVariants} combinations of changes were possible for an allomorph, so only single changes were listed; the rule patterns still search the rest.")));

        if (unrealised)
            reasons.Add(new RealizesReason(RealizesReasons.MayBeUnrealised, "A rule may delete the whole morpheme, so it can have no segments in the word; that case is listed as the empty variant and cannot be searched."));

        var phon = new List<RealizesPhonClause>();
        var unsearched = new List<string>();
        var altered = new List<string>();
        foreach (var row in survivors)
        {
            if (row.Text is not { MatchPattern: { } pattern, BoundaryMarker: null } || row.Rule.Kind != "rewrite" || EmptyTarget(pattern)) continue;
            var allomorph = allomorphs.GetValueOrDefault(row.AllomorphId);
            if (allomorph is null) continue;
            if (row.Reason == EnvironmentUnchecked && row.LhsSpans.Count > 0 && Computed(row, allomorph) is not null) continue;
            var context = Context(allomorph, row, anchor);
            if (!phon.Any(item => item.Pattern == pattern && item.Context == context)) phon.Add(new RealizesPhonClause(pattern, context, RuleName(row), "input"));
            var output = OutputPattern(row);
            if (row.Text.OutputMayBeAltered) altered.Add(RuleName(row));
            if (output is null || EmptyTarget(output)) { unsearched.Add(RuleName(row)); continue; }
            if (!phon.Any(item => item.Pattern == output && item.Context == context)) phon.Add(new RealizesPhonClause(output, context, RuleName(row), "output"));
        }
        // A computed variant is searched as text, so only the rest need an output-side pattern.
        Reason("output_side_unsearched", unsearched, "The corpus holds words the rules have already changed, and PanGloss gave no pattern for what these rules write, so their output is not searched");
        Reason("rule_interaction", altered, "Another rule may change what these rules write");
        var writers = survivors.Where(row => row.Rule.Kind == "rewrite").Select(row => row.Rule.Id).Distinct(StringComparer.Ordinal).Count();
        if (writers > 1 && !reasons.Any(reason => reason.Code == "rule_interaction"))
            reasons.Add(new RealizesReason(RealizesReasons.RuleInteraction, $"{writers} rules can change the morpheme, and one may feed or bleed another, so their combined output is not searched."));

        var byReason = survivors.Where(row => row.Reason is not (null or EnvironmentUnchecked)).GroupBy(row => row.Reason!)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Rule.Id).Distinct(StringComparer.Ordinal).Count());
        var unmodelled = surviving.Count(id => survivors.Where(row => row.Rule.Id == id).Any(row => row.Reason is not (null or EnvironmentUnchecked)));
        var summary = new RealizesEffectsSummary(surviving.Count, allRules - surviving.Count, unmodelled, Math.Round(seconds, 2), byReason.Count == 0 ? null : byReason);
        var set = forms.OrderBy(pair => pair.Value.Source == "allomorph" ? 0 : 1).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new RealizesForm(pair.Key, RealizesTiers.Possible, pair.Value.Source, 0, [],
                pair.Value.Rules.Count == 0 ? null : [.. pair.Value.Rules])).ToList();
        return new RealizesVariants(set, anchor, reasons, result.StopReason, Rules: rules, Phon: phon, Effects: summary);
    }

    /// <summary>The form that stands for a morpheme a rule deleted whole.</summary>
    internal const string Unrealised = "∅";

    // The seam for PanGloss output patterns; the literal rebuild is the fallback until replies carry one.
    private static string? OutputPattern(EffectRow row)
    {
        var text = row.Text!;
        if (text.OutputMatchPattern is { } given) return given;
        if (text.OutputUnprintable) return null;
        var rhs = text.Rhs;
        if (rhs is null || rhs.Length < 3 || rhs[0] != '/' || rhs[^1] != '/' || rhs.IndexOf('/', 1) != rhs.Length - 1) return null;
        var at = text.MatchPattern!.IndexOf(" / ", StringComparison.Ordinal);
        return at < 0 ? null : rhs + text.MatchPattern[at..];
    }

    // An insertion rule's pattern has nothing before the slash, and neither phon nor env accepts that.
    private static bool EmptyTarget(string pattern) => pattern.StartsWith("/ ", StringComparison.Ordinal);

    private sealed record Change(int Start, int End, string With, string Rule);

    private static string RuleName(EffectRow row) => string.IsNullOrWhiteSpace(row.Rule.Name) ? row.Rule.Id : row.Rule.Name;

    private static string Written(EffectsAllomorph allomorph) => string.Concat(allomorph.Segments).Normalize(NormalizationForm.FormC);

    // A row computes a literal segment or a deletion; a class, bundle or alpha variable is a feature change.
    private static string? Computed(EffectRow row, EffectsAllomorph allomorph)
    {
        if (row.Rule.Kind != "rewrite" || row.Text is not { Rhs: { } rhs, MatchPattern: not null, BoundaryMarker: null }) return null;
        if (row.LhsSpans.Count == 0 || allomorph.FormPartial) return null;
        if (rhs.Length == 0) return "";
        return rhs.Length > 2 && rhs[0] == '/' && rhs[^1] == '/' && rhs.IndexOf('/', 1) == rhs.Length - 1 ? rhs[1..^1] : null;
    }

    private static (List<(string Text, List<string> Rules)> Variants, bool Capped) Enumerate(IReadOnlyList<string> segments, List<Change> changes)
    {
        var output = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var ordered = changes.OrderBy(change => change.Start).ThenBy(change => change.End).ToList();
        var more = false;
        void Walk(int from, int cursor, StringBuilder text, List<string> used)
        {
            if (output.Count > MaxVariants) { more = true; return; }
            var rest = new StringBuilder(text.ToString());
            for (var i = cursor; i < segments.Count; i++) rest.Append(segments[i]);
            if (used.Count > 0) output.TryAdd(rest.ToString().Normalize(NormalizationForm.FormC), [.. used]);
            for (var index = from; index < ordered.Count; index++)
            {
                var change = ordered[index];
                if (change.Start < cursor || change.End > segments.Count) continue;
                var next = new StringBuilder(text.ToString());
                for (var i = cursor; i < change.Start; i++) next.Append(segments[i]);
                next.Append(change.With);
                Walk(index + 1, change.End, next, [.. used, change.Rule]);
            }
        }
        Walk(0, 0, new StringBuilder(), []);
        if (more)
        {
            output.Clear();
            foreach (var change in ordered)
            {
                var text = string.Concat(segments.Take(change.Start)) + change.With + string.Concat(segments.Skip(change.End));
                output.TryAdd(text.Normalize(NormalizationForm.FormC), [change.Rule]);
            }
        }
        return ([.. output.Select(pair => (pair.Key, pair.Value))], more);
    }

    // A boundary marker on the form's left means the affix follows a stem; on its right, it precedes one.
    private static string AnchorOf(IReadOnlyList<EffectsAllomorph> allomorphs)
    {
        var anchors = allomorphs.Select(item =>
        {
            var bare = string.Concat(item.Segments);
            if (item.Form == bare || item.Form.Length == 0) return "none";
            var onLeft = !item.Form.StartsWith(bare, StringComparison.Ordinal);
            var onRight = !item.Form.EndsWith(bare, StringComparison.Ordinal);
            return onLeft && onRight ? "none" : onLeft ? "end" : onRight ? "start" : "none";
        }).Distinct().ToList();
        return anchors.Count == 1 ? anchors[0] : "none";
    }

    // The written form with the rule's target widened to any character, so a pattern counts where it sits.
    private static string Context(EffectsAllomorph allomorph, EffectRow row, string anchor)
    {
        var widened = row.LhsSpans.Where(span => span.End <= allomorph.Segments.Count).ToList();
        var body = new StringBuilder();
        for (var i = 0; i < allomorph.Segments.Count; i++)
        {
            var inside = widened.Any(span => i >= span.Start && i < span.End);
            body.Append(inside ? "." : Regex.Escape(allomorph.Segments[i]));
        }
        if (widened.Count == 0) body.Insert(0, row.Edge == "left" ? ".?" : "").Append(row.Edge == "right" ? ".?" : "");
        return (anchor == "start" ? @"\A" : "") + body + (anchor == "end" ? @"\z" : "");
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
