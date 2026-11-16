using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>One generated example: a place in the person's texts that shows the section's grammar at work.</summary>
/// <param name="Place">Where it is, as the texts name it.</param>
/// <param name="Shape">The word as written there.</param>
/// <param name="Reading">The approved reading, as the grammar writes it.</param>
public sealed record GuideExample(string Place, string Shape, string Reading);

/// <summary>
/// The "Examples from Your Texts" block of one section: approved places in the current Context that carry the section's
/// linked grammar. Nothing in the engine joins approved occurrences to a section's links yet, so no
/// source supplies examples, counts, history or disagreeing Proposals, and the block stays hidden: it is never filled
/// with guesses, similar words or a placeholder. When a reviewed query supplies them, <see cref="Supply"/> shows them.
/// </summary>
public sealed partial class GrammarGuideExamplesViewModel : ObservableObject
{
    /// <summary>The examples a reviewed source supplied, or none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    private IReadOnlyList<GuideExample> _examples = [];

    /// <summary>The total the source counted; null when it gave none.</summary>
    [ObservableProperty]
    private int? _approvedCount;

    /// <summary>Whether the block shows: only when a source supplied examples.</summary>
    public bool IsVisible => Examples.Count > 0;

    /// <summary>Shows what a source supplied for the selected section; nothing, or an empty list, hides the block.</summary>
    public void Supply(IReadOnlyList<GuideExample>? examples, int? approvedCount = null)
    {
        Examples = examples ?? [];
        ApprovedCount = Examples.Count == 0 ? null : approvedCount;
    }

    /// <summary>Hides the block, as when the section or project changes.</summary>
    public void Clear() => Supply(null);
}
