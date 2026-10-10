using Microsoft.Data.Sqlite;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.Worker;

internal sealed record ParsimonyMorphFixture(
    ParsimonyMorphFixtureAnswer Answer,
    IReadOnlyList<ParsimonyMorphFixtureAnswer> Controls);

internal sealed record ParsimonyMorphFixtureAnswer(
    string Name,
    string MeasureId,
    int EligibleItems,
    int FindingItems,
    IReadOnlyList<string> Witnesses,
    IReadOnlyList<string> FindingTargets,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> PreservedSeedEntries,
    IReadOnlyList<string> HeldOutPositiveControls,
    IReadOnlyList<string> ReviewedLeakControls,
    IReadOnlyList<string> ExpectedExcessMembers,
    IReadOnlyList<string> ExpectedContextUniverse,
    IReadOnlyList<string> ExpectedLicensedContexts,
    IReadOnlyList<string> ExpectedObservedContexts);

internal static class ParsimonyMorphFixtureBuilder
{
    private static readonly IReadOnlyDictionary<string, ParsimonyMorphFixtureAnswer> Answers =
        new Dictionary<string, ParsimonyMorphFixtureAnswer>(StringComparer.Ordinal)
        {
            ["B-affix-unslotted"] = Make("single-unslotted-affix", "B-affix-unslotted", 7, 1,
                ["20000000-0000-0000-0000-000000000111"],
                ["20000000-0000-0000-0000-000000000111"],
                ["20000000-0000-0000-0000-000000000112"]),
            ["R-tmpl-precedence"] = Make("contradicted-order-with-controls", "R-tmpl-precedence", 3, 1,
                ["20000000-0000-0000-0000-000000000243"],
                ["20000000-0000-0000-0000-000000000231"],
                ["20000000-0000-0000-0000-000000000241", "20000000-0000-0000-0000-000000000242",
                    "20000000-0000-0000-0000-000000000235", "20000000-0000-0000-0000-000000000236"]),
            ["R-slot-blocking"] = Make("required-slot-with-alternative-controls", "R-slot-blocking", 4, 2,
                ["20000000-0000-0000-0000-000000000244", "20000000-0000-0000-0000-000000000245"],
                ["20000000-0000-0000-0000-000000000226", "20000000-0000-0000-0000-000000000228",
                    "20000000-0000-0000-0000-000000000229"],
                ["20000000-0000-0000-0000-000000000241", "20000000-0000-0000-0000-000000000230"]),
            ["R-allo-unconditioned"] = Make("nonfinal-broad-sibling", "R-allo-unconditioned", 3, 1,
                ["30000000-0000-0000-0000-000000000011"],
                ["30000000-0000-0000-0000-000000000011"],
                ["30000000-0000-0000-0000-000000000014", "30000000-0000-0000-0000-000000000015",
                    "30000000-0000-0000-0000-000000000016", "30000000-0000-0000-0000-000000000017"]),
            ["R-env-broad"] = Make("one-extra-context-with-ambiguous-controls", "R-env-broad", 1, 1,
                ["p"], ["90000000-0000-0000-0000-000000000003"],
                ["91000000-0000-0000-0000-000000000015", "91000000-0000-0000-0000-000000000016"],
                expectedExcessMembers: ["b"], expectedContextUniverse: ["p", "b", "t", "s"],
                expectedLicensedContexts: ["p", "b"], expectedObservedContexts: ["p"]),
            ["R-nc-excess"] = Make("unobserved-class-member", "R-nc-excess", 1, 1,
                ["p"], ["90000000-0000-0000-0000-000000000002"],
                ["91000000-0000-0000-0000-000000000014"],
                expectedExcessMembers: ["b"]),
            ["B-affix-null-vs-optional"] = Make("empty-forms-are-not-loaded-zeroes",
                "B-affix-null-vs-optional", 0, 0, [], [],
                ["30000000-0000-0000-0000-000000000302", "30000000-0000-0000-0000-000000000312",
                    "30000000-0000-0000-0000-000000000322", "30000000-0000-0000-0000-000000000332",
                    "30000000-0000-0000-0000-000000000342", "30000000-0000-0000-0000-000000000352",
                    "30000000-0000-0000-0000-000000000362"]),
        };

    public static ParsimonyMorphFixture For(SeededProject seed, string measureId)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (!Answers.TryGetValue(measureId, out var answer))
            throw new ArgumentOutOfRangeException(nameof(measureId), measureId,
                "No hand-written morphology answer manifest exists for this measure.");

        var preservedEntries = Array.AsReadOnly(new[]
        {
            seed.FirstEntryId.ToString(),
            seed.SecondEntryId.ToString(),
        });
        answer = answer with { PreservedSeedEntries = preservedEntries };
        IReadOnlyList<ParsimonyMorphFixtureAnswer> controls = measureId == "R-nc-excess"
            ? Array.AsReadOnly(new[]
            {
                Make("held-out-not-in-training", "R-nc-excess", 1, 1,
                    ["p"], ["90000000-0000-0000-0000-000000000002"],
                    ["91000000-0000-0000-0000-000000000014"],
                    preservedSeedEntries: preservedEntries,
                    heldOutPositiveControls: ["b", "m"], reviewedLeakControls: ["t"],
                    expectedExcessMembers: ["b", "m", "t"]),
                Make("held-out-positive-control", "R-nc-excess", 1, 1,
                    ["p", "b", "m"], ["90000000-0000-0000-0000-000000000002"],
                    ["91000000-0000-0000-0000-000000000014"],
                    preservedSeedEntries: preservedEntries,
                    heldOutPositiveControls: ["b", "m"], reviewedLeakControls: ["t"],
                    expectedExcessMembers: ["t"]),
            })
            : Array.Empty<ParsimonyMorphFixtureAnswer>();
        return new ParsimonyMorphFixture(answer, controls);
    }

    private static ParsimonyMorphFixtureAnswer Make(string name, string measureId, int eligibleItems,
        int findingItems, IReadOnlyList<string> witnesses, IReadOnlyList<string> findingTargets,
        IReadOnlyList<string> exclusions,
        IReadOnlyList<string>? preservedSeedEntries = null, IReadOnlyList<string>? heldOutPositiveControls = null,
        IReadOnlyList<string>? reviewedLeakControls = null, IReadOnlyList<string>? expectedExcessMembers = null,
        IReadOnlyList<string>? expectedContextUniverse = null,
        IReadOnlyList<string>? expectedLicensedContexts = null,
        IReadOnlyList<string>? expectedObservedContexts = null) =>
        new(name, measureId, eligibleItems, findingItems,
            Array.AsReadOnly(witnesses.ToArray()), Array.AsReadOnly(findingTargets.ToArray()),
            Array.AsReadOnly(exclusions.ToArray()),
            Array.AsReadOnly((preservedSeedEntries ?? Array.Empty<string>()).ToArray()),
            Array.AsReadOnly((heldOutPositiveControls ?? Array.Empty<string>()).ToArray()),
            Array.AsReadOnly((reviewedLeakControls ?? Array.Empty<string>()).ToArray()),
            Array.AsReadOnly((expectedExcessMembers ?? Array.Empty<string>()).ToArray()),
            Array.AsReadOnly((expectedContextUniverse ?? Array.Empty<string>()).ToArray()),
            Array.AsReadOnly((expectedLicensedContexts ?? Array.Empty<string>()).ToArray()),
            Array.AsReadOnly((expectedObservedContexts ?? Array.Empty<string>()).ToArray()));
}

internal static class ParsimonyMorphPatch
{
    public static void RemoveMsaSlot(SqliteConnection connection, SqliteTransaction transaction,
        string msaGuid, string slotGuid)
    {
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM msa_slot WHERE msa_guid=$msa AND slot_guid=$slot;";
            count.Parameters.AddWithValue("$msa", msaGuid);
            count.Parameters.AddWithValue("$slot", slotGuid);
            if ((long)count.ExecuteScalar()! != 1)
                throw new InvalidOperationException("The removed-slot fixture requires exactly one matching membership.");
        }

        using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM msa_slot WHERE msa_guid=$msa AND slot_guid=$slot;";
        remove.Parameters.AddWithValue("$msa", msaGuid);
        remove.Parameters.AddWithValue("$slot", slotGuid);
        if (remove.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("The removed-slot fixture did not remove exactly one membership.");
    }

    public static void AuthorLoadedZero() => throw new NotSupportedException(
        "A loaded zero cannot be authored until pinned loader evidence proves that the empty form is loaded as zero.");
}
