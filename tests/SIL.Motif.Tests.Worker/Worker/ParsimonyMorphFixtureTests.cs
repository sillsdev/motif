using Microsoft.Data.Sqlite;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class ParsimonyMorphFixtureTests
{
    [Fact]
    public void MorphologyAnswerManifestsAreIndependentAndCoverEveryReviewMeasure()
    {
        var seed = Seed();
        var expected = new Dictionary<string, (int Eligible, int Findings)>
        {
            ["B-affix-unslotted"] = (7, 1),
            ["R-tmpl-precedence"] = (3, 1),
            ["R-slot-blocking"] = (4, 2),
            ["R-allo-unconditioned"] = (3, 1),
            ["R-env-broad"] = (1, 1),
            ["R-nc-excess"] = (1, 1),
            ["B-affix-null-vs-optional"] = (0, 0),
        };

        foreach (var (measureId, counts) in expected)
        {
            var fixture = ParsimonyMorphFixtureBuilder.For(seed, measureId);
            Assert.Equal(measureId, fixture.Answer.MeasureId);
            Assert.Equal(counts.Eligible, fixture.Answer.EligibleItems);
            Assert.Equal(counts.Findings, fixture.Answer.FindingItems);
            Assert.Equal(new[] { seed.FirstEntryId.ToString(), seed.SecondEntryId.ToString() },
                fixture.Answer.PreservedSeedEntries);
            Assert.NotNull(fixture.Answer.Witnesses);
            Assert.NotNull(fixture.Answer.FindingTargets);
            Assert.NotNull(fixture.Answer.Exclusions);
        }
    }

    [Fact]
    public void RemovedSlotPatchRequiresExactlyOneMatchingMembership()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE msa_slot (msa_guid TEXT, slot_guid TEXT);" +
                                 "INSERT INTO msa_slot VALUES ('msa', 'slot'), ('msa', 'other');";
            create.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        ParsimonyMorphPatch.RemoveMsaSlot(connection, transaction, "msa", "slot");
        Assert.Equal(1, Count(connection, transaction, "msa", "other"));
        Assert.Equal(0, Count(connection, transaction, "msa", "slot"));
        Assert.Throws<InvalidOperationException>(() =>
            ParsimonyMorphPatch.RemoveMsaSlot(connection, transaction, "msa", "slot"));
        transaction.Rollback();
    }

    [Fact]
    public void LoadedZeroAuthoringRefusesWithoutLoaderProof()
    {
        var error = Assert.Throws<NotSupportedException>(ParsimonyMorphPatch.AuthorLoadedZero);
        Assert.Contains("loader", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NaturalClassAnswerKeepsProductiveHeldOutMembersAndNamesLeakSeparately()
    {
        var fixture = ParsimonyMorphFixtureBuilder.For(Seed(), "R-nc-excess");
        var answer = Assert.Single(fixture.Controls, control => control.Name == "held-out-positive-control");

        Assert.Equal(1, answer.EligibleItems);
        Assert.Equal(1, answer.FindingItems);
        Assert.Equal(new[] { "p", "b", "m" }, answer.Witnesses);
        Assert.Equal(new[] { "90000000-0000-0000-0000-000000000002" }, answer.FindingTargets);
        Assert.Equal(new[] { "91000000-0000-0000-0000-000000000014" }, answer.Exclusions);
        Assert.Equal(new[] { "b", "m" }, answer.HeldOutPositiveControls);
        Assert.Equal(new[] { "t" }, answer.ReviewedLeakControls);
        Assert.Equal(new[] { "t" }, answer.ExpectedExcessMembers);

        var untrainedAnswer = Assert.Single(fixture.Controls,
            control => control.Name == "held-out-not-in-training");
        Assert.Equal(new[] { "p" }, untrainedAnswer.Witnesses);
        Assert.Equal(new[] { "b", "m", "t" }, untrainedAnswer.ExpectedExcessMembers);
        Assert.Equal(new[] { "b", "m" }, untrainedAnswer.HeldOutPositiveControls);
        Assert.Equal(new[] { "t" }, untrainedAnswer.ReviewedLeakControls);
    }

    [Fact]
    public void EnvironmentAnswerPinsTheExactContextUniverseLicensedSetAndObservedSet()
    {
        var answer = ParsimonyMorphFixtureBuilder.For(Seed(), "R-env-broad").Answer;

        Assert.Equal(new[] { "p", "b", "t", "s" }, answer.ExpectedContextUniverse);
        Assert.Equal(new[] { "p", "b" }, answer.ExpectedLicensedContexts);
        Assert.Equal(new[] { "p" }, answer.ExpectedObservedContexts);
        Assert.Equal(new[] { "b" }, answer.ExpectedExcessMembers);
    }

    private static SeededProject Seed() => new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty,
        Guid.Empty, 1, 2);

    private static long Count(SqliteConnection connection, SqliteTransaction transaction, string msa, string slot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM msa_slot WHERE msa_guid=$msa AND slot_guid=$slot;";
        command.Parameters.AddWithValue("$msa", msa);
        command.Parameters.AddWithValue("$slot", slot);
        return (long)command.ExecuteScalar()!;
    }
}
