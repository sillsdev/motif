using SIL.Motif.Contract.Gdr;
using Xunit;

namespace SIL.Motif.Tests.Contract.Gdr;

public sealed class GuideInterlinearTests
{
    private const string Swahili =
        "::: {.interlinear}\nword: [nilisoma kitabu]{.v}\nmorphemes: [ni-li-som-a kitabu]{.v}\ngloss: 1SG-PST-read-FV book\n" +
        "translation: I read a book.\n:::";

    private const string Tamil =
        "::: {.interlinear}\nword: [நான் புத்தகம் படித்தேன்]{.v}\nmorphemes: [நான் புத்தகம் படி-த்த்-ஏன்]{.v}\n" +
        "gloss: 1SG book read-PST-1SG\ntranslation: I read the book.\n:::";

    [Fact]
    public void ASwahiliBlockParsesIntoAlignedColumns()
    {
        var parsed = GuideInterlinear.Parse("Intro\n\n" + Swahili + "\n\nAfter");
        Assert.Empty(parsed.Diagnostics);
        var block = Assert.Single(parsed.Blocks);
        Assert.Equal("nilisoma kitabu", block.Word);
        Assert.Equal("I read a book.", block.Translation);
        Assert.Equal(2, block.Columns.Count);
        Assert.Equal(["ni", "li", "som", "a"], block.Columns[0].MorphemePieces);
        Assert.Equal(["1SG", "PST", "read", "FV"], block.Columns[0].GlossPieces);
        Assert.True(block.IsAligned);
        Assert.Equal((3, 8), (block.Span.StartLine, block.Span.EndLine));
    }

    [Fact]
    public void ATamilBlockKeepsItsScriptAndAlignsByTokens()
    {
        var block = Assert.Single(GuideInterlinear.Parse(Tamil).Blocks);
        Assert.Equal(["நான்", "புத்தகம்", "படித்தேன்"], block.Columns.Select(column => column.Word));
        Assert.Equal(["படி", "த்த்", "ஏன்"], block.Columns[2].MorphemePieces);
        Assert.Empty(GuideInterlinear.Parse(Tamil).Diagnostics);
        Assert.Contains("புத்தகம்", block.ToGrid(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGridPadsEachColumnToItsWidestCell()
    {
        var grid = Assert.Single(GuideInterlinear.Parse(Swahili).Blocks).ToGrid().Split('\n');
        Assert.Equal(4, grid.Length);
        Assert.Equal(grid[1].IndexOf("kitabu", StringComparison.Ordinal), grid[0].IndexOf("kitabu", StringComparison.Ordinal));
        Assert.Equal(grid[1].IndexOf("kitabu", StringComparison.Ordinal), grid[2].IndexOf("book", StringComparison.Ordinal));
    }

    [Fact]
    public void MismatchedTokenCountsAreReported()
    {
        var parsed = GuideInterlinear.Parse("::: {.interlinear}\nword: a b\nmorphemes: a\ngloss: x y\n:::");
        Assert.Equal(GuideInterlinearDiagnosticKind.TokenCountMismatch, Assert.Single(parsed.Diagnostics).Kind);
        Assert.False(Assert.Single(parsed.Blocks).IsAligned);
    }

    [Fact]
    public void AGlossWithTheWrongNumberOfPiecesIsReported()
    {
        var parsed = GuideInterlinear.Parse("::: {.interlinear}\nword: nilisoma\nmorphemes: ni-li-soma\ngloss: 1SG-read\n:::");
        Assert.Equal(GuideInterlinearDiagnosticKind.GlossMismatch, Assert.Single(parsed.Diagnostics).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("::: {.interlinear}")]
    [InlineData("::: {.interlinear}\nnonsense\nword:\nword: x\nfoo: bar\n:::")]
    [InlineData(":::\n::: {.interlinear}\n:::\n:::")]
    public void BadInputNeverThrows(string? text)
    {
        var parsed = GuideInterlinear.Parse(text);
        Assert.NotNull(parsed.Diagnostics);
        GuideInterlinear.Fold(GuideInterlinear.Expand(text ?? ""));
        GuideInterlinear.ParseStored([text ?? "", "word: a"]);
    }

    [Fact]
    public void AMissingLineAndAnUnclosedBlockAreDiagnosed()
    {
        var parsed = GuideInterlinear.Parse("::: {.interlinear}\nword: a\ngloss: b");
        Assert.Contains(parsed.Diagnostics, item => item.Kind == GuideInterlinearDiagnosticKind.UnclosedBlock);
        Assert.Contains(parsed.Diagnostics, item => item.Kind == GuideInterlinearDiagnosticKind.MissingLine);
        Assert.Empty(parsed.Blocks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpandThenFoldReturnsTheCanonicalBlock(bool tamil)
    {
        var block = tamil ? Tamil : Swahili;
        var expanded = GuideInterlinear.Expand("Before\n\n" + block + "\n\nAfter\n");
        Assert.DoesNotContain(":::", expanded, StringComparison.Ordinal);
        Assert.Contains("\n\nword: ", expanded, StringComparison.Ordinal);
        Assert.Equal("Before\n\n" + block + "\n\nAfter\n", GuideInterlinear.Fold(expanded));
    }

    [Fact]
    public void ABlockThatIsNotWellFormedIsLeftAsWritten()
    {
        const string text = "::: {.interlinear}\nword: a\nmorphemes: b\n:::";
        Assert.Equal(text, GuideInterlinear.Expand(text));
    }

    [Fact]
    public void StoredParagraphsFormABlock()
    {
        var parsed = GuideInterlinear.ParseStored(["Prose", "word: nilisoma", "morphemes: ni-li-soma", "gloss: 1SG-PST-read", "translation: I read.", "More"]);
        var block = Assert.Single(parsed.Blocks);
        Assert.Equal((2, 5), (block.Span.StartLine, block.Span.EndLine));
        Assert.Empty(parsed.Diagnostics);
    }

    [Fact]
    public void ALoneWordParagraphIsProseAndRaisesNothing()
    {
        var parsed = GuideInterlinear.ParseStored(["word: the root is lonely", "More prose"]);
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Blocks);
    }

    [Fact]
    public void AWordAndAGlossParagraphAreARecognisedBlockMissingItsMorphemes()
    {
        var parsed = GuideInterlinear.ParseStored(["word: a", "gloss: b"]);
        Assert.Contains(parsed.Diagnostics, item => item.Kind == GuideInterlinearDiagnosticKind.MissingLine);
    }

    [Fact]
    public void ATamilBlockInDecomposedFormRoundTripsAndPairsUp()
    {
        var block = Tamil.Normalize(System.Text.NormalizationForm.FormD);
        var expanded = GuideInterlinear.Expand("Before\n\n" + block + "\n\nAfter");
        Assert.Equal("Before\n\n" + block + "\n\nAfter", GuideInterlinear.Fold(expanded));
        Assert.Empty(GuideInterlinear.Parse(block).Diagnostics);
    }

    [Fact]
    public void ProseThatMerelyLooksLabelledRoundTripsIdentically()
    {
        const string text = "word: the root is lonely\n\nSome prose.\n\ngloss: not a block\n\nword: again";
        Assert.Equal(text, GuideInterlinear.Fold(GuideInterlinear.Expand(text)));
    }

    [Fact]
    public void AParagraphRunWrappedInAStylingDivStaysPlainLines()
    {
        const string text = "::: {field=x}\nword: a\n\nmorphemes: b\n\ngloss: c\n:::";
        Assert.Equal(text, GuideInterlinear.Fold(text));
        Assert.Equal(text, GuideInterlinear.Expand(text));
    }
}
