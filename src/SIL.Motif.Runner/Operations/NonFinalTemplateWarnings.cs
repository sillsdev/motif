using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Adds a Dry Run warning for a touched affix template whose Final value is false while no derivational affix takes
/// its category as input. A word can end only after a final template or after a derivation that continues from it, so
/// such a template never completes a word and its words stay unparsed. This reads the project's analyses, not a parse.
/// </summary>
internal static class NonFinalTemplateWarnings
{
    internal static void Append(LcmCache cache, IReadOnlyList<OperationEnvelope> operations, List<ExpectedEffect> effects)
    {
        var touched = TouchedTemplates(cache, operations);
        if (touched.Count == 0) return;
        var derivations = cache.ServiceLocator.GetInstance<IMoDerivAffMsaRepository>().AllInstances().ToArray();
        foreach (var template in touched.Values)
        {
            if (template.Final || template.Owner is not IPartOfSpeech category) continue;
            if (derivations.Any(msa => msa.FromPartOfSpeechRA is { } from &&
                    (LexicalFormAuthoring.InCategoryOrAncestor(category, from) ||
                     LexicalFormAuthoring.InCategoryOrAncestor(from, category)))) continue;
            var categoryName = Name(category);
            var templateName = template.Name.BestAnalysisAlternative.Text is { Length: > 0 } text && text != "***"
                ? text : CanonicalId.FromGuid(template.Guid).Value;
            var others = category.AffixTemplatesOS.Count(other => other.Guid != template.Guid && other.Final);
            var elsewhere = others == 0
                ? $"it is the only template for {categoryName} that can end a word"
                : $"{categoryName} has {others} other final template(s), but this one still ends no word";
            var warning = $"Template '{templateName}' for {categoryName} has Final set to false, and no derivational " +
                $"affix continues from {categoryName} ({elsewhere}), so a word cannot end after it and its words will not parse. " +
                "Set Final to true unless a derivation continues from this template.";
            effects.Add(new(CanonicalId.FromGuid(template.Guid), "dryRunWarning", new Dictionary<string, string>(),
                new Dictionary<string, string> { ["warning"] = warning },
                System.Text.Json.JsonSerializer.SerializeToElement(new { warning })));
        }
    }

    private static string Name(IPartOfSpeech category) =>
        category.Name.BestAnalysisAlternative.Text is { Length: > 0 } text && text != "***"
            ? text : CanonicalId.FromGuid(category.Guid).Value;

    private static Dictionary<Guid, IMoInflAffixTemplate> TouchedTemplates(
        LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var touched = new Dictionary<Guid, IMoInflAffixTemplate>();
        foreach (var operation in operations)
            foreach (var id in new[] { operation.Target, operation.EntityId })
                if (id is { } value && repository.TryGetObject(value.ToGuid(), out var found) &&
                    found is IMoInflAffixTemplate template)
                    touched[template.Guid] = template;
        return touched;
    }
}
