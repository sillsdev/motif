using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Lowers a named stem pattern and its realization into the Input and Output creates of one process, so a
/// process that is an entry's lexeme form and one that is an alternate form share a single construction.
/// </summary>
internal static class AffixProcessGraphBuilder
{
    /// <summary>Requires every phoneme and natural class the pattern names to be usable in a process.</summary>
    internal static void RequireReferences(LcmCache cache, IReadOnlyList<AffixProcessInputPart> input,
        IReadOnlyList<AffixProcessOutputStep> output)
    {
        foreach (var part in input)
        {
            if (part.Phoneme is { } phone) AffixProcessSemanticValidator.RequirePhoneme(cache, phone);
            if (part.NaturalClass is { } naturalClass) AffixProcessSemanticValidator.RequireNaturalClass(cache, naturalClass);
        }
        foreach (var phone in output.Where(step => step.Insert is not null).SelectMany(step => step.Insert!))
            AffixProcessSemanticValidator.RequirePhoneme(cache, phone);
    }

    /// <summary>
    /// Appends the process's input and output operations to <paramref name="operations"/>. <paramref name="creates"/>
    /// maps each already-created identity, the process included, to the operation that creates it, and gains
    /// every part this adds.
    /// </summary>
    internal static void Append(List<OperationEnvelope> operations, Func<CanonicalId> mint,
        Dictionary<CanonicalId, CanonicalId> creates, CanonicalId process, IReadOnlyList<AffixProcessInputPart> input,
        IReadOnlyList<AffixProcessOutputStep> output, string rationale)
    {
        var parts = new Dictionary<string, CanonicalId>(StringComparer.Ordinal);
        CanonicalId? priorInput = null;
        foreach (var part in input)
        {
            var concreteClass = part.Remaining ? "PhVariable" : part.Phoneme is not null ? "PhSimpleContextSeg" : "PhSimpleContextNC";
            var id = Create(MoAffixProcessInputOperationKinds.Create, concreteClass, priorInput);
            parts.Add(part.Name, id);
            if (part.Phoneme is { } phone)
                Add(PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, id, new { @ref = phone.Value });
            if (part.NaturalClass is { } naturalClass)
                Add(PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, id, new { @ref = naturalClass.Value });
            priorInput = id;
        }
        CanonicalId? priorOutput = null;
        foreach (var step in output)
        {
            if (step.Copy is { } name)
            {
                var id = Create(MoAffixProcessOutputOperationKinds.Create, "MoCopyFromInput", priorOutput);
                Add(MoCopyFromInputContentOperationKinds.SetContent, id, new { @ref = parts[name].Value }, parts[name]);
                priorOutput = id;
            }
            else foreach (var phone in step.Insert!)
            {
                var id = Create(MoAffixProcessOutputOperationKinds.Create, "MoInsertPhones", priorOutput);
                Add(MoInsertPhonesContentOperationKinds.AddRefContent, id, new { member = phone.Value });
                priorOutput = id;
            }
        }

        CanonicalId Create(string kind, string concreteClass, CanonicalId? prior)
        {
            var id = mint();
            var operation = Add(kind, process, new { @class = concreteClass }, prior, id,
                prior is { } previous ? new Placement(previous, null) : null);
            creates.Add(id, operation.OperationId);
            return id;
        }
        OperationEnvelope Add(string kind, CanonicalId target, object after, CanonicalId? referenced = null,
            CanonicalId? entity = null, Placement? placement = null)
        {
            var dependencies = new[] { (CanonicalId?)target, referenced }
                .Where(id => id is not null && creates.ContainsKey(id.Value))
                .Select(id => new OperationDependency(creates[id!.Value])).Distinct().ToArray();
            var operation = new OperationEnvelope(mint(), kind, entityId: entity, target: target,
                after: JsonSerializer.SerializeToElement(after), placement: placement, dependsOn: dependencies,
                rationale: rationale);
            operations.Add(operation);
            return operation;
        }
    }
}
