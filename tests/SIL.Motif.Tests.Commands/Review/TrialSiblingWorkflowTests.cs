using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TrialSiblingWorkflowTests
{
    private static string CorpusFile(ReviewAcceptanceFixture fixture)
    {
        var path = Path.Combine(fixture.Root, "types.tsv");
        string[] words = ["kuishe", "kuishi", "kuisha", "unrelated", "other"];
        File.WriteAllLines(path, words.Select((word, index) => word + "\t" + (index % 2 == 0 ? "Approved" : "Open")).Prepend("word\tstate"));
        return path;
    }

    private static string SaveProposal(ReviewAcceptanceFixture fixture, object[] operations)
    {
        var id = CanonicalId.Mint("proposal/").Value;
        var saved = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, _) =>
        {
            ProposalCompareExports.SaveProposal(database, id, JsonSerializer.Serialize(new
            {
                contractVersions = new Dictionary<string, string> { ["lexical"] = "1.0" }, proposalId = id, operations,
            }));
            return CommandOutcome<string>.Success("");
        });
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        return id;
    }

    private static object Operation(string kind, Guid? target, object after) => new
    {
        operationId = CanonicalId.Mint("operation/").Value, kind, target = target is { } t ? CanonicalId.FromGuid(t).Value : null, after,
    };

    [Fact]
    public void TheSampleIsDrawnFromTheFormsAProposalWritesAndTheDroppedExistingFormsAreWarnedAbout()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = SaveProposal(fixture,
        [
            Operation("lexical/lexEntry/createAlternateForms", Guid.NewGuid(), new { morphType = "m", ws = "x", text = "-ish" }),
        ]);
        var request = new EnqueueTrialRequest(fixture.Project, MotifProductVersion.CurrentText, proposal, Words: ["kuisha"])
            { Siblings = new TrialSiblingsRequest(CorpusFile(fixture)) };

        var sample = TrialSiblingResolver.Resolve(request, new HashSet<string> { "kuisha" });

        Assert.True(sample.Succeeded, sample.Refusal?.Message);
        Assert.Equal(["kuishe", "kuishi"], sample.Value!.Words.Order());
        Assert.Equal(["ish"], sample.Value.Forms);
        Assert.Contains("no Baseline", sample.Value.Warning);
        Assert.Contains("warning: no Baseline", sample.Value.Describe());
    }

    [Fact]
    public void AProposalThatWritesNoFormAndNamesNoPatternRefusesWithNone()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = SaveProposal(fixture, [Operation("lexical/lexSense/setGloss", Guid.NewGuid(), new { ws = "en", text = "x" })]);
        var request = new EnqueueTrialRequest(fixture.Project, MotifProductVersion.CurrentText, proposal, Words: ["a"])
            { Siblings = new TrialSiblingsRequest(CorpusFile(fixture)) };

        var refused = TrialSiblingResolver.Resolve(request, new HashSet<string>());

        Assert.Equal(RefusalCodes.TrialSiblingsNone, refused.Refusal!.Code);
    }

    [Fact]
    public void TheSiblingRecordSurvivesTheJobInputRoundTrip()
    {
        var input = new TrialJobInput("{}", null)
        {
            Siblings = new TrialSiblingsInput(new JobWordList("abc", 2), "per-form", 99, 50, ["ish"], "sha", 7, 3)
            {
                Warning = "careful",
                ReferrerForms = 12,
                Referrers = [new SIL.Motif.Contract.Review.TrialReferrer("grammar/id", "MoInflClass", 4)],
            },
        };

        var parsed = TrialJobInput.Parse(JsonSerializer.Serialize(input, SIL.Motif.Contract.MotifJson.CreateOptions())).Siblings!;

        Assert.Equal(input.Siblings.Words, parsed.Words);
        Assert.Equal(["ish"], parsed.TouchedForms);
        Assert.Equal(("per-form", 99L, 50, "careful"), (parsed.Pattern, parsed.Seed, parsed.Max, parsed.Warning));
        Assert.Equal(12, parsed.ReferrerForms);
        Assert.Equal(new SIL.Motif.Contract.Review.TrialReferrer("grammar/id", "MoInflClass", 4), Assert.Single(parsed.Referrers!));
        Assert.DoesNotContain("siblings", JsonSerializer.Serialize(new TrialJobInput("{}", null), SIL.Motif.Contract.MotifJson.CreateOptions()));
    }
}
