using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WindowCopyTests
{
    [Fact]
    public void SharedWindowSentencesKeepTheirApprovedWording()
    {
        Assert.Equal("Nothing parsed since the last Refresh.", WindowCopy.NothingParsedSinceRefresh);
        Assert.Equal("Parsing… see the top row.", WindowCopy.ParsingSeeTopRow);
        Assert.Equal("AI Handoff for this list", WindowCopy.AiHandoffForThisList);
        Assert.Equal("AI Handoff for ticked words in this list", WindowCopy.AiHandoffForTickedWordsInThisList);
        Assert.Equal("AI Handoff for this word", WindowCopy.AiHandoffForThisWord);
        Assert.Equal("AI Handoff", WindowCopy.AiHandoff);
        Assert.Equal("AI Handoff for 12 words", WindowCopy.AiHandoffForWordCount(12));
        Assert.Equal("Choose a word list first.", WindowCopy.ChooseAWordListFirst);
        Assert.Equal("No words in this list to send to AI Handoff.", WindowCopy.NoWordsInListForHandoff);
        Assert.Equal("This word list has no words to tick.", WindowCopy.NoWordsInListToTick);
        Assert.Equal("Tick words first.", WindowCopy.TickWordsFirst);
        Assert.Equal("FieldWorks saved since", WindowCopy.FieldWorksSavedSince);
        Assert.Equal("Drag all AI Handoff files", WindowCopy.DragAllAiHandoffFiles);
    }
}
