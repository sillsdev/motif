using SIL.Motif.App.Controls;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One FieldWorks analysis with the opinion mark and linked morphemes shown in Analyze texts.</summary>
public sealed class FieldWorksAnalysisDisplayViewModel
{
    /// <summary>Creates the display row from one stored analysis and its current opinion.</summary>
    public FieldWorksAnalysisDisplayViewModel(FieldWorksAnalysisMarking analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        StoredAnalysisId = analysis.StoredAnalysisId;
        Opinion = analysis.Opinion;
        OpinionMarkKind = WindowWords.OpinionOf(analysis.Opinion) switch
        {
            OpinionMarkKind.None => OpinionMarkKind.Unknown,
            var opinion => opinion,
        };
        Morphs = analysis.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
    }

    /// <summary>The stable identity used to remove or change the stored analysis.</summary>
    public string StoredAnalysisId { get; }

    /// <summary>The current FieldWorks opinion for this analysis.</summary>
    public string Opinion { get; }

    /// <summary>The opinion in FieldWorks' own words: Approved, Disapproved or Unknown.</summary>
    public string OpinionLabel => WindowWords.Of(OpinionMarkKind);

    /// <summary>The mark that uses FieldWorks' shape and letter for this opinion.</summary>
    public OpinionMarkKind OpinionMarkKind { get; }

    /// <summary>The morphemes, glosses, and entry links that make up this stored analysis.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
}

/// <summary>One staged analysis change and the pending change that can undo it.</summary>
public sealed class StagedMarkingDisplayViewModel(ChangeViewModel change)
{
    /// <summary>The pending change used by the Undo command.</summary>
    public ChangeViewModel Change { get; } = change ?? throw new ArgumentNullException(nameof(change));

    /// <summary>The short opinion or presence transition shown in the staged strip; an addition says what it adds.</summary>
    public string Transition => Change.StagedTransition is { IsAddition: true } addition
        ? $"Will add as {addition.AfterApply}"
        : Change.StagedTransition.Text;

    /// <summary>The current fit message, when the project has been checked.</summary>
    public string FitStatus => Change.FitStatus;

    /// <summary>Whether the pending change needs another check.</summary>
    public bool IsUncertain => Change.IsUncertain;

    /// <summary>Whether the pending change no longer fits the project.</summary>
    public bool NoLongerFits => Change.IsNoLongerFits;
}
