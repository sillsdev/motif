using System.Globalization;

namespace SIL.Motif.App.ViewModels;

// The project chip and project card read the shell's freshness, so dot, words and counts agree.
public sealed partial class WorkspaceShellViewModel
{
    private DateTimeOffset? _projectOpenedAt;

    /// <summary>Notes when the open project was chosen, for the project card's subtitle.</summary>
    private void NoteProjectOpened() => _projectOpenedAt = HasProject ? Context.Clock.GetLocalNow() : null;

    /// <summary>The status in words, or nothing before a project is open, so a name alone is never read as a status.</summary>
    public string ProjectChipStatusText => HasFreshness ? FreshnessLabel : string.Empty;

    /// <summary>The chip's accessible name and tooltip: the project name and its status, so colour never carries it alone.</summary>
    public string ProjectChipAccessibleName => HasFreshness
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectChipNameFormat, ProjectName, FreshnessLabel)
        : ProjectName;

    /// <summary>The time of the last parse in the window's words, or <see langword="null"/> before one.</summary>
    private string? ParsedTimeText => Context.Evidence.Assessment?.CompletedAt is { } at
        ? TimeOfDay(at)
        : null;

    private string TimeOfDay(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, Context.Clock.LocalTimeZone).ToString("t", CultureInfo.CurrentCulture);

    /// <summary>The wide chip's words after the name: the status and, once words were parsed, when.</summary>
    public string ProjectChipDetailText
    {
        get
        {
            if (!HasFreshness) return string.Empty;
            var detail = ParsedTimeText is { } parsed
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectChipJoinFormat, FreshnessLabel,
                    string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectChipParsedFormat, parsed))
                : FreshnessLabel;
            return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectChipDetailFormat, detail);
        }
    }

    /// <summary>The project card's subtitle: what the project is and when it was opened.</summary>
    public string ProjectCardSubtitle => _projectOpenedAt is { } opened
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectCardOpenedFormat, TimeOfDay(opened))
        : string.Empty;

    /// <summary>The status block's second line: when the words were last parsed.</summary>
    public string ProjectCardParsedText => ParsedTimeText is { } parsed
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectCardParsedFormat, parsed)
        : MotifAppStrings.ProjectCardNotParsed;

    /// <summary>The card's Texts to Parse summary: how many of the project's texts are chosen, and the saved set's name.</summary>
    public string ProjectCardTextsSummary
    {
        get
        {
            if (Context.Setup is not { } setup || setup.Selection is not { HasTexts: true } selection ||
                selection.ChosenTextIds.Count == 0) return MotifAppStrings.ProjectCardNoTextsChosen;
            var total = PluralText.Format(MotifAppStrings.ProjectCardTextsCountPattern, selection.TextCount);
            return setup.SavedSelectionName is { } name
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectCardTextsSummaryFormat,
                    selection.ChosenTextIds.Count, total, name)
                : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProjectCardTextsCountFormat,
                    selection.ChosenTextIds.Count, total);
        }
    }

    private void RaiseProjectCard()
    {
        OnPropertyChanged(nameof(ProjectChipStatusText));
        OnPropertyChanged(nameof(ProjectChipAccessibleName));
        OnPropertyChanged(nameof(ProjectChipDetailText));
        OnPropertyChanged(nameof(ProjectCardSubtitle));
        OnPropertyChanged(nameof(ProjectCardParsedText));
        OnPropertyChanged(nameof(ProjectCardTextsSummary));
    }
}
