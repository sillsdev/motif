using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds one Disabled-field operation for a compound rule without changing the cache.</summary>
public static class EditCompoundRuleComposer
{
    private const string ConstructName = "EditCompoundRule";
    private const string Rationale = "Authored by the EditCompoundRule composer.";

    /// <summary>
    /// Resolves the exact rule and expected value, then returns one unconditional write. A stale expected
    /// value is refused before a Draft receives the operation.
    /// </summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditCompoundRuleIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);

        var target = ReferenceFieldLowering.Resolve<ICmObject>(cache, intent.Target, ConstructName);
        if (target is not IMoCompoundRule rule)
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' is not a compound rule. " +
                "Only an endocentric or exocentric compound rule can be turned off or on here.");

        if (rule.Disabled != intent.ExpectedDisabled)
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' has Disabled={rule.Disabled}; " +
                $"the intent expected Disabled={intent.ExpectedDisabled}.");

        var operation = new OperationEnvelope(
            operationId: CanonicalId.Mint(),
            kind: MoCompoundRuleDisabledOperationKinds.SetDisabled,
            target: intent.Target,
            after: JsonSerializer.SerializeToElement(new { value = intent.Disabled }),
            rationale: Rationale);
        return [operation];
    }
}
