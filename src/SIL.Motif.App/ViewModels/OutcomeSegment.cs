using System.Globalization;
using System.Windows.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>One part of a whole, such as the words that parsed, drawn in its mark's colour.</summary>
public sealed record OutcomeSegment(Mark Mark, int Count, string Label)
{
    /// <summary>The count as the legend prints it, grouped for reading.</summary>
    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The exact Matrix cell or cells this part opens, when it is a link.</summary>
    public ICommand? Command { get; init; }

    /// <summary>The accessible name of this part's link.</summary>
    public string? ActionName { get; init; }
}
