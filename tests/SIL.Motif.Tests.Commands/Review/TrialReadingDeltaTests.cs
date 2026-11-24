using System.Collections.Immutable;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TrialReadingDeltaTests
{
    private static string Id(int value) => CanonicalId.FromGuid(new Guid(value, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8])).Value;

    private static ReadingSignature Sig(int number, bool stem = true) => new([new ReadingMorpheme(Id(number), Id(900001), [],
        ReadingSenseStatus.Resolved, Id(number + 100), Id(number + 200), null, stem, null,
        [new ReadingFeatureValue(ReadingFeatureRole.Inflection, [Id(7)], [Id(9), Id(8)])])]);

    private static ProposalCompareExports.FormSpec Form(string form, int[] before, int[] after) =>
        new(form, [], []) { BeforeSignatures = [.. before.Select(item => Sig(item))], AfterSignatures = [.. after.Select(item => Sig(item))] };

    private static string Publish(string project, params ProposalCompareExports.FormSpec[] forms)
    {
        var id = "";
        var result = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, located) =>
        {
            id = ProposalCompareExports.AddTrial(database, located, forms);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return id;
    }

    [Fact]
    public void LostGainedChangedAndDoubledAreClassifiedFromTheRetainedEvidence()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("lost", [1], []), Form("gained", [], [2]), Form("changed", [3], [4]),
            Form("doubled", [5], [5, 6]), Form("partial", [7, 7], [7]), Form("same", [8], [8]));

        var result = TrialReadingDelta.Read(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var delta = result.Value!;
        Assert.Equal(new ReadingDeltaCounts(6, 5, 1, 1, 1, 1, 0, 3, 3, 0), delta.Counts);
        Assert.Equal(["lost"], delta.Classes.Single(item => item.Class == "lost").Rows.Select(row => row.Word));
        Assert.Equal(["doubled"], delta.Classes.Single(item => item.Class == "doubled").Rows.Select(row => row.Word));
        var changed = delta.Classes.Single(item => item.Class == "changed").Rows.Single();
        Assert.Equal(Sig(3).Id, changed.Retired.Single().Id);
        Assert.Equal(Sig(4).Id, changed.Added.Single().Id);
        Assert.StartsWith("case/", changed.CaseKey);
    }

    [Fact]
    public void ManifestsMatchTheLaneFormatAndEnumerateTheWholeDelta()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = Publish(fixture.Project, Form("lost", [1], []), Form("changed", [3], [4]), Form("partial", [7, 7], [7]));
        var directory = Path.Combine(fixture.Root, "manifests");

        var result = TrialReadingDelta.ReadAndWrite(new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal), directory);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var retired = File.ReadAllLines(Path.Combine(directory, "expect-retired.jsonl"));
        Assert.Equal(["changed", "lost", "partial"], retired.Select(line => line[10..line.IndexOf('"', 10)]));
        Assert.Equal(ReadingManifestFormat.Row("partial", Sig(7), 1), retired[2]);
        Assert.EndsWith("\"count\": 1}", retired[2]);
        Assert.Single(File.ReadAllLines(Path.Combine(directory, "expect-added.jsonl")));
        Assert.Equal(3, result.Value!.Manifests[0].Rows);
        Assert.Equal(1, result.Value.Manifests[1].Rows);
        Assert.All(retired, line => Assert.True(ReadingManifestFormat.TryParseRow(line, out _, out _, out _)));
    }

    [Fact]
    public void AReadingWithoutACompleteIdentityRefusesManifestsAndIsCounted()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var partial = new ProposalCompareExports.FormSpec("odd", [], []) { BeforeSignatures = [ProposalCompareExports.Signature(5)], AfterSignatures = [] };
        var proposal = Publish(fixture.Project, partial, Form("lost", [1], []));
        var request = new ReadTrialDeltaRequest(fixture.Project, ProposalId: proposal);

        Assert.Equal(1, TrialReadingDelta.Read(request).Value!.Counts.Unmeasured);
        var refused = TrialReadingDelta.ReadAndWrite(request, Path.Combine(fixture.Root, "none"));

        Assert.Equal(RefusalCodes.TrialDeltaIncomplete, refused.Refusal?.Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "none")));
    }

    [Fact]
    public void RequestsThatNameNoTrialOrAnUnknownClassAreRefused()
    {
        using var fixture = new ReviewAcceptanceFixture();
        Assert.Equal(RefusalCodes.TrialDeltaRequestInvalid, TrialReadingDelta.Read(new(fixture.Project)).Refusal?.Code);
        Assert.Equal(RefusalCodes.TrialDeltaRequestInvalid,
            TrialReadingDelta.Read(new(fixture.Project, JobId: "job/x", Classes: ["nope"])).Refusal?.Code);
    }
}
