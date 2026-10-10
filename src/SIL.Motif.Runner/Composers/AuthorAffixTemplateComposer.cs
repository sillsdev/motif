using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds operations that add an alternative template to an existing category.</summary>
public static class AuthorAffixTemplateComposer
{
    private const string ConstructName = "AuthorAffixTemplate";
    private const string Rationale = "Authored by the AuthorAffixTemplate composer.";

    /// <summary>Authors a named template using slots owned by the category or one of its ancestors.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorAffixTemplateIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        if (string.IsNullOrWhiteSpace(intent.WritingSystem) ||
            cache.WritingSystemFactory.GetWsFromStr(intent.WritingSystem) == 0)
            throw new InvalidOperationException(
                $"'{ConstructName}': writing system tag '{intent.WritingSystem}' is not known to this project.");
        if (intent.PrefixSlots is null || intent.SuffixSlots is null)
            throw new InvalidOperationException($"'{ConstructName}': both ordered slot lists must be supplied.");

        var category = ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, intent.Category, ConstructName);
        var slotIds = intent.PrefixSlots.Concat(intent.SuffixSlots).ToArray();
        if (slotIds.Distinct().Count() != slotIds.Length)
            throw new InvalidOperationException(
                $"'{ConstructName}': a slot cannot appear more than once across the prefix and suffix lists.");

        foreach (var slotId in slotIds)
        {
            var slot = ReferenceFieldLowering.Resolve<IMoInflAffixSlot>(cache, slotId, ConstructName);
            if (!BelongsToCategoryOrAncestor(slot, category))
                throw new InvalidOperationException(
                    $"'{ConstructName}': slot '{slotId.Value}' does not belong to the template category or one of its ancestors.");
        }

        var mint = mintId ?? (() => CanonicalId.Mint());
        var templateId = mint();
        var createOperationId = mint();
        var createDependency = new[] { new OperationDependency(createOperationId) };
        var lastTemplate = category.AffixTemplatesOS.LastOrDefault();
        var placement = lastTemplate is null ? null : new Placement(CanonicalId.FromGuid(lastTemplate.Guid), null);
        var operations = new List<OperationEnvelope>
        {
            new(createOperationId, PartOfSpeechAffixTemplatesOperationKinds.Create,
                entityId: templateId, target: intent.Category,
                after: JsonSerializer.SerializeToElement(new { }), placement, rationale: Rationale),
            new(mint(), MoInflAffixTemplateNameOperationKinds.SetName, target: templateId,
                after: JsonSerializer.SerializeToElement(new
                {
                    ws = intent.WritingSystem,
                    text = intent.Name.Normalize(NormalizationForm.FormD),
                }), dependsOn: createDependency, rationale: Rationale),
            new(mint(), MoInflAffixTemplateFinalOperationKinds.SetFinal, target: templateId,
                after: JsonSerializer.SerializeToElement(new { value = intent.Final }),
                dependsOn: createDependency, rationale: Rationale),
        };

        AddMemberships(operations, intent.PrefixSlots, templateId, createOperationId,
            MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots, mint);
        AddMemberships(operations, intent.SuffixSlots, templateId, createOperationId,
            MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots, mint);
        return operations;
    }

    private static void AddMemberships(List<OperationEnvelope> operations, IReadOnlyList<CanonicalId> slots,
        CanonicalId templateId, CanonicalId createOperationId, string kind, Func<CanonicalId> mintId)
    {
        CanonicalId? previousMember = null;
        CanonicalId? previousAddOperation = null;
        foreach (var slot in slots)
        {
            var dependencies = new List<OperationDependency> { new(createOperationId) };
            if (previousAddOperation is { } previous)
                dependencies.Add(new OperationDependency(previous));
            var placement = previousMember is { } anchor ? new Placement(anchor, null) : null;
            var add = new OperationEnvelope(mintId(), kind, target: templateId,
                after: JsonSerializer.SerializeToElement(new { member = slot.Value }),
                placement: placement, dependsOn: dependencies, rationale: Rationale);
            operations.Add(add);
            previousMember = slot;
            previousAddOperation = add.OperationId;
        }
    }

    private static bool BelongsToCategoryOrAncestor(IMoInflAffixSlot slot, IPartOfSpeech category)
    {
        var owner = slot.Owner as IPartOfSpeech;
        for (var current = category; current is not null; current = current.Owner as IPartOfSpeech)
            if (owner?.Guid == current.Guid) return true;
        return false;
    }
}
