namespace SIL.Motif.App.ViewModels;

/// <summary>Copy shared by the App's window presenters and views.</summary>
public static class WindowCopy
{
    /// <summary>The prompt shown when the current Baseline has no matching Assessment.</summary>
    public const string NothingParsedSinceRefresh = "Nothing parsed since the last Refresh.";

    /// <summary>The prompt shown while an Assessment is active.</summary>
    public const string ParsingSeeTopRow = "Parsing… see the top row.";

    /// <summary>The button label for sending the selected list to AI Handoff.</summary>
    public const string AiHandoffForThisList = "AI Handoff for this list";

    /// <summary>The accessible name for sending ticked words from the selected list to AI Handoff.</summary>
    public const string AiHandoffForTickedWordsInThisList = "AI Handoff for ticked words in this list";

    /// <summary>The button label for sending one selected word to AI Handoff.</summary>
    public const string AiHandoffForThisWord = "AI Handoff for this word";

    /// <summary>The button label for sending a selected set of words to AI Handoff.</summary>
    public static string AiHandoffForWordCount(int count) => $"AI Handoff for {count:N0} words";

    /// <summary>The button label before any words are selected for AI Handoff.</summary>
    public const string AiHandoff = "AI Handoff";

    /// <summary>The disabled reason when no word list is selected.</summary>
    public const string ChooseAWordListFirst = "Choose a word list first.";

    /// <summary>The disabled reason when the selected list has no words.</summary>
    public const string NoWordsInListForHandoff = "No words in this list to send to AI Handoff.";

    /// <summary>The disabled reason when an empty list cannot provide words to tick.</summary>
    public const string NoWordsInListToTick = "This word list has no words to tick.";

    /// <summary>The disabled reason when no words have been ticked.</summary>
    public const string TickWordsFirst = "Tick words first.";

    /// <summary>The freshness note shown beside a Try a Word result made against an older save.</summary>
    public const string FieldWorksSavedSince = "FieldWorks saved since";

    /// <summary>The accessible name for dragging all files from a completed AI Handoff.</summary>
    public const string DragAllAiHandoffFiles = "Drag all AI Handoff files";
}
