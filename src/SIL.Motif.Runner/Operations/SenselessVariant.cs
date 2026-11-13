using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Decides when an entry may be left with no sense. LibLCM's CreateVariantEntryAndBackRef makes a variant entry with a
/// lexeme form and a variant reference and no sense, so that shape is one FieldWorks produces (ADR 0112). The parser
/// loads such an entry only through its variant reference and a component with a stem analysis, pinned by
/// `RealParserSenselessVariantTests`.
/// </summary>
internal static class SenselessVariant
{
    /// <summary>Why a sense-less entry would be dropped by the parser, or null when it stays a variant of a stem.</summary>
    internal static string? WhyNotAllowed(ILexEntry entry)
    {
        var variants = entry.EntryRefsOS
            .Where(reference => reference.RefType == LexEntryRefTags.krtVariant && reference.VariantEntryTypesRS.Count > 0)
            .ToArray();
        if (variants.Length == 0)
            return "the entry is not a variant (it has no variant reference with a variant type)";
        foreach (var reference in variants)
        {
            var stem = reference.ComponentLexemesRS.FirstOrDefault(HasStemAnalysis);
            if (stem is not null) return null;
        }
        return "no component of its variant reference carries a stem analysis, so the parser would load nothing for it";
    }

    /// <summary>True when the entry or sense is a stem the parser loads: a sense with a stem analysis, or an entry with one.</summary>
    internal static bool HasStemAnalysis(ICmObject component) => component switch
    {
        ILexSense sense => sense.MorphoSyntaxAnalysisRA is IMoStemMsa,
        ILexEntry entry => entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().Any(),
        _ => false,
    };

    /// <summary>The warning for a sense-less variant whose component has no stem analysis.</summary>
    internal static string NoStemWarning(ICmObject component) =>
        $"{SenseRemoval.Describe(component)} has no stem analysis, so the parser loads nothing for a variant of it " +
        "that has no sense of its own. Give the main entry or sense a stem analysis, or keep a sense on the variant.";
}

/// <summary>
/// Adds a Dry Run warning for a touched entry that has no sense and is a variant whose components carry no stem
/// analysis: the parser loads nothing for it, so its form silently stops parsing.
/// </summary>
internal static class SenselessVariantWarnings
{
    internal static void Append(LcmCache cache, IReadOnlyList<OperationEnvelope> operations, List<ExpectedEffect> effects)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var entries = new Dictionary<Guid, ILexEntry>();
        foreach (var operation in operations)
            foreach (var id in new[] { operation.Target, operation.EntityId })
                if (id is { } value && repository.TryGetObject(value.ToGuid(), out var found))
                    switch (found)
                    {
                        case ILexEntry entry: entries[entry.Guid] = entry; break;
                        case ILexEntryRef { Owner: ILexEntry owner }: entries[owner.Guid] = owner; break;
                    }
        foreach (var entry in entries.Values)
        {
            if (entry.SensesOS.Count != 0 || !entry.EntryRefsOS.Any(r => r.RefType == LexEntryRefTags.krtVariant)) continue;
            if (SenselessVariant.WhyNotAllowed(entry) is not { } why) continue;
            var warning = $"{SenseRemoval.Describe(entry)} has no sense and {why}.";
            effects.Add(new(CanonicalId.FromGuid(entry.Guid), "dryRunWarning", new Dictionary<string, string>(),
                new Dictionary<string, string> { ["warning"] = warning },
                System.Text.Json.JsonSerializer.SerializeToElement(new { warning })));
        }
    }
}
