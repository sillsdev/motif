using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds one Disabled-field operation for a flat ad hoc prohibition without changing the cache.</summary>
public static class EditAdhocProhibitionComposer
{
    private const string ConstructName = "EditAdhocProhibition";
    private const string Rationale = "Authored by the EditAdhocProhibition composer.";

    /// <summary>
    /// Resolves the exact target and expected value, then returns one unconditional write. A stale
    /// expected value is refused before a Draft receives the operation.
    /// </summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAdhocProhibitionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);

        var target = ReferenceFieldLowering.Resolve<ICmObject>(cache, intent.Target, ConstructName);
        if (target is not IMoAlloAdhocProhib and not IMoMorphAdhocProhib)
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' is not an ad hoc prohibition. " +
                "Only flat allomorph and morpheme prohibitions can be edited.");

        var prohibition = (IMoAdhocProhib)target;
        if (prohibition.Disabled != intent.ExpectedDisabled)
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' has Disabled={prohibition.Disabled}; " +
                $"the intent expected Disabled={intent.ExpectedDisabled}.");

        var operation = new OperationEnvelope(
            operationId: CanonicalId.Mint(),
            kind: MoAdhocProhibDisabledOperationKinds.SetDisabled,
            target: intent.Target,
            after: JsonSerializer.SerializeToElement(new { value = intent.Disabled }),
            rationale: Rationale);
        return [operation];
    }
}
