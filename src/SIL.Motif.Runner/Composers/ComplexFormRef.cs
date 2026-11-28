using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>One complex-form link of an entry: what the entry is a complex form of.</summary>
/// <param name="Entry">The entry that is the complex form.</param>
/// <param name="Components">The entries or senses it is made of, at least one.</param>
/// <param name="ComplexTypes">Types in the project's complex form types list; none is FieldWorks' "not applicable".</param>
/// <param name="PrimaryLexemes">The components that are primary; null for none.</param>
/// <param name="ShowComplexFormsIn">The components that list this complex form; null shows it under all of them (unless the type is a derivation).</param>
/// <param name="HideMinorEntry">Hides the minor entry in dictionary views; false shows it.</param>
public sealed record ComplexFormRefIntent(
    CanonicalId Entry,
    IReadOnlyList<CanonicalId> Components,
    IReadOnlyList<CanonicalId> ComplexTypes,
    IReadOnlyList<CanonicalId>? PrimaryLexemes = null,
    IReadOnlyList<CanonicalId>? ShowComplexFormsIn = null,
    bool HideMinorEntry = false);

/// <summary>
/// Stages the complex-form link of an entry as FieldWorks makes it: a LexEntryRef with RefType krtComplexForm and its complex form
/// types (InsertEntryDlg.CreateNewEntryInternal, its m_fComplexForm branch), then each component added to ComponentLexemes with
/// the primary lexemes and ShowComplexFormsIn the payload names; ShowComplexFormsIn defaults to the components in their order, and no primary is
/// chosen unless named, where the lexicon editor's coupling (EntrySequenceReferenceLauncher.AddNewObjectsToProperty) makes the first component primary.
/// </summary>
public static class ComplexFormRefComposer
{
    private const string Rationale = "Authored by the complex-form composer: a complex-form link of one entry.";

    /// <summary>Composes the link. Components in <paramref name="newComponents"/> are created earlier in the same batch and are not read from the project.</summary>
    public static OperationEnvelope Build(LcmCache cache, ComplexFormRefIntent intent, CanonicalId operationId, CanonicalId referenceId,
        CanonicalId? entryCreate = null, IReadOnlySet<CanonicalId>? newComponents = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Components.Count == 0)
            throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidComponent, "A complex form needs at least one component entry or sense.");
        foreach (var type in intent.ComplexTypes)
            VariantLinkSafety.ResolveObject<ILexEntryType>(cache, type, RefusalCodes.VariantInvalidType,
                "The type must belong to the complex form types list.");
        foreach (var component in intent.Components.Where(item => newComponents is null || !newComponents.Contains(item)))
        {
            var found = VariantLinkSafety.ResolveObject<ICmObject>(cache, component, RefusalCodes.VariantInvalidComponent,
                "The component must be an entry or sense.");
            if (found is not (ILexEntry or ILexSense))
                throw VariantLinkSafety.Refuse(RefusalCodes.VariantInvalidComponent, "The component must be an entry or sense.");
        }
        var payload = new Dictionary<string, object?>
        {
            ["complexTypes"] = intent.ComplexTypes.Select(item => item.Value).ToArray(),
            ["components"] = intent.Components.Select(item => item.Value).ToArray(),
        };
        if (intent.PrimaryLexemes is not null) payload["primaryLexemes"] = intent.PrimaryLexemes.Select(item => item.Value).ToArray();
        if (intent.ShowComplexFormsIn is not null) payload["showComplexFormsIn"] = intent.ShowComplexFormsIn.Select(item => item.Value).ToArray();
        if (intent.HideMinorEntry) payload["hideMinorEntry"] = true;
        return new OperationEnvelope(operationId, ComplexFormRefCreateOperationKinds.Create, target: intent.Entry, entityId: referenceId,
            after: JsonSerializer.SerializeToElement(payload),
            dependsOn: entryCreate is { } create ? [new OperationDependency(create)] : null, rationale: Rationale);
    }
}
