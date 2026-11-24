using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel;
using SIL.Motif.Contract.Query;
using SIL.Motif.Projection;

namespace SIL.Motif.Commands.Queries;

/// <summary>What one tier knows of a morpheme's surface forms, before it is compiled to a search.</summary>
/// <param name="Forms">The forms, with the evidence for each.</param>
/// <param name="Anchor">Where in a word the morpheme sits: <c>start</c>, <c>end</c> or <c>none</c>.</param>
/// <param name="Reasons">Why the set is not exhaustive; empty when it is.</param>
/// <param name="StopReason">Why an enumerating tier stopped early; null otherwise.</param>
/// <param name="Terms">Search terms that each carry their own anchor and attested context, as <see cref="SIL.Motif.Contract.Query.RealizesPatternCompiler"/> marks them; null when the forms and one anchor say it all.</param>
/// <param name="ProjectHasAnalyses">Whether the project holds any stored analysis, so a refusal can say which of the two is missing.</param>
internal sealed record RealizesVariants(IReadOnlyList<RealizesForm> Forms, string Anchor, IReadOnlyList<RealizesReason> Reasons,
    string? StopReason = null, bool ProjectHasAnalyses = true, IReadOnlyList<string>? Terms = null)
{
    internal bool Complete => Reasons.Count == 0 && StopReason is null;
}

/// <summary>
/// The <c>attested</c> tier: the surface string of the morpheme's morph in every stored analysis that uses it and no person has
/// disapproved. It reads facts only, calls no parser, and is exact for the analyses as they are now.
/// </summary>
internal static class RealizesAttested
{
    private const int ExamplesPerForm = 3;

    public static RealizesVariants Read(LcmCache cache, ResolvedMorpheme morpheme)
    {
        var repository = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
        var entry = repository.GetObject(Guid.Parse(morpheme.EntryGuid));
        var msaGuid = Guid.Parse(morpheme.MsaGuid);
        var lexeme = entry.LexemeFormOA;
        var anyAnalysis = false;
        var unwritten = 0;
        var byForm = new Dictionary<(string Form, string Source), SortedSet<string>>();
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            var disapproved = wordform.HumanDisapprovedParses.ToHashSet();
            var word = WritingSystemTextReader.First(cache, wordform.Form).Text.Normalize(NormalizationForm.FormC);
            foreach (var analysis in wordform.AnalysesOC.Where(analysis => !disapproved.Contains(analysis)))
            {
                anyAnalysis = true;
                foreach (var bundle in analysis.MorphBundlesOS)
                {
                    if (bundle.MorphRA?.OwnerOfClass<ILexEntry>() is not { } owner || owner.Guid != entry.Guid) continue;
                    if (bundle.MsaRA is { } msa && msa.Guid != msaGuid) continue;
                    var form = Bare(bundle.Form is { } text ? Vernacular(cache, text) : "");
                    if (form.Length == 0) { unwritten++; continue; }
                    var source = ReferenceEquals(bundle.MorphRA, lexeme) ? "lexeme-form" : "alternate-form";
                    if (!byForm.TryGetValue((form, source), out var words)) byForm[(form, source)] = words = new SortedSet<string>(StringComparer.Ordinal);
                    if (word.Length > 0) words.Add(word);
                }
            }
        }
        var forms = byForm.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key.Form, StringComparer.Ordinal)
            .Select(pair => new RealizesForm(pair.Key.Form, RealizesTiers.Attested, pair.Key.Source, pair.Value.Count,
                [.. pair.Value.Take(ExamplesPerForm)])).ToList();
        List<RealizesReason> reasons = [];
        if (unwritten > 0)
            reasons.Add(new RealizesReason("unwritten_variant",
                $"{unwritten} analyses use the morpheme with no written form, so a word carrying it cannot be found by its text."));
        return new RealizesVariants(forms, AnchorOf(lexeme?.MorphTypeRA?.Guid), reasons, ProjectHasAnalyses: anyAnalysis);
    }

    // A prefix or proclitic starts a word, a suffix or enclitic ends it, and any other type can sit anywhere.
    private static string AnchorOf(Guid? morphType)
    {
        if (morphType is not { } type) return "none";
        if (type == MoMorphTypeTags.kguidMorphPrefix || type == MoMorphTypeTags.kguidMorphPrefixingInterfix ||
            type == MoMorphTypeTags.kguidMorphProclitic) return "start";
        if (type == MoMorphTypeTags.kguidMorphSuffix || type == MoMorphTypeTags.kguidMorphSuffixingInterfix ||
            type == MoMorphTypeTags.kguidMorphEnclitic) return "end";
        return "none";
    }

    private static string Vernacular(LcmCache cache, IMultiAccessorBase text)
    {
        var chosen = text.get_String(cache.DefaultVernWs)?.Text;
        return chosen is { Length: > 0 } && chosen != "***" ? chosen : WritingSystemTextReader.First(cache, text).Text;
    }

    // An analysis shows an affix with its hyphen and a clitic with its equals sign; the surface string has neither.
    private static string Bare(string form) => form.Normalize(NormalizationForm.FormC).Trim().Trim('-', '=').Trim();
}
