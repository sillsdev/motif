using SIL.Motif.Host.RichText;
using SIL.TextEditing.Markdown;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>When a Rationale or Comment check needs the project, and which reports are findings (ADR 0099).</summary>
public sealed class ProposalTextCheckerTests
{
    private const string Guid = "11111111-2222-4333-8444-555555555555";

    [Theory]
    [InlineData("Why it is right.")]
    [InlineData("Some **bold** and *italic* prose\nover two lines.")]
    [InlineData("See [the guide](https://example.org/guide) for more.")]
    [InlineData("Some <b>raw</b> HTML beside [entry-1](/lexicon/entry-1).")]
    [InlineData("| Form | Gloss |\n| --- | --- |\n| form-1 | gloss-1 |")]
    public void TextWithNoObjectLinkOrProjectAttributeOrWithAMarkupErrorOpensNoBaseline(string text) =>
        Assert.False(ProposalTextChecker.NeedsProject(text));

    [Theory]
    [InlineData("Glosses [entry-1](/lexicon/entry-1) as the corpus does.")]
    [InlineData("Glosses [entry-1](/lexicon/entry-1){guid=" + Guid + "} as the corpus does.")]
    [InlineData("The form [text-1]{ws=qaa-x-one} is new.")]
    [InlineData("A [styled]{custom-style=Emphasis} word.")]
    public void ALinkToAnObjectOrAWritingSystemOrStyleAttributeNeedsTheProject(string text) =>
        Assert.True(ProposalTextChecker.NeedsProject(text));

    [Fact]
    public void AnErrorWhoseIdTheCheckerDoesNotKnowIsAFinding() =>
        Assert.True(ProposalTextChecker.IsFinding(new MarkdownDiagnostic("fwmd.future.unknown", MarkdownSeverity.Error, "x", 0, 1)));

    [Fact]
    public void ASpellingNoteIsNotAFindingEvenAsAnError() =>
        Assert.False(ProposalTextChecker.IsFinding(new MarkdownDiagnostic(MarkdownDiagnosticIds.LinkStalePath, MarkdownSeverity.Error, "x", 0, 1)));

    [Fact]
    public void AWarningIsAFindingOnlyForALinkToADeletedObject()
    {
        Assert.False(ProposalTextChecker.IsFinding(new MarkdownDiagnostic("fwmd.future.unknown", MarkdownSeverity.Warning, "x", 0, 1)));
        Assert.True(ProposalTextChecker.IsFinding(new MarkdownDiagnostic(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "x", 0, 1)));
    }
}
