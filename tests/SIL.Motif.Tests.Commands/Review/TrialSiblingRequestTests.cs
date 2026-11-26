using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TrialSiblingRequestTests
{
    private static OperationEnvelope Operation(string kind, object? after = null) => new(CanonicalId.Mint("op/"), kind,
        after: after is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(after)).RootElement.Clone());

    [Fact]
    public void OnlyAnEntrysFormWritingOperationsContributeNewFormText()
    {
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint("proposal/"), null,
        [
            Operation("lexical/lexEntry/createAlternateForms", new { morphType = "m", ws = "x", text = "-ish" }),
            Operation("lexical/lexEntry/createLexemeForm", new { nested = new { form = "kwa" } }),
            Operation("lexical/lexEntry/setCitationForm", new { text = "cit" }),
            Operation("lexical/lexPronunciation/setForm", new { text = "not-a-form" }),
            Operation("lexical/lexEntry/deleteAlternateForm", new { text = "not-written" }),
            Operation("analysis/langProject/createWordforms", new { ws = "x", text = "word" }),
        ]);

        Assert.Equal(["-ish", "kwa", "cit"], TrialSiblingResolver.NewForms(proposal));
    }

    [Theory]
    [InlineData(null, 5, "Next: pass --corpus-types")]
    [InlineData("", 5, "Next: pass --corpus-types")]
    [InlineData("types.tsv", 0, "--siblings-max")]
    public void SiblingsNeedACorpusTypesFileAndAPositiveCap(string? path, int max, string expected)
    {
        var request = new EnqueueTrialRequest("missing.fwdata", "1.0", "proposal/one", Words: ["a"])
            { Siblings = new TrialSiblingsRequest(path, max) };

        var refused = TrialWorkflow.Enqueue(request, Path.Combine(Path.GetTempPath(), "motif-siblings-none"), null,
            BaselineMode.BestEffort, CancellationToken.None);

        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, refused.Refusal!.Code);
        Assert.Contains(expected, refused.Refusal.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiblingsNeedAWordListToAddTo(bool allWords)
    {
        var request = new EnqueueTrialRequest("missing.fwdata", "1.0", "proposal/one", AllWords: allWords)
            { Siblings = new TrialSiblingsRequest("types.tsv") };

        var refused = TrialWorkflow.Enqueue(request, Path.Combine(Path.GetTempPath(), "motif-siblings-none"), null,
            BaselineMode.BestEffort, CancellationToken.None);

        Assert.Equal(RefusalCodes.TrialSiblingsInvalid, refused.Refusal!.Code);
        Assert.Contains("--words", refused.Refusal.Message);
    }
}
