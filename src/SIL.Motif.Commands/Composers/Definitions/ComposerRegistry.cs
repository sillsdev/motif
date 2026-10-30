namespace SIL.Motif.Commands;

/// <summary>The composers declared as typed definitions, in the order front ends list them.</summary>
internal static class ComposerDefinitions
{
    /// <summary>One instance of every typed definition.</summary>
    public static IReadOnlyList<IComposerDefinition> All { get; } = 
    [
        new AuthorAffixProcessDefinition(),
        new AuthorAffixProcessAllomorphDefinition(),
        new AuthorFeatureValueDefinition(),
        new DeleteEmptySenseDefinition(),
        new LinkVariantDefinition(),
        new EditPhonemeDefinition(),
        new EditAffixInflectionClassesDefinition(),
        new EditDerivationalAffixRestrictionDefinition(),
        new EditCompoundRuleDefinition(),
        new EditPartOfSpeechInflectionClassDefaultDefinition(),
        new EditStemAllomorphStemNameDefinition(),
        new EditStemInflectionClassDefinition(),
        new SetNoDefaultCompoundingDefinition(),
        new SetEntryDoNotParseDefinition(),
        new RewriteLexemeFormDefinition(),
        new SetStemPartOfSpeechDefinition(),
        new RetireAllomorphDefinition(),
        new RetireRedundantZeroAffixDefinition(),
        new RecordParsimonyDispositionDefinition(),
    ];
}

/// <summary>Joins typed definitions and legacy composers into the one verb-keyed table the engine reads.</summary>
internal static class ComposerRegistry
{
    /// <summary>Builds the table; a verb declared twice throws, so two composers cannot share a command.</summary>
    public static IReadOnlyDictionary<string, IComposerDefinition> Build(
        IEnumerable<IComposerDefinition> typed, IEnumerable<DraftComposer> legacy)
    {
        var table = new Dictionary<string, IComposerDefinition>(StringComparer.Ordinal);
        foreach (var definition in typed.Concat(legacy.Select(composer => (IComposerDefinition)new LegacyComposerAdapter(composer))))
            if (!table.TryAdd(definition.Verb, definition))
                throw new InvalidOperationException($"The composing verb '{definition.Verb}' is declared twice.");
        return table;
    }
}
