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
        new AuthorAllomorphRequiredFeaturesDefinition(),
        new RemoveFeatureValueDefinition(),
        new DeleteEmptySenseDefinition(),
        new MoveSenseToEntryDefinition(),
        new MoveExampleDefinition(),
        new AddExampleDefinition(),
        new DeleteSenseDefinition(),
        new DeleteLexicalEntryDefinition(),
        new MergeLexicalEntriesDefinition(),
        new RemoveAlternateFormDefinition(),
        new LinkVariantDefinition(),
        new MakeVariantOfDefinition(),
        new MakeVariantsOfDefinition(),
        new StripVariantSensesDefinition(),
        new AuthorVariantEntryDefinition(),
        new AuthorAdhocProhibitionDefinition(),
        new EditAdhocProhibitionReferencesDefinition(),
        new DeleteAdhocProhibitionDefinition(),
        new DeleteAffixProcessDefinition(),
        new EditMorphTypeDefinition(),
        new EditTextNotesDefinition(),
        new ChangeAffixKindDefinition(),
        new EditCliticAttachmentDefinition(),
        new EditPhonemeDefinition(),
        new AuthorPhonologicalFeatureDefinition(),
        new AddPhonologicalFeatureValueDefinition(),
        new EditPhonemeFeaturesDefinition(),
        new EditNaturalClassFeaturesDefinition(),
        new MergePhonemeDefinition(),
        new SplitPhonemeDefinition(),
        new EditAffixInflectionClassesDefinition(),
        new EditDerivationalAffixRestrictionDefinition(),
        new AuthorExceptionFeatureDefinition(),
        new EditExceptionFeaturesDefinition(),
        new AddExceptionFeaturesDefinition(),
        new TagStemsByQueryDefinition(),
        new RemoveExceptionFeatureDefinition(),
        new EditPhonologicalRuleFeaturesDefinition(),
        new EditPhonologicalRuleDefinition(),
        new OrderPhonologicalRulesDefinition(),
        new SetPhonologicalRuleDisabledDefinition(),
        new DeletePhonologicalRuleDefinition(),
        new EditCompoundRuleDefinition(),
        new EditPartOfSpeechInflectionClassDefaultDefinition(),
        new EditStemAllomorphStemNameDefinition(),
        new EditStemInflectionClassDefinition(),
        new SetNoDefaultCompoundingDefinition(),
        new SetNotOnCliticsDefinition(),
        new SetCharacterClassDefinition(),
        new AuthorCircumfixDefinition(),
        new AddMorphoSyntaxAnalysisDefinition(),
        new SetEntryDoNotParseDefinition(),
        new RewriteLexemeFormDefinition(),
        new EditAllomorphFormDefinition(),
        new EditCitationFormDefinition(),
        new EditLexicalTextDefinition("definition"),
        new EditLexicalTextDefinition("gloss"),
        new EditLexicalTextDefinition("lexical-note"),
        new SetStemPartOfSpeechDefinition(),
        new RetireAllomorphDefinition(),
        new RetireRedundantZeroAffixDefinition(),
        new RetireBoundaryMarkerDefinition(),
        new MergeBoundaryMarkersDefinition(),
        new RetireAffixSlotDefinition(),
        new DeleteExceptionFeatureDefinition(),
        new DeleteEnvironmentDefinition(),
        new DeleteNaturalClassDefinition(),
        new DeleteTextSegmentNoteDefinition(),
        new RecordParsimonyDispositionDefinition(),
        new RecordSourceValidatedDecisionDefinition(),
        new AddSourceDefinition(),
        new SetSourceNameDefinition(),
        new SetSourceContextDefinition(),
        new SetSourceVersificationDefinition(),
        new SetSourceStatusDefinition(),
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
