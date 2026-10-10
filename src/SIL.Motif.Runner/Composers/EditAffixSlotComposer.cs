using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds an optionality write for one existing affix slot and reads its shared users.</summary>
public static class EditAffixSlotComposer
{
    private const string ConstructName = "EditAffixSlot";
    private const string Rationale = "Authored by the EditAffixSlot composer.";

    /// <summary>Resolves the slot and its expected value without changing the project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAffixSlotIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var slot = ReferenceFieldLowering.Resolve<IMoInflAffixSlot>(cache, intent.Target, ConstructName);
        if (slot.Optional != intent.ExpectedOptional)
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' has Optional={slot.Optional}; " +
                $"the intent expected Optional={intent.ExpectedOptional}.");
        if (slot.Optional == intent.Optional) return [];
        return
        [
            new OperationEnvelope(CanonicalId.Mint(), MoInflAffixSlotOptionalOperationKinds.SetOptional,
                target: intent.Target, after: JsonSerializer.SerializeToElement(new { value = intent.Optional }),
                rationale: Rationale),
        ];
    }

    /// <summary>Returns every inflectional MSA and template whose grammar uses the slot.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadAffectedUsers(LcmCache cache,
        EditAffixSlotIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var slot = ReferenceFieldLowering.Resolve<IMoInflAffixSlot>(cache, intent.Target, ConstructName);
        var objects = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances();
        var users = objects.OfType<IMoInflAffMsa>()
            .Where(msa => msa.SlotsRC.Contains(slot))
            .Select(msa => new ComposedRelatedObject("inflectional-affix", Id(msa), AffixName(msa)))
            .Concat(objects.OfType<IMoInflAffixTemplate>()
                .Where(template => template.PrefixSlotsRS.Contains(slot) || template.SuffixSlotsRS.Contains(slot))
                .Select(template => new ComposedRelatedObject("affix-template", Id(template), Text(template.Name))))
            .OrderBy(user => user.Kind, StringComparer.Ordinal)
            .ThenBy(user => user.Id, StringComparer.Ordinal)
            .ToArray();
        return users;
    }

    private static string AffixName(IMoInflAffMsa msa)
    {
        var entry = msa.Owner as ILexEntry;
        var headword = entry?.HeadWord?.Text ?? string.Empty;
        var gloss = entry?.AllSenses.FirstOrDefault(sense => sense.MorphoSyntaxAnalysisRA == msa)?.Gloss
            .BestAnalysisAlternative?.Text;
        return string.IsNullOrWhiteSpace(gloss) ? headword : $"{headword} ‘{gloss}’";
    }

    private static string Text(IMultiAccessorBase? text) =>
        text?.BestAnalysisAlternative?.Text is { Length: > 0 } value && value != "***" ? value : string.Empty;

    private static string Id(ICmObject value) => CanonicalId.FromGuid(value.Guid).Value;
}
