using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>One variant entry whose copied sense is to go, and optionally the main sense it should vary.</summary>
/// <param name="Entry">The variant entry that carries a copied sense and analysis.</param>
/// <param name="Component">
/// The main entry's sense the variant should point at. Needed only when the variant now points at the main entry and
/// that entry has more than one sense; a variant already pointing at a sense keeps it.
/// </param>
public sealed record StripVariantSensesItem(CanonicalId Entry, CanonicalId? Component = null);

/// <summary>The variant entries to turn into sense-less variants.</summary>
/// <param name="Entries">Each entry to strip, with its main sense when it must be named.</param>
public sealed record StripVariantSensesIntent(IReadOnlyList<StripVariantSensesItem> Entries);

/// <summary>Reads a closed strip-variant-senses intent.</summary>
public static class StripVariantSensesIntentParser
{
    /// <summary>Reads the intent, refusing any property it does not name.</summary>
    public static StripVariantSensesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "StripVariantSenses", "entries");
        var entries = SoundSystemIntentParsing.Array(authored, "entries");
        if (entries.Length == 0) throw new Contract.Parsing.ContractParseException("'entries' needs at least one entry.");
        return new(entries.Select(item =>
        {
            SoundSystemIntentParsing.Object(item, "StripVariantSenses entry", "entry", "component");
            return new StripVariantSensesItem(SoundSystemIntentParsing.Id(item, "entry"),
                item.TryGetProperty("component", out _) ? SoundSystemIntentParsing.Id(item, "component") : null);
        }).ToArray());
    }
}

/// <summary>
/// Turns variant entries that copy their main entry's sense into sense-less variants of that sense: it repoints the
/// variant reference at the main sense when it names the main entry, then deletes the copied senses and their analyses.
/// The variant keeps its lexeme form, allomorphs and environments. A sense-less variant of a sense is what LibLCM's
/// CreateVariantEntryAndBackRef makes, and the parser reads it as that sense's stem (ADR 0112). An entry is refused when
/// anything refers to its copied sense or analysis, or when the main sense has no stem analysis.
/// </summary>
public static class StripVariantSensesComposer
{
    private const string ConstructName = "StripVariantSenses";
    private const string Rationale = "Authored by the StripVariantSenses composer: the variant uses the main sense, not a copy.";

    /// <summary>Composes the changes without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, StripVariantSensesIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var operations = new List<OperationEnvelope>();
        var seen = new HashSet<Guid>();
        foreach (var item in intent.Entries)
        {
            var plan = Plan(cache, item);
            if (!seen.Add(plan.Entry.Guid))
                throw new InvalidOperationException($"'{ConstructName}': {SenseRemoval.Describe(plan.Entry)} is listed twice.");
            OperationDependency[] previous = [];
            if (plan.Reference is { } reference && plan.Main.Guid != reference.ComponentLexemesRS.Single().Guid)
            {
                var repoint = new OperationEnvelope(mint(), VariantLinkOperationKinds.Set,
                    target: CanonicalId.FromGuid(reference.Guid), rationale: Rationale,
                    after: JsonSerializer.SerializeToElement(new
                    {
                        component = CanonicalId.FromGuid(plan.Main.Guid).Value,
                        variantType = CanonicalId.FromGuid(reference.VariantEntryTypesRS.Single().Guid).Value,
                        expectedComponents = new[] { CanonicalId.FromGuid(reference.ComponentLexemesRS.Single().Guid).Value },
                        expectedTypes = new[] { CanonicalId.FromGuid(reference.VariantEntryTypesRS.Single().Guid).Value },
                    }));
                operations.Add(repoint);
                previous = [new OperationDependency(repoint.OperationId)];
            }
            var removed = new HashSet<Guid>();
            foreach (var sense in plan.Entry.SensesOS.ToArray())
            {
                var removal = SenseRemoval.Inspect(sense, removed, trustLastSense: true);
                var op = new OperationEnvelope(mint(), LexSenseRemoveOperationKinds.Remove,
                    target: CanonicalId.FromGuid(sense.Guid), rationale: Rationale, dependsOn: previous,
                    after: JsonSerializer.SerializeToElement(new
                    {
                        owner = CanonicalId.FromGuid(removal.Owner.Guid).Value,
                        topLevel = true,
                        entry = CanonicalId.FromGuid(removal.Entry.Guid).Value,
                        msas = removal.OrphanedAnalyses.Select(msa => CanonicalId.FromGuid(msa.Guid).Value)
                            .Order(StringComparer.Ordinal).ToArray(),
                    }));
                operations.Add(op);
                previous = [new OperationDependency(op.OperationId)];
                foreach (var each in removal.Subtree) removed.Add(each.Guid);
                foreach (var example in removal.Subtree.SelectMany(each => each.ExamplesOS)) removed.Add(example.Guid);
            }
        }
        return operations;
    }

    /// <summary>
    /// Reports, per entry, the exception features the copied analysis carries that the main sense's lacks (lost) and the
    /// other way round (gained), because the variant takes the main sense's analysis when it is stripped.
    /// </summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadDeltas(LcmCache cache, StripVariantSensesIntent intent)
    {
        var report = new List<ComposedRelatedObject>();
        foreach (var item in intent.Entries)
        {
            var plan = Plan(cache, item);
            var own = plan.Entry.SensesOS.SelectMany(sense => sense.AllSenses).Select(sense => sense.MorphoSyntaxAnalysisRA)
                .OfType<IMoStemMsa>().SelectMany(msa => msa.ProdRestrictRC).DistinctBy(feature => feature.Guid).ToArray();
            var main = ((IMoStemMsa)plan.Main.MorphoSyntaxAnalysisRA!).ProdRestrictRC.ToArray();
            var gained = main.Where(feature => own.All(other => other.Guid != feature.Guid)).Select(Name).Order().ToArray();
            var lost = own.Where(feature => main.All(other => other.Guid != feature.Guid)).Select(Name).Order().ToArray();
            var id = CanonicalId.FromGuid(plan.Entry.Guid).Value;
            var name = SenseRemoval.Describe(plan.Entry);
            report.Add(new ComposedRelatedObject("exception-feature-delta", id,
                $"{name} takes the exception features of {SenseRemoval.Describe(plan.Main)}: gains " +
                $"[{string.Join(", ", gained)}], loses [{string.Join(", ", lost)}]."));
            var gloss = plan.Entry.SensesOS.Select(SenseRemoval.Gloss).Where(text => text.Length > 0).Order().ToArray();
            if (gloss.Length != 0 && !gloss.Contains(SenseRemoval.Gloss(plan.Main), StringComparer.Ordinal))
                report.Add(new ComposedRelatedObject("variant-gloss-differs", id,
                    $"{name} copies the gloss [{string.Join(", ", gloss)}], which is not the main sense's " +
                    $"'{SenseRemoval.Gloss(plan.Main)}'; the copy is deleted."));
        }
        return report;
    }

    private static string Name(ICmPossibility feature) =>
        feature.Name.BestAnalysisAlternative.Text is { Length: > 0 } text && text != "***"
            ? text : CanonicalId.FromGuid(feature.Guid).Value;

    private sealed record StripPlan(ILexEntry Entry, ILexEntryRef? Reference, ILexSense Main);

    private static StripPlan Plan(LcmCache cache, StripVariantSensesItem item)
    {
        var entry = LexicalFormAuthoring.Resolve<ICmObject>(cache, item.Entry, ConstructName) as ILexEntry
            ?? throw new InvalidOperationException($"'{ConstructName}': '{item.Entry.Value}' is not an entry.");
        var label = SenseRemoval.Describe(entry);
        if (entry.SensesOS.Count == 0)
            throw new InvalidOperationException($"'{ConstructName}': {label} has no sense to strip.");
        var references = entry.EntryRefsOS.Where(reference => reference.RefType == LexEntryRefTags.krtVariant).ToArray();
        if (references.Length != 1 || references[0].ComponentLexemesRS.Count != 1 ||
            references[0].VariantEntryTypesRS.Count != 1)
            throw new InvalidOperationException(
                $"'{ConstructName}': {label} must be a variant through exactly one variant reference with one component " +
                "and one variant type.");
        var reference = references[0];
        var current = reference.ComponentLexemesRS.Single();
        ILexSense main;
        if (item.Component is { } named)
            main = LexicalFormAuthoring.Resolve<ICmObject>(cache, named, ConstructName) as ILexSense
                ?? throw new InvalidOperationException($"'{ConstructName}': 'component' must name a sense.");
        else if (current is ILexSense sense)
            main = sense;
        else if (current is ILexEntry mainEntry && mainEntry.SensesOS.Count == 1)
            main = mainEntry.SensesOS[0];
        else
            throw new InvalidOperationException(
                $"'{ConstructName}': {label} is a variant of an entry with several senses; name the main sense in 'component'.");
        var mainEntryOfSense = SenseRemoval.EntryOf(main);
        var currentEntry = current as ILexEntry ?? SenseRemoval.EntryOf((ILexSense)current);
        if (mainEntryOfSense is null || mainEntryOfSense.Guid != currentEntry?.Guid)
            throw new InvalidOperationException(
                $"'{ConstructName}': the main sense must belong to the entry {label} is a variant of, " +
                $"{SenseRemoval.Describe(currentEntry!)}.");
        if (mainEntryOfSense.Guid == entry.Guid)
            throw new InvalidOperationException($"'{ConstructName}': {label} cannot take its own sense as its main sense.");
        if (main.MorphoSyntaxAnalysisRA is not IMoStemMsa)
            throw new InvalidOperationException(
                $"'{ConstructName}': {SenseRemoval.Describe(main)} has no stem analysis, so the parser would load nothing " +
                "for a sense-less variant of it.");
        return new StripPlan(entry, reference, main);
    }
}
