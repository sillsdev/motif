namespace SIL.Motif.App.ViewModels;

/// <summary>Copy shared by the App's window presenters and views.</summary>
public static class WindowCopy
{
    /// <summary>The prompt shown when the current Baseline has no matching Assessment.</summary>
    public static string NothingParsedSinceRefresh => MotifAppStrings.WindowNothingParsedSinceRefresh;

    /// <summary>The prompt shown while an Assessment is active.</summary>
    public static string ParsingSeeTopRow => MotifAppStrings.WindowParsingSeeTopRow;

    /// <summary>The button label for sending the selected list to AI Handoff.</summary>
    public static string AiHandoffForThisList => MotifAppStrings.WindowAiHandoffForThisList;

    /// <summary>The accessible name for sending ticked words from the selected list to AI Handoff.</summary>
    public static string AiHandoffForTickedWordsInThisList => MotifAppStrings.WindowAiHandoffForTickedWordsAccessibleName;

    /// <summary>The button label for sending one selected word to AI Handoff.</summary>
    public static string AiHandoffForThisWord => MotifAppStrings.WindowAiHandoffForThisWord;

    /// <summary>The button label for sending a selected set of words to AI Handoff.</summary>
    public static string AiHandoffForWordCount(int count) =>
        PluralText.Format(MotifAppStrings.WindowAiHandoffForWordsPattern, count);

    /// <summary>The button label before any words are selected for AI Handoff.</summary>
    public static string AiHandoff => MotifAppStrings.WindowAiHandoff;

    /// <summary>The disabled reason when no word list is selected.</summary>
    public static string ChooseAWordListFirst => MotifAppStrings.WindowChooseAWordListFirst;

    /// <summary>The disabled reason when the selected list has no words.</summary>
    public static string NoWordsInListForHandoff => MotifAppStrings.WindowNoWordsInListForHandoff;

    /// <summary>The disabled reason when an empty list cannot provide words to tick.</summary>
    public static string NoWordsInListToTick => MotifAppStrings.WindowNoWordsInListToTick;

    /// <summary>The disabled reason when no words have been ticked.</summary>
    public static string TickWordsFirst => MotifAppStrings.WindowTickWordsFirst;

    /// <summary>The freshness note shown beside a Try a Word result made against an older save.</summary>
    public static string FieldWorksSavedSince => MotifAppStrings.WindowFieldWorksSavedSince;

    /// <summary>The accessible name for dragging all files from a completed AI Handoff.</summary>
    public static string DragAllAiHandoffFiles => MotifAppStrings.WindowDragAllAiHandoffFilesAccessibleName;
}
