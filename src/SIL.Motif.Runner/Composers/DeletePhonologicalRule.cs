using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Deletes one phonological rule. Disabling is the usual way to retire a rule; deletion is final.</summary>
/// <param name="Target">Canonical id of an existing phonological rule.</param>
/// <param name="ExpectedName">The name the rule must still carry, so the wrong rule is never deleted.</param>
public sealed record DeletePhonologicalRuleIntent(CanonicalId Target, string ExpectedName);

/// <summary>Parses the closed JSON intent for deleting a rule.</summary>
public static class DeletePhonologicalRuleIntentParser
{
    /// <summary>Reads the target and the expected name.</summary>
    public static DeletePhonologicalRuleIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "DeletePhonologicalRule", "target", "expectedName");
        return new(SoundSystemIntentParsing.Id(authored, "target"), SoundSystemIntentParsing.Text(authored, "expectedName"));
    }
}

/// <summary>Builds the deletion after the name guard and a check that nothing else refers to the rule.</summary>
public static class DeletePhonologicalRuleComposer
{
    private const string ConstructName = "DeletePhonologicalRule";

    /// <summary>Returns the deletion, or throws on a stale name or a rule that something still uses.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, DeletePhonologicalRuleIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var rule = LexicalFormAuthoring.Resolve<IPhSegmentRule>(cache, intent.Target, ConstructName);
        var name = rule.Name.BestAnalysisAlternative.Text;
        if (!string.Equals(name, intent.ExpectedName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"'{ConstructName}': '{intent.Target.Value}' is named '{name}', not '{intent.ExpectedName}'.");
        var users = rule.ReferringObjects.Where(user => !ReferenceEquals(user, rule) && !Owns(rule, user)).ToArray();
        if (users.Length > 0)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{name}' is still used by a {users[0].ClassName} ({users[0].Guid:D}); remove that use first, " +
                "or disable the rule instead.");
        return [new OperationEnvelope(CanonicalId.Mint(), PhonologicalRuleDeleteOperationKinds.DeletePhonRules,
            target: intent.Target, after: JsonSerializer.SerializeToElement(new { }),
            rationale: "Authored by the DeletePhonologicalRule composer.")];
    }

    private static bool Owns(ICmObject owner, ICmObject value)
    {
        for (var current = value.Owner; current is not null; current = current.Owner)
            if (current.Guid == owner.Guid) return true;
        return false;
    }
}
