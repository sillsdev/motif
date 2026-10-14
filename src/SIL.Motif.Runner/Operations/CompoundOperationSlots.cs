using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

internal static class CompoundOperationSlots
{
    internal static IReadOnlyList<OperationSlot> RetargetBundle(OperationEnvelope operation)
    {
        var target = OperationSlotRegistry.Target(operation);
        return [new OperationSlot(target, SnapshotFields.WfiMorphBundleMorph),
            new OperationSlot(target, SnapshotFields.WfiMorphBundleForm, WholeField: true)];
    }

    internal static IReadOnlyList<OperationSlot> RetargetAdhoc(OperationEnvelope operation)
    {
        var target = OperationSlotRegistry.Target(operation);
        var payload = AdhocRetargetPayload.Parse(OperationSlotRegistry.After(operation));
        var slots = new List<OperationSlot>();
        foreach (var field in payload.Fields)
        {
            var name = field.Field switch
            {
                "FirstAllomorph" => SnapshotFields.MoAlloAdhocProhibFirstAllomorph,
                "RestOfAllos" => SnapshotFields.MoAlloAdhocProhibRestOfAllos,
                "Allomorphs" => SnapshotFields.MoAlloAdhocProhibAllomorphs,
                _ => throw new InvalidOperationException($"Unsupported ad hoc field '{field.Field}'.")
            };
            if (field.Field == "FirstAllomorph")
                slots.Add(new OperationSlot(target, name));
            else
            {
                var changed = field.Before.Zip(field.After).Where(pair => pair.First != pair.Second);
                foreach (var member in changed.SelectMany(pair => new[] { pair.First, pair.Second }).Distinct())
                    slots.Add(new OperationSlot(target, name, OperationSlotRegistry.MemberKey(member)));
            }
        }
        return slots;
    }

    internal static IReadOnlyList<OperationSlot> DeleteAlternateForms(OperationEnvelope operation)
    {
        var target = OperationSlotRegistry.Target(operation);
        var payload = AlternateFormDeletePayload.Parse(OperationSlotRegistry.After(operation));
        return payload.Forms.Select(form =>
            new OperationSlot(target, SnapshotFields.LexEntryAlternateForms, OperationSlotRegistry.MemberKey(form))).ToArray();
    }
}
