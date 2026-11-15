using SIL.Motif.Host.Texts;

namespace SIL.Motif.App.ViewModels;

/// <summary>The three states a word in the Passage can be in, from the project's stored occurrence status.</summary>
public enum PassageWordState
{
    /// <summary>No analysis is chosen at this place yet; it still needs one.</summary>
    NeedsAnalysis,

    /// <summary>The grammar or project guessed an analysis here that nobody has approved.</summary>
    Guessed,

    /// <summary>A person approved the analysis chosen at this place.</summary>
    Approved,
}

/// <summary>Maps a stored occurrence status onto a Passage state, and decides which places a keep can act on.</summary>
public static class PassageWordStates
{
    public static PassageWordState From(string? status) => status switch
    {
        InterlinearAnalysisStatus.Approved => PassageWordState.Approved,
        InterlinearAnalysisStatus.Unapproved => PassageWordState.Guessed,
        _ => PassageWordState.NeedsAnalysis,
    };

    /// <summary>Whether Tab stops here: a word not yet approved at this place.</summary>
    public static bool IsGap(PassageWordState state) => state != PassageWordState.Approved;
}
