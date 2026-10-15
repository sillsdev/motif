using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Retirement;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>
/// One composer a Draft is authored through, named by the CLI verb that runs it alone. The single verb and
/// <c>compose-batch</c> both run it from this description, so a batch has no path a single call lacks.
/// </summary>
/// <param name="Verb">The CLI verb, and the <c>verb</c> a batch entry names.</param>
/// <param name="Composer">The composer name recorded in the Draft's composer provenance.</param>
/// <param name="Build">Resolves the authored intent against the project to the operations it lowers to.</param>
/// <param name="ValidateAppend">
/// Checks the additions against the operations already staged: the Draft's own, then those added earlier in the
/// same batch.
/// </param>
/// <param name="ReplacePrevious">
/// Whether an operation already in the Draft is an earlier edit this intent replaces in place.
/// </param>
/// <param name="RelatedObjects">Existing objects the change affects, reported for review.</param>
internal sealed record DraftComposer(
    string Verb,
    string Composer,
    Func<LcmCache, JsonElement, CanonicalId, IReadOnlyList<OperationEnvelope>> Build,
    Action<IReadOnlyList<OperationEnvelope>, JsonElement, IReadOnlyList<OperationEnvelope>>? ValidateAppend = null,
    Func<JsonElement, OperationEnvelope, bool>? ReplacePrevious = null,
    Func<LcmCache, JsonElement, IReadOnlyList<ComposedRelatedObject>>? RelatedObjects = null);

public static partial class ProposalCommands
{
    /// <summary>Every composer a Draft can be authored through by intent, keyed by its CLI verb.</summary>
    internal static IReadOnlyDictionary<string, IComposerDefinition> DraftComposers { get; } =
        ComposerRegistry.Build(ComposerDefinitions.All, LegacyComposers());

    /// <summary>Every verb a Draft can be composed through, whether declared as a typed definition or not.</summary>
    public static IReadOnlyList<string> ComposerVerbs { get; } = DraftComposers.Keys.ToArray();

    /// <summary>What front ends need to offer each typed composer definition, in definition order.</summary>
    public static IReadOnlyList<ComposerDescriptor> ComposerDescriptors { get; } =
        ComposerDefinitions.All.Select(definition => new ComposerDescriptor(definition)).ToArray();

    private static DraftComposer[] LegacyComposers() => new DraftComposer[]
    {
        new("compose-author-lexeme-form", "AuthorLexemeForm", (cache, input, _) =>
            AuthorLexemeFormComposer.Build(cache, AuthorLexemeFormIntentParser.Parse(input))),
        new("compose-author-feature-structure", "AuthorFeatureStructure", (cache, input, _) =>
            AuthorFeatureStructureComposer.Build(cache, AuthorFeatureStructureIntentParser.Parse(input))),
        new("compose-author-phoneme", "AuthorPhoneme", (cache, input, _) =>
            AuthorPhonemeComposer.Build(cache, AuthorPhonemeIntentParser.Parse(input))),
        new("compose-author-natural-class", "AuthorNaturalClass", (cache, input, _) =>
            AuthorNaturalClassComposer.Build(cache, AuthorNaturalClassIntentParser.Parse(input))),
        new("compose-edit-natural-class", "EditNaturalClass", (cache, input, _) =>
                EditNaturalClassComposer.Build(cache, EditNaturalClassIntentParser.Parse(input)),
            ReplacePrevious: (input, operation) => ReplacesTarget(input, operation,
                PhNCSegmentsSegmentsOperationKinds.AddRefSegments, PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments),
            RelatedObjects: (cache, input) => EditNaturalClassComposer.ReadAffectedUsers(cache,
                EditNaturalClassIntentParser.Parse(input).Target)),
        new("compose-relink-natural-class", "RelinkNaturalClass", (cache, input, _) =>
                RelinkNaturalClassComposer.Build(cache, RelinkNaturalClassIntentParser.Parse(input)),
            ValidateAppend: (staged, input, operations) => RelinkNaturalClassComposer.RequireEarlierCreation(
                staged, RelinkNaturalClassIntentParser.Parse(input), operations),
            RelatedObjects: (cache, input) => EditNaturalClassComposer.ReadAffectedUsers(cache,
                RelinkNaturalClassIntentParser.Parse(input).Source)),
        new("compose-author-environment", "AuthorEnvironment", (cache, input, _) =>
            AuthorEnvironmentComposer.Build(cache, AuthorEnvironmentIntentParser.Parse(input))),
        new("compose-author-phonological-rule", "AuthorPhonologicalRule", (cache, input, _) =>
            AuthorPhonologicalRuleComposer.Build(cache, AuthorPhonologicalRuleIntentParser.Parse(input))),
        new("compose-author-part-of-speech", "AuthorPartOfSpeech", (cache, input, _) =>
            AuthorPartOfSpeechComposer.Build(cache, AuthorPartOfSpeechIntentParser.Parse(input))),
        new("compose-author-lexical-entry", "AuthorLexicalEntry", (cache, input, _) =>
            AuthorLexicalEntryComposer.Build(cache, AuthorLexicalEntryIntentParser.Parse(input))),
        new("compose-author-inflection-feature", "AuthorInflectionFeature", (cache, input, _) =>
            AuthorInflectionFeatureComposer.Build(cache, AuthorInflectionFeatureIntentParser.Parse(input))),
        new("compose-author-inflection-class", "AuthorInflectionClass", (cache, input, _) =>
            AuthorInflectionClassComposer.Build(cache, AuthorInflectionClassIntentParser.Parse(input))),
        new("compose-author-stem-name", "AuthorStemName", (cache, input, _) =>
            AuthorStemNameComposer.Build(cache, AuthorStemNameIntentParser.Parse(input))),
        new("compose-author-alternate-form", "AuthorAlternateForm", (cache, input, _) =>
            AuthorAlternateFormComposer.Build(cache, AuthorAlternateFormIntentParser.Parse(input))),
        new("compose-author-affix-slot", "AuthorAffixSlot", (cache, input, _) =>
            AuthorAffixSlotComposer.Build(cache, AuthorAffixSlotIntentParser.Parse(input))),
        new("compose-author-affix-template", "AuthorAffixTemplate", (cache, input, _) =>
            AuthorAffixTemplateComposer.Build(cache, AuthorAffixTemplateIntentParser.Parse(input))),
        new("retire-allomorph", RetirementRecipes.RuleReplacement.ComposerName, (cache, input, _) =>
        {
            var json = input.GetRawText();
            return input.TryGetProperty("format", out var format) &&
                   format.GetString() == RetirementRecipes.RuleReplacement.ProvenanceFormat
                ? ReplaceListedAllomorphsWithRuleComposer.Build(cache, AllomorphRetirementCodec.Parse(json),
                    () => CanonicalId.Mint())
                : RetireAllomorphComposer.Build(cache, RetireAllomorphCodec.Parse(json), () => CanonicalId.Mint());
        }),
        new("retire-redundant-zero-affix", "RetireRedundantZeroAffix", (cache, input, _) =>
            RetireRedundantZeroAffixComposer.Build(cache, RetireRedundantZeroAffixCodec.Parse(input.GetRawText()),
                () => CanonicalId.Mint())),
        new("compose-edit-adhoc-prohibition", "EditAdhocProhibition", (cache, input, _) =>
                EditAdhocProhibitionComposer.Build(cache, EditAdhocProhibitionIntentParser.Parse(input)),
            ValidateAppend: (staged, _, operations) => ValidateUniqueAdhocDisabledWrites(staged, operations)),
        new("compose-edit-affix-slot", "EditAffixSlot", (cache, input, _) =>
                EditAffixSlotComposer.Build(cache, EditAffixSlotIntentParser.Parse(input)),
            ReplacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoInflAffixSlotOptionalOperationKinds.SetOptional, MoInflAffixSlotOptionalOperationKinds.ClearOptional),
            RelatedObjects: (cache, input) => EditAffixSlotComposer.ReadAffectedUsers(cache,
                EditAffixSlotIntentParser.Parse(input))),
        new("compose-edit-affix-template", "EditAffixTemplate", (cache, input, _) =>
                EditAffixTemplateComposer.Build(cache, EditAffixTemplateIntentParser.Parse(input)),
            ReplacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.RemoveRefPrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.RemoveRefSuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.MoveSuffixSlots)),
        new("compose-edit-inflectional-affix", "EditInflectionalAffix", (cache, input, _) =>
                EditInflectionalAffixComposer.Build(cache, EditInflectionalAffixIntentParser.Parse(input)),
            ReplacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoInflAffMsaSlotsOperationKinds.AddRefSlots, MoInflAffMsaSlotsOperationKinds.RemoveRefSlots)),
        new("compose-edit-allomorph-condition", "EditAllomorphCondition", (cache, input, _) =>
                EditAllomorphConditionComposer.Build(cache, EditAllomorphConditionIntentParser.Parse(input)),
            ReplacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
                MoStemAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv,
                MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
                MoAffixAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv,
                MoAffixAllomorphPositionOperationKinds.AddRefPosition,
                MoAffixAllomorphPositionOperationKinds.RemoveRefPosition,
                MoAffixAllomorphPositionOperationKinds.MovePosition)),
        new("order-allomorphs", "OrderAllomorphs", (cache, input, _) =>
                OrderAllomorphsComposer.Build(cache, OrderAllomorphsIntentParser.Parse(input)),
            ReplacePrevious: (input, operation) => ReplacesTarget(input, operation,
                LexEntryAlternateFormsOperationKinds.MoveAlternateForms)),
        new("compose-author-text", "AuthorText", (cache, input, _) =>
            AuthorTextComposer.Build(cache, AuthorTextIntentParser.Parse(input))),
        new("compose-edit-analysis-decision", "EditAnalysisDecision", (cache, input, _) =>
            EditAnalysisDecisionComposer.Build(cache, EditAnalysisDecisionIntentParser.Parse(input))),
        new("compose-record-parsimony-disposition", "RecordParsimonyDisposition", (cache, input, proposalId) =>
            RecordParsimonyDispositionComposer.Build(cache, RecordParsimonyDispositionIntentParser.Parse(input),
                proposalId)),
    };
}
