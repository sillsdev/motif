namespace SIL.Motif.App.ViewModels;

/// <summary>A recorded slow word, its parser work steps and any recorded parser outcome.</summary>
/// <param name="Word">The word named in the recorded timing row.</param>
/// <param name="WorkText">Its work steps with their unit, or a note that none were recorded.</param>
/// <param name="Outcome">The outcome from the summary or a loaded parse, or null when neither records one.</param>
public sealed record OverviewSlowWord(string Word, string WorkText, Mark? Outcome)
{
    /// <summary>Whether this row has an outcome to show without guessing one from missing data.</summary>
    public bool HasOutcome => Outcome is not null;
}
