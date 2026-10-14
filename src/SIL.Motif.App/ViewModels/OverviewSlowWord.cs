namespace SIL.Motif.App.ViewModels;

/// <summary>A recorded slow word, its measured time and any recorded parser outcome.</summary>
/// <param name="Word">The word named in the recorded timing row.</param>
/// <param name="TimeText">Its measured time formatted with the appropriate duration unit.</param>
/// <param name="Outcome">The outcome from the summary or a loaded parse, or null when neither records one.</param>
public sealed record OverviewSlowWord(string Word, string TimeText, Mark? Outcome)
{
    /// <summary>Whether this row has an outcome to show without guessing one from missing data.</summary>
    public bool HasOutcome => Outcome is not null;
}
