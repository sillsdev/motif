using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds a reviewable slot creation and any requested inflectional MSA assignments.</summary>
public static class AuthorAffixSlotComposer
{
    private const string ConstructName = "AuthorAffixSlot";
    private const string Rationale = "Authored by the AuthorAffixSlot composer.";

    /// <summary>Authors a category-owned slot without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorAffixSlotIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        if (string.IsNullOrWhiteSpace(intent.WritingSystem) ||
            cache.WritingSystemFactory.GetWsFromStr(intent.WritingSystem) == 0)
            throw new InvalidOperationException(
                $"'{ConstructName}': writing system tag '{intent.WritingSystem}' is not known to this project.");

        var category = ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, intent.Category, ConstructName);
        var assignments = intent.Assignments ?? [];
        if (assignments.Distinct().Count() != assignments.Count)
            throw new InvalidOperationException($"'{ConstructName}': assignments must not contain duplicate MSA ids.");

        var mint = mintId ?? (() => CanonicalId.Mint());
        var slotId = mint();
        var createOperationId = mint();
        var dependency = new[] { new OperationDependency(createOperationId) };
        var operations = new List<OperationEnvelope>
        {
            new(createOperationId, PartOfSpeechAffixSlotsOperationKinds.Create,
                entityId: slotId, target: intent.Category,
                after: JsonSerializer.SerializeToElement(new { }), rationale: Rationale),
            new(mint(), MoInflAffixSlotNameOperationKinds.SetName, target: slotId,
                after: JsonSerializer.SerializeToElement(new
                {
                    ws = intent.WritingSystem,
                    text = intent.Name.Normalize(NormalizationForm.FormD),
                }), dependsOn: dependency, rationale: Rationale),
            new(mint(), MoInflAffixSlotOptionalOperationKinds.SetOptional, target: slotId,
                after: JsonSerializer.SerializeToElement(new { value = intent.Optional }),
                dependsOn: dependency, rationale: Rationale),
        };

        foreach (var msa in assignments.OrderBy(id => id.Value, StringComparer.Ordinal))
            operations.Add(EditInflectionalAffixComposer.BuildAssignmentForCreatedSlot(
                cache, msa, slotId, category, createOperationId, mint));

        return operations;
    }
}
