using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Gdr;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The interlinear examples of one Guide section as aligned monospace grids, read from the section's stored paragraphs.
/// Showing reads only; a block with a problem is shown as far as it can be placed, with its problems underneath.
/// </summary>
public sealed partial class GuideInterlinearViewModel : ObservableObject
{
    /// <summary>The grids, one per block, separated by a blank line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private string _gridText = "";

    /// <summary>What was found wrong with the blocks, one per line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems))]
    private string _problemsText = "";

    /// <summary>Whether <see cref="ProblemsText"/> says anything.</summary>
    public bool HasProblems => ProblemsText.Length > 0;

    /// <summary>Whether the section has an interlinear block to show.</summary>
    public bool IsShown => GridText.Length > 0;

    /// <summary>Shows the blocks in <paramref name="paragraphs"/>, each the text of one stored paragraph.</summary>
    public void Show(IReadOnlyList<string> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var parsed = GuideInterlinear.ParseStored(paragraphs);
        GridText = string.Join("\n\n", parsed.Blocks.Select(block => block.ToGrid()));
        ProblemsText = string.Join("\n", parsed.Diagnostics.Select(item => item.Message));
    }

    /// <summary>Hides the block, as when the section or project changes.</summary>
    public void Clear() => Show([]);
}
