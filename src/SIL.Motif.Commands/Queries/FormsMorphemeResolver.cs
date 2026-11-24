using System.Globalization;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>A morpheme named by its FieldWorks identity, with the form the project shows for it.</summary>
/// <param name="EntryGuid">The lexical entry's GUID, lowercase.</param>
/// <param name="MsaGuid">The morphosyntactic analysis's GUID, lowercase.</param>
/// <param name="MsaClass">The LibLCM class of the analysis.</param>
/// <param name="Form">The entry's headword, or null when it has none.</param>
internal sealed record ResolvedMorpheme(string EntryGuid, string MsaGuid, string MsaClass, string? Form)
{
    internal const string StemClass = "MoStemMsa";

    internal GenerateMorpheme ToRequest() => new(EntryGuid, MsaGuid, MsaClass);

    internal FormsExplainMorpheme ToResponse() => new(EntryGuid, MsaGuid, MsaClass, Form);

    internal string Id => EntryGuid + ":" + MsaGuid;
}

/// <summary>Whether a morpheme was found, and the refusal that says why not.</summary>
internal readonly record struct MorphemeLookup(ResolvedMorpheme? Morpheme, Refusal? Refusal);

/// <summary>
/// Turns what a person typed for a morpheme into one entry and analysis of the project: <c>entry:msa</c>, an entry
/// guid or Motif id, or a form in the lexicon. It names what it found and never guesses between two.
/// </summary>
internal static class FormsMorphemeResolver
{
    private const int MaxListed = 8;

    /// <summary>The most morphemes one explain call names: the root and every <c>--with</c>.</summary>
    internal const int MaxMorphemes = 32;

    /// <summary>Whether <paramref name="spec"/> reads as <c>entry:msa</c> with each side a guid or Motif id; the guids come back lowercase.</summary>
    internal static bool TryPair(string spec, out string entry, out string msa)
    {
        entry = msa = string.Empty;
        var parts = spec.Split(':');
        return parts.Length == 2 && TryGuid(parts[0], out entry) && TryGuid(parts[1], out msa);
    }

    /// <summary>Reads a guid or Motif canonical id as a lowercase textual guid.</summary>
    internal static bool TryGuid(string text, out string guid)
    {
        guid = string.Empty;
        var trimmed = text.Trim();
        if (Guid.TryParseExact(trimmed, "D", out var parsed))
        {
            guid = parsed.ToString("D", CultureInfo.InvariantCulture);
            return true;
        }
        if (!CanonicalId.TryParse(trimmed, out var id)) return false;
        guid = id.ToGuid().ToString("D", CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Names the morpheme <paramref name="spec"/> in <paramref name="cache"/>; a root must be a stem, any other morpheme an affix.</summary>
    internal static MorphemeLookup Resolve(LcmCache cache, string spec, bool asRoot)
    {
        var repository = cache.ServiceLocator.ObjectRepository;
        if (TryPair(spec, out var entryText, out var msaText))
        {
            if (!repository.TryGetObject(Guid.Parse(msaText), out var msaObject) || msaObject is not IMoMorphSynAnalysis msa ||
                msa.Owner is not ILexEntry owner || !string.Equals(owner.Guid.ToString("D"), entryText, StringComparison.Ordinal))
                return Unresolved(spec, $"'{spec}' is not an analysis of that entry in the project.");
            return Found(owner, msa, asRoot, spec);
        }
        if (TryGuid(spec, out var guid))
        {
            if (!repository.TryGetObject(Guid.Parse(guid), out var found))
                return Unresolved(spec, $"No entry or analysis has the id '{spec}' in the project.");
            if (found is IMoMorphSynAnalysis single && single.Owner is ILexEntry singleOwner)
                return Found(singleOwner, single, asRoot, spec);
            if (found is ILexEntry entry) return OfEntry(entry, asRoot, spec);
            return Unresolved(spec, $"The id '{spec}' names neither an entry nor an analysis.");
        }
        return ByForm(cache, spec, asRoot);
    }

    private static MorphemeLookup ByForm(LcmCache cache, string text, bool asRoot)
    {
        var wanted = Bare(text);
        var matches = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Where(entry => FormsOf(entry).Any(form => string.Equals(Bare(form), wanted, StringComparison.Ordinal)))
            .OrderBy(entry => entry.Guid)
            .ToArray();
        if (matches.Length == 0)
            return Unresolved(text, $"No entry in the project has the form '{text}'.");
        var usable = matches.Where(entry => Candidates(entry, asRoot).Any()).ToArray();
        if (usable.Length == 0)
            return Unresolved(text, $"'{text}' names {matches.Length.ToString(CultureInfo.InvariantCulture)} " +
                $"entr{(matches.Length == 1 ? "y" : "ies")}, but none has {(asRoot ? "a stem" : "an affix")} analysis.");
        if (usable.Length > 1)
            return Ambiguous(text, usable.Select(entry => Describe(entry, asRoot)).ToArray(), usable.Length);
        return OfEntry(usable[0], asRoot, text);
    }

    private static MorphemeLookup OfEntry(ILexEntry entry, bool asRoot, string spec)
    {
        var candidates = Candidates(entry, asRoot).ToArray();
        if (candidates.Length == 0)
            return Unresolved(spec, $"The entry '{Label(entry)}' has no {(asRoot ? "stem" : "affix")} analysis.");
        if (candidates.Length > 1)
            return Ambiguous(spec, candidates.Select(msa => Describe(entry, msa)).ToArray(), candidates.Length);
        return Found(entry, candidates[0], asRoot, spec);
    }

    private static MorphemeLookup Found(ILexEntry entry, IMoMorphSynAnalysis msa, bool asRoot, string spec)
    {
        var isStem = msa.ClassName == ResolvedMorpheme.StemClass;
        if (asRoot && !isStem)
            return Unresolved(spec, $"The root must be a stem; '{Label(entry)}' is {msa.ClassName}.");
        return new MorphemeLookup(new ResolvedMorpheme(entry.Guid.ToString("D", CultureInfo.InvariantCulture),
            msa.Guid.ToString("D", CultureInfo.InvariantCulture), msa.ClassName, Label(entry)), null);
    }

    private static IEnumerable<IMoMorphSynAnalysis> Candidates(ILexEntry entry, bool asRoot) =>
        entry.MorphoSyntaxAnalysesOC.Where(msa => (msa.ClassName == ResolvedMorpheme.StemClass) == asRoot)
            .OrderBy(msa => msa.Guid);

    private static IEnumerable<string> FormsOf(ILexEntry entry)
    {
        if (entry.HeadWord?.Text is { Length: > 0 } headword) yield return headword;
        foreach (var form in entry.AllAllomorphs)
            foreach (var ws in form.Form.AvailableWritingSystemIds)
                if (form.Form.get_String(ws)?.Text is { Length: > 0 } text) yield return text;
    }

    private static string Bare(string form) => form.Normalize(NormalizationForm.FormC).Trim().Trim('-', '=').Trim();

    private static string Label(ILexEntry entry) => entry.HeadWord?.Text ?? entry.Guid.ToString("D", CultureInfo.InvariantCulture);

    private static string Describe(ILexEntry entry, bool asRoot) =>
        $"{Label(entry)} {entry.Guid:D} ({string.Join(", ", Candidates(entry, asRoot).Select(msa => msa.ClassName))})";

    private static string Describe(ILexEntry entry, IMoMorphSynAnalysis msa) =>
        $"{entry.Guid:D}:{msa.Guid:D} ({msa.ClassName})";

    private static MorphemeLookup Unresolved(string spec, string message) =>
        new(null, new Refusal(RefusalCodes.FormsExplainMorphemeUnresolved, FailureReason.NotFound, message,
            new Dictionary<string, string> { ["morpheme"] = spec }));

    private static MorphemeLookup Ambiguous(string spec, IReadOnlyList<string> options, int total)
    {
        var shown = options.Take(MaxListed).ToArray();
        var more = total > shown.Length ? $" and {(total - shown.Length).ToString(CultureInfo.InvariantCulture)} more" : string.Empty;
        return new MorphemeLookup(null, new Refusal(RefusalCodes.FormsExplainMorphemeAmbiguous, FailureReason.InvalidArgument,
            $"'{spec}' matches {total.ToString(CultureInfo.InvariantCulture)} analyses: {string.Join("; ", shown)}{more}. " +
            "Name one as entry:msa.", new Dictionary<string, string> { ["morpheme"] = spec }));
    }
}
