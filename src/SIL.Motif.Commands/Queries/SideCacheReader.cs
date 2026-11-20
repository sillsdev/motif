using System.Text;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Query;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads the forms, graphemes and word parses a query side shows from one project copy, and never writes.</summary>
internal static class SideCacheReader
{
    /// <summary>The forms of the lexicon with each one's word parse, and the project's graphemes.</summary>
    public sealed record Side(IReadOnlyList<QuerySideRow> Rows, IReadOnlyList<string> Graphemes);

    public static Side Read(LcmCache cache)
    {
        var words = ReadWords(cache);
        var graphemes = ReadGraphemes(cache);
        var rows = new List<QuerySideRow>();
        foreach (var form in ReadForms(cache))
        {
            var parse = words.TryGetValue(form.Form.Normalize(NormalizationForm.FormC), out var found)
                ? found : new QueryWordParse(QuerySideNames.Absent, 0, []);
            rows.Add(new QuerySideRow(form with { Tokens = parse.Tokens > 0 ? parse.Tokens : null }, parse));
        }
        return new Side(rows, graphemes);
    }

    private static string Vernacular(LcmCache cache, IMultiAccessorBase text)
    {
        var chosen = text.get_String(cache.DefaultVernWs)?.Text;
        return chosen is { Length: > 0 } ? chosen : WritingSystemTextReader.First(cache, text).Text;
    }

    private static string Name(LcmCache cache, ICmPossibility? item) =>
        item is null ? "" : WritingSystemTextReader.BestAnalysis(cache, item.Name).Text;

    private static string Abbreviation(LcmCache cache, ICmPossibility? item) =>
        item is null ? "" : WritingSystemTextReader.BestAnalysis(cache, item.Abbreviation).Text is { Length: > 0 } abbreviation
            ? abbreviation : Name(cache, item);

    private static IEnumerable<IPartOfSpeech?> Categories(IMoMorphSynAnalysis msa) => msa switch
    {
        IMoStemMsa stem => [stem.PartOfSpeechRA],
        IMoInflAffMsa inflectional => [inflectional.PartOfSpeechRA],
        IMoDerivAffMsa derivational => [derivational.FromPartOfSpeechRA],
        IMoDerivStepMsa step => [step.PartOfSpeechRA],
        IMoUnclassifiedAffixMsa unclassified => [unclassified.PartOfSpeechRA],
        _ => [],
    };

    private static List<QueryForm> ReadForms(LcmCache cache)
    {
        var rows = new List<QueryForm>();
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .OrderBy(entry => entry.Guid).ToList();
        string LexemeForm(ILexEntry entry) => entry.LexemeFormOA is { } lexeme ? Vernacular(cache, lexeme.Form) : "";
        IReadOnlyList<string> Environments(IMoForm form) => form is IMoStemAllomorph stem
            ? [.. stem.PhoneEnvRC.Select(env => env.StringRepresentation?.Text ?? "").Where(text => text.Length > 0)]
            : form is IMoAffixAllomorph affix
                ? [.. affix.PhoneEnvRC.Select(env => env.StringRepresentation?.Text ?? "").Where(text => text.Length > 0)]
                : [];
        IReadOnlyList<string> CategoriesOf(ILexEntry entry) =>
            [.. entry.MorphoSyntaxAnalysesOC.SelectMany(Categories).OfType<IPartOfSpeech>()
                .Select(category => Abbreviation(cache, category)).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal)];
        var variantEntries = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            var references = entry.EntryRefsOS.Where(reference => reference.RefType == LexEntryRefTags.krtVariant).ToList();
            var categories = CategoriesOf(entry);
            if (references.Count > 0 && entry.LexemeFormOA is { } variant)
            {
                variantEntries.Add(entry.Guid);
                var form = Vernacular(cache, variant.Form);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var reference in references)
                    foreach (var component in reference.ComponentLexemesRS)
                    {
                        var main = component as ILexEntry ?? component.OwnerOfClass<ILexEntry>();
                        var basis = LexemeForm(main);
                        if (form.Length == 0 || basis.Length == 0 || !seen.Add(basis)) continue;
                        rows.Add(new QueryForm(CanonicalId.FromGuid(variant.Guid).Value + "@" + CanonicalId.FromGuid(main.Guid).Value, form, basis, "variant",
                            CanonicalId.FromGuid(entry.Guid).Value, MorphType(cache, variant), Name(cache, reference.VariantEntryTypesRS.FirstOrDefault()),
                            Environments(variant), categories));
                    }
            }
        }
        foreach (var entry in entries)
        {
            if (entry.LexemeFormOA is not { } lexeme) continue;
            var basis = Vernacular(cache, lexeme.Form);
            if (basis.Length == 0) continue;
            var entryId = CanonicalId.FromGuid(entry.Guid).Value;
            var categories = CategoriesOf(entry);
            if (!variantEntries.Contains(entry.Guid))
                rows.Add(new QueryForm(CanonicalId.FromGuid(lexeme.Guid).Value, basis, basis, "lexeme", entryId,
                    MorphType(cache, lexeme), "", Environments(lexeme), categories));
            foreach (var alternate in entry.AlternateFormsOS)
            {
                var form = Vernacular(cache, alternate.Form);
                if (form.Length == 0) continue;
                rows.Add(new QueryForm(CanonicalId.FromGuid(alternate.Guid).Value, form, basis, "alternate", entryId,
                    MorphType(cache, alternate), "", Environments(alternate), categories));
            }
        }
        return rows;
    }

    private static string MorphType(LcmCache cache, IMoForm form) =>
        Name(cache, form.MorphTypeRA).ToLowerInvariant().Replace(' ', '-');

    private static List<string> ReadGraphemes(LcmCache cache)
    {
        var graphemes = new List<string>();
        var ws = cache.DefaultVernWs;
        foreach (var set in cache.LangProject.PhonologicalDataOA?.PhonemeSetsOS ?? Enumerable.Empty<IPhPhonemeSet>())
            foreach (var phoneme in set.PhonemesOC)
                foreach (var code in phoneme.CodesOS)
                    if (code.Representation.get_String(ws)?.Text is { Length: > 0 } text && !graphemes.Contains(text))
                        graphemes.Add(text);
        return graphemes;
    }

    private static Dictionary<string, QueryWordParse> ReadWords(LcmCache cache)
    {
        var tokens = new Dictionary<Guid, int>();
        foreach (var text in cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances())
            foreach (var paragraph in text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? Enumerable.Empty<IStTxtPara>())
                foreach (var occurrence in SegmentServices.GetAnalysisOccurrences(paragraph))
                {
                    var wordform = occurrence.Analysis switch
                    {
                        IWfiGloss gloss => (IWfiWordform)gloss.Owner.Owner,
                        IWfiAnalysis analysis => (IWfiWordform)analysis.Owner,
                        IWfiWordform bare => bare,
                        _ => null,
                    };
                    if (wordform is not null) tokens[wordform.Guid] = tokens.GetValueOrDefault(wordform.Guid) + 1;
                }
        var parses = new Dictionary<string, QueryWordParse>(StringComparer.Ordinal);
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().OrderBy(word => word.Guid))
        {
            var form = WritingSystemTextReader.First(cache, wordform.Form).Text.Normalize(NormalizationForm.FormC);
            if (form.Length == 0) continue;
            var approved = wordform.HumanApprovedAnalyses.ToHashSet();
            var disapproved = wordform.HumanDisapprovedParses.ToHashSet();
            var analyses = wordform.AnalysesOC.ToList();
            var state = approved.Count > 0 ? QuerySideNames.Approved
                : analyses.Count == 0 ? QuerySideNames.Unparsed
                : analyses.All(disapproved.Contains) ? QuerySideNames.Failed : QuerySideNames.Parsed;
            var readings = analyses.OrderBy(analysis => analysis.Guid).Select(analysis => new QueryReading(
                Abbreviation(cache, analysis.CategoryRA), approved.Contains(analysis),
                [.. analysis.MorphBundlesOS.Select(bundle => new QueryMorph(
                    bundle.Form is { } bundleForm ? Vernacular(cache, bundleForm) : "",
                    bundle.SenseRA is { } sense ? WritingSystemTextReader.BestAnalysis(cache, sense.Gloss).Text : "",
                    bundle.MsaRA is { } msa ? string.Join(",", Categories(msa).OfType<IPartOfSpeech>().Select(c => Abbreviation(cache, c))) : "",
                    bundle.MorphRA?.OwnerOfClass<ILexEntry>() is { } entry ? CanonicalId.FromGuid(entry.Guid).Value : ""))])).ToList();
            var parse = new QueryWordParse(state, tokens.GetValueOrDefault(wordform.Guid), readings);
            if (!parses.TryGetValue(form, out var existing) || Rank(parse.State) > Rank(existing.State)) parses[form] = parse;
        }
        return parses;
    }

    private static int Rank(string state) => state switch
    {
        QuerySideNames.Approved => 3, QuerySideNames.Parsed => 2, QuerySideNames.Failed => 1, _ => 0,
    };
}
