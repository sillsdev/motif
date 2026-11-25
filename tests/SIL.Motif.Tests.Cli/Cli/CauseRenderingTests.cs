using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Readings;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>How the cause groups of a corpus forms query are printed, with and without a sub-label.</summary>
public sealed class CauseRenderingTests
{
    private static CauseExample Example(string word) =>
        new(word, [new CauseReading([new CauseMorph("f1", "12345678-aaaa", "sa", Kind: MorphKinds.Stem)])]);

    [Fact]
    public void ASubLabelIsPrintedInBracketsAndAGroupWithoutOneIsNot()
    {
        var response = new CorpusFormsResponse(CorpusFormsResponse.SchemaName, new CorpusSummary("c.tsv", 2, 5, "approximate", false), null, 2, [], null,
            Causes: new CauseReport(true, 2, 5,
            [
                new CauseGroup(ReadingCause.SameSegmentation, false, 1, 3, Example("sa"), ReadingCause.Stems),
                new CauseGroup(ReadingCause.Other, false, 1, 2, Example("ug")),
            ]));
        var text = CorpusFormsText.Render(response);
        Assert.Contains("same-segmentation [stems]: 1 words, 3 tokens; e.g. sa", text);
        Assert.Contains("  other: 1 words, 2 tokens; e.g. ug", text);
    }
}
