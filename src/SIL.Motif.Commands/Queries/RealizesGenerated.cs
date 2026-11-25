using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>One surface PanGloss generated, with the morpheme's own piece of it and the rules that fired.</summary>
/// <param name="Surface">The whole word.</param>
/// <param name="Piece">The morpheme's piece of the word, or the whole word when the engine could not place it.</param>
/// <param name="PieceSource"><c>morphSpans</c>, or <c>whole-word:</c> and the reason the engine gave.</param>
/// <param name="Rules">The names of the phonological rules whose steps changed the word.</param>
/// <param name="Trace">Every step, <c>rule: input -> output</c>.</param>
internal sealed record GeneratedPiece(string Surface, string Piece, string PieceSource, IReadOnlyList<string> Rules, IReadOnlyList<string> Trace);

/// <summary>What one generate request gave for a morpheme.</summary>
/// <param name="Pieces">One per surface and distinct span answer.</param>
/// <param name="Complete">Whether PanGloss finished the request.</param>
/// <param name="StopReason">Why it stopped, or null.</param>
/// <param name="Empty">Surfaces the morpheme has no segment in: a null affix, or one a rule deleted.</param>
internal sealed record GeneratedRead(IReadOnlyList<GeneratedPiece> Pieces, bool Complete, string? StopReason, int Empty);

/// <summary>
/// The <c>generated</c> tier: the forms PanGloss produces for a morpheme on real neighbours the planner chose for each rule
/// environment. The morpheme's piece of a surface comes from <c>morphSpans</c>, the engine's own morph ranges; where the engine
/// says it cannot place the morpheme the whole word stands in and is labelled so, never a guessed piece.
/// </summary>
internal static class RealizesGenerated
{
    internal const string Whole = "whole-word";

    /// <summary>Reads the <c>generate</c> result for the morpheme at <paramref name="entryGuid"/> and <paramref name="msaGuid"/>.</summary>
    internal static GeneratedRead Read(string json, string entryGuid, string msaGuid)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var pieces = new List<GeneratedPiece>();
        var empty = 0;
        foreach (var seed in root.GetProperty("seeds").EnumerateArray())
        {
            var index = -1;
            var position = 0;
            foreach (var allomorph in seed.GetProperty("allomorphs").EnumerateArray())
            {
                if (string.Equals(Str(allomorph, "entryGuid"), entryGuid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Str(allomorph, "msaGuid"), msaGuid, StringComparison.OrdinalIgnoreCase)) { index = position; break; }
                position++;
            }
            if (index < 0) continue;
            foreach (var surface in seed.GetProperty("surfaces").EnumerateArray())
            {
                var word = (Str(surface, "surface") ?? "").Normalize(NormalizationForm.FormC);
                var steps = surface.TryGetProperty("steps", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
                var rules = steps.Where(step => step.GetProperty("rule").TryGetProperty("kind", out var kind) && kind.GetString() == "phon")
                    .Select(step => Str(step.GetProperty("rule"), "name") ?? Str(step.GetProperty("rule"), "id") ?? "?").Distinct(StringComparer.Ordinal).ToList();
                var trace = steps.Select(step => $"{Str(step.GetProperty("rule"), "name") ?? Str(step.GetProperty("rule"), "id")}: {Str(step, "input")} -> {Str(step, "output")}").ToList();
                var answers = surface.TryGetProperty("morphSpans", out var spans) && spans.ValueKind == JsonValueKind.Array ? spans.EnumerateArray().ToList() : [];
                if (answers.Count == 0) { pieces.Add(new GeneratedPiece(word, word, Whole + ":no_morph_spans", rules, trace)); continue; }
                var normalized = Str(surface, "normalized");
                foreach (var answer in answers)
                {
                    var entry = answer.GetProperty("morphemes").EnumerateArray().FirstOrDefault(item => item.GetProperty("item").GetInt32() == index);
                    if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("spans", out var runs) || runs.ValueKind != JsonValueKind.Array)
                    {
                        var reason = entry.ValueKind == JsonValueKind.Object ? Str(entry, "reason") : null;
                        pieces.Add(new GeneratedPiece(word, word, Whole + ":" + (reason ?? "no_span"), rules, trace));
                        continue;
                    }
                    if (runs.GetArrayLength() == 0) { empty++; continue; }
                    if (runs.GetArrayLength() > 1 || !runs[0].TryGetProperty("offsets", out var offsets) || offsets.ValueKind != JsonValueKind.Array)
                    {
                        pieces.Add(new GeneratedPiece(word, word, Whole + ":" + (runs.GetArrayLength() > 1 ? "split_span" : "offsets_unavailable"), rules, trace));
                        continue;
                    }
                    var bytes = Encoding.UTF8.GetBytes(normalized ?? word.Normalize(NormalizationForm.FormD));
                    var (from, to) = (offsets[0].GetInt32(), offsets[1].GetInt32());
                    pieces.Add(from >= 0 && to <= bytes.Length && from < to
                        ? new GeneratedPiece(word, Encoding.UTF8.GetString(bytes, from, to - from).Normalize(NormalizationForm.FormC), "morphSpans", rules, trace)
                        : new GeneratedPiece(word, word, Whole + ":offsets_out_of_range", rules, trace));
                }
            }
        }
        var stop = Str(root, "stopReason");
        return new GeneratedRead(pieces, root.GetProperty("complete").GetBoolean(), stop, empty);
    }

    /// <summary>Plans test neighbours, generates the morpheme on each and reads its piece; the answer is the <c>generated</c> variant set.</summary>
    public static RealizesVariants Collect(IRealizesGrammar grammar, RealizesTarget target, IReadOnlyList<PlannerCandidate> lexicon,
        IReadOnlyList<PlannerCandidate> attestedNeighbours, bool thorough, CancellationToken cancellationToken, string? processes = null)
    {
        if (target.EntryId is null || target.MsaId is null) throw RealizesRefusals.NeedsIdentity(target.AnyId ?? "the morpheme");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var effects = grammar.Effects(new ServeEffectsRequest(new EffectsMorpheme(target.EntryId, target.MsaId)), cancellationToken);
        var possible = RealizesPossible.Reduce(effects, clock.Elapsed.TotalSeconds, processes);
        var msaClass = effects.MsaClass ?? lexicon.FirstOrDefault(item => string.Equals(item.MsaGuid, target.MsaId, StringComparison.OrdinalIgnoreCase))?.MsaClass
            ?? (possible.Anchor == "none" ? ResolvedMorpheme.StemClass : "MoInflAffMsa");
        var isStem = msaClass == ResolvedMorpheme.StemClass;
        var self = new GenerateMorpheme(target.EntryId, target.MsaId, msaClass);
        var survivors = effects.Survivors.ToList();
        var plan = RealizesPlanner.Plan(grammar, survivors, effects.Allomorphs, isStem, possible.Anchor, lexicon, thorough ? 3 : 2, RealizesPlanner.MaxContexts, cancellationToken);
        var contexts = plan.Contexts.ToList();
        foreach (var neighbour in attestedNeighbours.Where(item => item.EntryGuid != target.EntryId).Take(3))
            if (isStem ? neighbour.Kind is "prefix" or "suffix" : neighbour.Kind == "stem")
                contexts.Add(new PlannedContext(neighbour, neighbour.Kind == "prefix" ? "left" : "right", [], "attested-neighbour"));
        var reasons = possible.Reasons.ToList();
        if (plan.Uncovered.Count > 0)
            reasons.Add(new RealizesReason(RealizesReasons.Uncovered, string.Format(CultureInfo.InvariantCulture,
                "No real {0} in the lexicon has an edge that triggers: {1}.", isStem ? "affix" : "root",
                string.Join(", ", plan.Uncovered.Select(cell => cell.Key).Take(8)))));
        if (lexicon.Count == 0)
            reasons.Add(new RealizesReason(RealizesReasons.NoLexicon, "The grammar file is not a FieldWorks project, so there are no real roots or affixes to test the rules on."));
        if (plan.Capped.Count > 0)
            reasons.Add(new RealizesReason(RealizesReasons.ContextCap, Format($"The planner stops at {RealizesPlanner.MaxContexts} contexts, so these environments were not generated: {string.Join(", ", plan.Capped.Select(cell => cell.Key).Take(8))}.")));
        if (plan.Ignored.Count > 0)
            reasons.Add(new RealizesReason(RealizesReasons.EdgeNotTested, Format($"Rules at the other edge of the morpheme are not tested, because a root is only joined at one side: {string.Join(", ", plan.Ignored.Select(cell => cell.Key).Take(8))}.")));
        if (!isStem)
            reasons.Add(new RealizesReason(RealizesReasons.TemplateNotWalked, "The morpheme is generated only next to one root; rules that depend on other affixes in the word template are not tested."));
        var forms = new Dictionary<string, GeneratedForm>(StringComparer.Ordinal);
        var stop = possible.StopReason;
        var unrealised = 0;
        var failed = 0;
        foreach (var context in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.Neighbour is null && !isStem) { failed++; continue; }
            var request = isStem
                ? new ServeGenerateRequest(self, context.Neighbour is null ? [] : [new GenerateMorpheme(context.Neighbour.EntryGuid, context.Neighbour.MsaGuid, context.Neighbour.MsaClass)])
                : new ServeGenerateRequest(new GenerateMorpheme(context.Neighbour!.EntryGuid, context.Neighbour.MsaGuid, context.Neighbour.MsaClass), [self]);
            var json = grammar.Generate(request, cancellationToken);
            if (json is null) { failed++; continue; }
            var read = Read(json, target.EntryId, target.MsaId);
            if (!read.Complete) { stop ??= read.StopReason ?? "generate_stopped"; reasons.Add(new RealizesReason("generate_stop:" + (read.StopReason ?? "unreported"), $"PanGloss stopped generating on {Label(context)}.")); }
            unrealised += read.Empty;
            if (read.Pieces.Count == 0 && read.Empty == 0) failed++;
            foreach (var piece in read.Pieces)
            {
                var key = piece.Piece.Normalize(NormalizationForm.FormC);
                if (!forms.TryGetValue(key, out var form)) forms[key] = form = new GeneratedForm(piece);
                form.Add(piece, context, Label(context));
            }
        }
        if (failed > 0)
            reasons.Add(new RealizesReason(RealizesReasons.NoGeneratedSurface, $"{failed} planned contexts gave no surface; PanGloss refused them or produced no analysis of the morpheme."));
        if (unrealised > 0)
        {
            reasons.Add(new RealizesReason(RealizesReasons.MayBeUnrealised, $"On {unrealised} generated surfaces the morpheme has no segment, because a rule deleted it or it is a null; the surface is listed as the empty variant."));
            forms.TryAdd(RealizesPossible.Unrealised, new GeneratedForm(new GeneratedPiece("", RealizesPossible.Unrealised, "morphSpans", [], [])));
        }
        var seen = forms.Keys.Select(Bare).ToHashSet(StringComparer.Ordinal);
        var missing = effects.Allomorphs.Select(item => Bare(string.Concat(item.Segments))).Where(text => text.Length > 0 && !seen.Contains(text)).Distinct().ToList();
        if (missing.Count > 0)
            reasons.Add(new RealizesReason(RealizesReasons.AllomorphNotGenerated, $"No planned context produced these allomorphs of the morpheme: {string.Join(", ", missing)}; one conditioned by an environment no chosen root satisfies is missing."));
        if (forms.Values.Any(form => form.WholeWord))
            reasons.Add(new RealizesReason(RealizesReasons.PieceUnknown, "For some surfaces PanGloss could not place the morpheme; the whole word stands in, labelled whole-word."));
        var set = forms.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value.ToForm(pair.Key)).ToList();
        var rules = possible.Rules;
        return new RealizesVariants(set, possible.Anchor, reasons.GroupBy(reason => reason.Code).Select(group => group.First()).ToList(),
            stop, Rules: rules, Effects: possible.Effects);
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Bare(string form) => form.Trim('-', '+', '=', '~').Normalize(NormalizationForm.FormC);

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Label(PlannedContext context) =>
        (context.Neighbour is { } neighbour ? neighbour.Form + " (" + neighbour.Id + ")" : "none") + " [" + context.Label + "]";

    private sealed class GeneratedForm(GeneratedPiece first)
    {
        private readonly List<string> _roots = [];
        private readonly SortedSet<string> _rules = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _cells = new(StringComparer.Ordinal);

        public bool WholeWord { get; } = first.PieceSource.StartsWith(Whole, StringComparison.Ordinal);

        public void Add(GeneratedPiece piece, PlannedContext context, string label)
        {
            if (!_roots.Contains(label)) _roots.Add(label);
            foreach (var rule in piece.Rules) _rules.Add(rule);
            _cells.Add(context.Label);
        }

        public RealizesForm ToForm(string text) => new(text, RealizesTiers.Generated, "generate", 0, [],
            _rules.Count == 0 ? null : [.. _rules], string.Join("; ", _roots.Take(4)), first.Surface, first.PieceSource, [.. first.Trace], string.Join(";", _cells));
    }
}
