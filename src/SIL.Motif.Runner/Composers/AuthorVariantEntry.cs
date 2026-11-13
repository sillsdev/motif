using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>One new variant entry with no sense, bound to the one sense (or entry) it varies.</summary>
/// <param name="Component">The main sense (or entry) the variant is a variant of.</param>
/// <param name="VariantType">A type in the project's variant types list.</param>
/// <param name="Form">The variant's lexeme form.</param>
/// <param name="FormWritingSystem">The vernacular writing system tag of <paramref name="Form"/>.</param>
/// <param name="MorphType">The existing morph type of the variant's form.</param>
/// <param name="Environments">The phonological environments the form occurs in; absent means any.</param>
/// <param name="HideMinorEntry">Hides the variant's minor entry in dictionary views; false shows it.</param>
public sealed record AuthorVariantEntryIntent(
    CanonicalId Component,
    CanonicalId VariantType,
    string Form,
    string FormWritingSystem,
    CanonicalId MorphType,
    IReadOnlyList<CanonicalId>? Environments = null,
    bool HideMinorEntry = false);

/// <summary>Reads a closed author-variant-entry intent.</summary>
public static class AuthorVariantEntryIntentParser
{
    /// <summary>Reads the intent, refusing any property it does not name.</summary>
    public static AuthorVariantEntryIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorVariantEntry", "component", "variantType", "form", "formWs",
            "morphType", "environments", "hideMinorEntry");
        return new(SoundSystemIntentParsing.Id(authored, "component"), SoundSystemIntentParsing.Id(authored, "variantType"),
            SoundSystemIntentParsing.Text(authored, "form"), SoundSystemIntentParsing.Text(authored, "formWs"),
            SoundSystemIntentParsing.Id(authored, "morphType"),
            AuthorLexicalEntryIntentParser.NonEmptyIds(authored, "environments"),
            VariantIntentFlags.Optional(authored, "hideMinorEntry"));
    }
}

/// <summary>
/// Creates a sense-less variant entry in one step: the entry, its lexeme form with morph type and environments, and a
/// variant reference to the main sense. LibLCM's CreateVariantEntryAndBackRef makes the same shape (a lexeme form and a
/// variant reference, no sense and no analysis), the path ADR 0112 requires a composer to cite. The parser then reads the variant as the main sense's stem.
/// </summary>
public static class AuthorVariantEntryComposer
{
    private const string ConstructName = "AuthorVariantEntry";
    private const string Rationale = "Authored by the AuthorVariantEntry composer: a sense-less variant of one sense.";

    /// <summary>Composes the variant without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorVariantEntryIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var component = VariantLinkSafety.ResolveObject<ICmObject>(cache, intent.Component,
            RefusalCodes.VariantInvalidComponent, "The component must be an entry or sense.");
        if (component is not (ILexEntry or ILexSense))
            throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidComponent, "The component must be an entry or sense.");
        VariantLinkSafety.ResolveObject<ILexEntryType>(cache, intent.VariantType, RefusalCodes.VariantInvalidType,
            "The type must belong to the variant types list.");
        var source = mintId ?? (() => CanonicalId.Mint());
        var entryIntent = new AuthorLexicalEntryIntent(intent.Form, intent.FormWritingSystem, intent.MorphType, "", "",
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, default), Environments: intent.Environments);
        var operations = AuthorLexicalEntryComposer.BuildEntry(cache, entryIntent, source, null, withSense: false, withMsa: false)
            .ToList();
        var createEntry = operations.Single(operation => operation.Kind == LexDbEntriesOperationKinds.Create);
        var entryId = createEntry.EntityId!.Value;
        operations.Add(new OperationEnvelope(LexicalFormAuthoring.Normalize(source()), VariantRefCreateOperationKinds.Create,
            target: entryId, entityId: LexicalFormAuthoring.Normalize(source()),
            after: VariantIntentFlags.RefPayload(intent.Component, intent.VariantType, intent.HideMinorEntry),
            dependsOn: [new OperationDependency(createEntry.OperationId)], rationale: Rationale));
        return operations;
    }

    /// <summary>Warns when the main component carries no stem analysis, so the parser would load nothing for the variant.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadWarnings(LcmCache cache, AuthorVariantEntryIntent intent)
    {
        var component = VariantLinkSafety.ResolveObject<ICmObject>(cache, intent.Component,
            RefusalCodes.VariantInvalidComponent, "The component must be an entry or sense.");
        return SenselessVariant.HasStemAnalysis(component) ? []
            : [new ComposedRelatedObject("variant-without-stem", CanonicalId.FromGuid(component.Guid).Value,
                SenselessVariant.NoStemWarning(component))];
    }
}
