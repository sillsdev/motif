using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Adds a Dry Run warning for a touched derivational affix that nothing in the project can feed: no stem of its
/// from-category (or a subcategory of it), and no other derivational affix that makes that category. It also warns when
/// the to-category it makes is consumed by nothing: no other affix takes it (or a related category) as input, and no
/// template or inflectional analysis is on it. This is a count over the project's analyses, not a parse, so it is cheap;
/// either is the usual symptom of from and to categories swapped.
/// </summary>
internal static class DerivationalFeedWarnings
{
    internal static void Append(LcmCache cache, IReadOnlyList<OperationEnvelope> operations, List<ExpectedEffect> effects)
    {
        var touched = TouchedDerivationalAnalyses(cache, operations);
        if (touched.Count == 0) return;
        var stems = cache.ServiceLocator.GetInstance<IMoStemMsaRepository>().AllInstances()
            .Select(stem => stem.PartOfSpeechRA).OfType<IPartOfSpeech>().ToArray();
        var derivations = cache.ServiceLocator.GetInstance<IMoDerivAffMsaRepository>().AllInstances().ToArray();
        foreach (var msa in touched.Values)
            AppendToCategoryWarning(cache, msa, derivations, effects);
        foreach (var msa in touched.Values)
        {
            if (msa.FromPartOfSpeechRA is not { } from) continue;
            if (stems.Any(stem => LexicalFormAuthoring.InCategoryOrAncestor(from, stem))) continue;
            if (derivations.Any(other => other.Guid != msa.Guid &&
                    other.ToPartOfSpeechRA is { } made && LexicalFormAuthoring.InCategoryOrAncestor(from, made))) continue;
            var fromName = Name(from);
            var toName = msa.ToPartOfSpeechRA is { } to ? Name(to) : DerivationDirection.NoCategory;
            var warning = $"This derivational affix ({fromName} -> {toName}) attaches to {fromName}, but the project has no " +
                $"{fromName} stem (or subcategory stem, or derivation that makes {fromName}) for it to apply to, so it will " +
                "gain no words. If the categories are the wrong way round, swap them.";
            effects.Add(new(CanonicalId.FromGuid(msa.Guid), "dryRunWarning", new Dictionary<string, string>(),
                new Dictionary<string, string> { ["warning"] = warning },
                System.Text.Json.JsonSerializer.SerializeToElement(new { warning })));
        }
    }

    private static void AppendToCategoryWarning(LcmCache cache, IMoDerivAffMsa msa,
        IReadOnlyList<IMoDerivAffMsa> derivations, List<ExpectedEffect> effects)
    {
        if (msa.ToPartOfSpeechRA is not { } to) return;
        bool Related(IPartOfSpeech? category) => category is not null &&
            (LexicalFormAuthoring.InCategoryOrAncestor(to, category) || LexicalFormAuthoring.InCategoryOrAncestor(category, to));
        if (derivations.Any(other => other.Guid != msa.Guid && Related(other.FromPartOfSpeechRA))) return;
        var locator = cache.ServiceLocator;
        if (locator.GetInstance<IMoInflAffMsaRepository>().AllInstances().Any(other => Related(other.PartOfSpeechRA))) return;
        if (locator.GetInstance<IMoUnclassifiedAffixMsaRepository>().AllInstances().Any(other => Related(other.PartOfSpeechRA))) return;
        if (locator.GetInstance<IPartOfSpeechRepository>().AllInstances()
                .Any(category => category.AffixTemplatesOS.Count > 0 && Related(category))) return;
        var fromName = msa.FromPartOfSpeechRA is { } from ? Name(from) : DerivationDirection.NoCategory;
        var toName = Name(to);
        var warning = $"This derivational affix ({fromName} -> {toName}) makes {toName}, but nothing in the project takes " +
            $"{toName} (or a subcategory) as input: no affix attaches to it and no template or inflectional affix is on it, " +
            "so its words can take nothing further. If the categories are the wrong way round, swap them.";
        effects.Add(new(CanonicalId.FromGuid(msa.Guid), "dryRunWarning", new Dictionary<string, string>(),
            new Dictionary<string, string> { ["warning"] = warning },
            System.Text.Json.JsonSerializer.SerializeToElement(new { warning })));
    }

    private static string Name(IPartOfSpeech category) =>
        category.Name.BestAnalysisAlternative.Text is { Length: > 0 } text && text != "***"
            ? text : CanonicalId.FromGuid(category.Guid).Value;

    private static Dictionary<Guid, IMoDerivAffMsa> TouchedDerivationalAnalyses(
        LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var touched = new Dictionary<Guid, IMoDerivAffMsa>();
        foreach (var operation in operations)
            foreach (var id in new[] { operation.Target, operation.EntityId })
                if (id is { } value && repository.TryGetObject(value.ToGuid(), out var found) && found is IMoDerivAffMsa msa)
                    touched[msa.Guid] = msa;
        return touched;
    }
}
