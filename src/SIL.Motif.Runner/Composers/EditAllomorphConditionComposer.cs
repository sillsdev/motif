using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds condition-reference edits for one existing stem or affix allomorph.</summary>
public static class EditAllomorphConditionComposer
{
    private const string ConstructName = "EditAllomorphCondition";
    private const string Rationale = "Authored by the EditAllomorphCondition composer.";

    /// <summary>Requires an exact current condition list and changes only the selected allomorph's references.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAllomorphConditionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.ExpectedEnvironments);
        ArgumentNullException.ThrowIfNull(intent.Environments);

        var target = ReferenceFieldLowering.Resolve<ICmObject>(cache, intent.Target, ConstructName);
        return intent.Field switch
        {
            AllomorphConditionField.PhoneEnv => BuildPhoneEnv(cache, intent, target),
            AllomorphConditionField.Position when target is IMoAffixAllomorph affix =>
                BuildPosition(cache, intent, affix),
            AllomorphConditionField.Position => throw WrongTarget(intent, "an affix allomorph"),
            _ => throw new InvalidOperationException($"'{ConstructName}': unsupported condition field."),
        };
    }

    private static IReadOnlyList<OperationEnvelope> BuildPhoneEnv(LcmCache cache,
        EditAllomorphConditionIntent intent, ICmObject target)
    {
        var (current, addKind, removeKind) = target switch
        {
            IMoStemAllomorph stem => (ReadPhoneEnv(stem),
                MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
                MoStemAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv),
            IMoAffixAllomorph affix => (ReadPhoneEnv(affix),
                MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
                MoAffixAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv),
            _ => throw WrongTarget(intent, "a stem or affix allomorph"),
        };
        var expected = Set(intent.ExpectedEnvironments, "expectedEnvironments");
        var desired = Set(intent.Environments, "environments");
        RequireExpected(intent.Target, expected, current);
        ValidateEnvironments(cache, desired);

        var existing = current.ToHashSet();
        return existing.Except(desired).OrderBy(IdKey, StringComparer.Ordinal)
            .Select(id => ReferenceOperation(removeKind, intent.Target, id))
            .Concat(desired.Except(existing).OrderBy(IdKey, StringComparer.Ordinal)
                .Select(id => ReferenceOperation(addKind, intent.Target, id)))
            .ToArray();
    }

    private static IReadOnlyList<OperationEnvelope> BuildPosition(LcmCache cache,
        EditAllomorphConditionIntent intent, IMoAffixAllomorph affix)
    {
        var current = affix.PositionRS.Select(item => CanonicalId.FromGuid(item.Guid)).ToArray();
        var expected = Unique(intent.ExpectedEnvironments, "expectedEnvironments");
        var desired = Unique(intent.Environments, "environments");
        RequireExpected(intent.Target, expected, current);
        ValidateEnvironments(cache, desired);

        return OrderedSequenceDiff.Create(current, desired).Select(edit =>
        {
            var kind = edit.Kind switch
            {
                OrderedSequenceEditKind.Add => MoAffixAllomorphPositionOperationKinds.AddRefPosition,
                OrderedSequenceEditKind.Remove => MoAffixAllomorphPositionOperationKinds.RemoveRefPosition,
                OrderedSequenceEditKind.Move => MoAffixAllomorphPositionOperationKinds.MovePosition,
                _ => throw new InvalidOperationException("Unsupported position-sequence edit."),
            };
            return new OperationEnvelope(CanonicalId.Mint(), kind, target: intent.Target,
                after: JsonSerializer.SerializeToElement(new { member = edit.Member.Value }),
                placement: edit.Kind == OrderedSequenceEditKind.Remove ? null : edit.Placement,
                rationale: Rationale);
        }).ToArray();
    }

    private static CanonicalId[] ReadPhoneEnv(IMoStemAllomorph allomorph) =>
        allomorph.PhoneEnvRC.Select(item => CanonicalId.FromGuid(item.Guid))
            .OrderBy(IdKey, StringComparer.Ordinal).ToArray();

    private static CanonicalId[] ReadPhoneEnv(IMoAffixAllomorph allomorph) =>
        allomorph.PhoneEnvRC.Select(item => CanonicalId.FromGuid(item.Guid))
            .OrderBy(IdKey, StringComparer.Ordinal).ToArray();

    private static CanonicalId[] Set(IReadOnlyList<CanonicalId> ids, string name) =>
        Unique(ids, name).OrderBy(IdKey, StringComparer.Ordinal).ToArray();

    private static CanonicalId[] Unique(IReadOnlyList<CanonicalId> ids, string name)
    {
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException($"'{ConstructName}': '{name}' must contain unique identities.");
        return ids.ToArray();
    }

    private static void ValidateEnvironments(LcmCache cache, IReadOnlyList<CanonicalId> ids)
    {
        var environments = cache.LangProject.PhonologicalDataOA.EnvironmentsOS
            .Select(item => CanonicalId.FromGuid(item.Guid)).ToHashSet();
        foreach (var id in ids)
        {
            _ = ReferenceFieldLowering.Resolve<IPhEnvironment>(cache, id, ConstructName);
            if (!environments.Contains(id))
                throw new InvalidOperationException(
                    $"'{ConstructName}': environment '{id.Value}' is not owned by this project's phonological data.");
        }
    }

    private static void RequireExpected(CanonicalId target, IReadOnlyList<CanonicalId> expected,
        IReadOnlyList<CanonicalId> current)
    {
        if (!expected.SequenceEqual(current))
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{target.Value}' condition references differ from the expected list.");
    }

    private static OperationEnvelope ReferenceOperation(string kind, CanonicalId target, CanonicalId member) =>
        new(CanonicalId.Mint(), kind, target: target,
            after: JsonSerializer.SerializeToElement(new { member = member.Value }), rationale: Rationale);

    private static string IdKey(CanonicalId id) => id.Value;

    private static InvalidOperationException WrongTarget(EditAllomorphConditionIntent intent, string expected) =>
        new($"'{ConstructName}': target '{intent.Target.Value}' must be {expected} for field '{intent.Field}'.");
}
