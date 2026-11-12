using System;
using System.Collections.Generic;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Names the direction of a derivational affix by its category names, so a swapped pair such as
/// "Adjective -> Noun" for a Noun-to-Adjective prefix shows in the composer's summary instead of two opaque ids.
/// </summary>
public static class DerivationDirection
{
    /// <summary>The kind the direction carries in the related objects of a composed intent.</summary>
    public const string Kind = "derivation";

    /// <summary>The word shown where a derivational affix has no category on one side.</summary>
    public const string NoCategory = "(no category)";

    /// <summary>One entry naming "From -> To" when <paramref name="msa"/> is derivational; none otherwise.</summary>
    public static IReadOnlyList<ComposedRelatedObject> Read(LcmCache cache, LexicalEntryMsa msa)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(msa);
        if (msa.Kind != LexicalEntryMsaKind.Derivational || msa.ToCategory is not { } to) return [];
        return [new ComposedRelatedObject(Kind, msa.Category.Value, $"{NameOf(cache, msa.Category)} -> {NameOf(cache, to)}")];
    }

    /// <summary>The category's best analysis name, or its id when the category is unknown or unnamed.</summary>
    public static string NameOf(LcmCache cache, CanonicalId category)
    {
        if (cache.ServiceLocator.ObjectRepository.TryGetObject(category.ToGuid(), out var found) &&
            found is IPartOfSpeech { Name: { } name } &&
            name.BestAnalysisAlternative.Text is { Length: > 0 } text && text != "***")
            return text;
        return category.Value;
    }
}
