using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that every FieldWorks link in the window shows the FieldWorks tool it opens, and that its spoken name
/// says what opens there, so nobody has to click to find out where a link goes.
/// </summary>
public sealed class FieldWorksLinkWordsTests
{
    private const string EntryLink = "silfw://localhost/link?database%3dp%26tool%3dlexiconEdit%26guid%3dx%26tag%3d";
    private const string WordformLink = "silfw://localhost/link?database%3dp%26tool%3dAnalyses%26guid%3dx%26tag%3d";

    [Fact]
    public void AMorphemeLinkShowsItsToolAndNamesTheEntryItOpens()
    {
        var morph = new ParserReadingMorphViewModel(
            new ParserReadingMorph("kat", "cut", "v", null, false, EntryLink) { Entry = "kata" });

        Assert.Equal("Lexicon Edit ↗", morph.LinkText);
        Assert.Equal("Open kata in Lexicon Edit", morph.LinkName);
    }

    [Fact]
    public void AMorphemeWhoseEntryIsUnresolvedIsNamedByItsForm()
    {
        var morph = new ParserReadingMorphViewModel(new ParserReadingMorph("kat", "cut", "v", null, false, EntryLink));

        Assert.Equal("Open the entry for kat in Lexicon Edit", morph.LinkName);
    }

    [Fact]
    public void ALinkWhoseToolMotifDoesNotKnowStillSaysItLeavesForFieldWorks()
    {
        var morph = new ParserReadingMorphViewModel(
            new ParserReadingMorph("kat", "cut", "v", null, false, "silfw://motif.test/project/entry-1"));

        Assert.Equal("FieldWorks ↗", morph.LinkText);
        Assert.Equal("Open the entry for kat in FieldWorks", morph.LinkName);
    }

    [Fact]
    public void AWordLinkShowsWordAnalysesAndNamesTheWord()
    {
        var token = new ResultsTokenViewModel("Text", 1,
            new TextToken("Walikata", "walikata", null, null) { WordLink = WordformLink }, null);

        Assert.Equal("Word Analyses ↗", token.WordLinkText);
        Assert.Equal("Open Walikata in Word Analyses", token.WordLinkName);
    }

    [Fact]
    public void ATraceMorphLinkNamesItsHeadwordAndTool_OrItsFormWhenNoHeadwordIsRecorded()
    {
        var named = new TraceMorphViewModel(Trace("kat", "kata"), allowLiveLink: true);
        var unnamed = new TraceMorphViewModel(Trace("kat", null), allowLiveLink: true);

        Assert.Equal("Open kata in Lexicon Edit ↗", named.LinkText);
        Assert.Equal("Open kata in Lexicon Edit", named.LinkName);
        Assert.Equal("Open the entry for kat in Lexicon Edit", unnamed.LinkName);
    }

    private static TraceMorph Trace(string form, string? headword) =>
        new(null, form, headword, "cut", "v", null, null, null, null, EntryLink);
}
