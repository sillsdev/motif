namespace SIL.Motif.App.ViewModels;

/// <summary>One viewport item on Review changes: a group heading or one change row.</summary>
public sealed class ReviewListEntryViewModel
{
    /// <summary>Creates the page heading entry that scrolls with Review's changes.</summary>
    internal ReviewListEntryViewModel(ReviewPageModel page)
    {
        Page = page;
    }

    /// <summary>Creates one group heading or pending-change entry.</summary>
    internal ReviewListEntryViewModel(ReviewChangeGroupViewModel group, ChangeViewModel? change, bool isLastChange)
    {
        Group = group;
        Change = change;
        IsLastChange = isLastChange;
    }

    /// <summary>The Review page for its heading entry.</summary>
    public ReviewPageModel? Page { get; }

    /// <summary>The group that gives a heading or change row its context.</summary>
    public ReviewChangeGroupViewModel? Group { get; }

    /// <summary>The change shown by this entry, or <see langword="null"/> for a heading.</summary>
    public ChangeViewModel? Change { get; }

    /// <summary>Whether this entry displays the Review page heading and empty-state copy.</summary>
    public bool IsPageHeader => Page is not null;

    /// <summary>Whether this entry displays the group's heading and column labels.</summary>
    public bool IsGroupHeader => Group is not null && Change is null;

    /// <summary>The group shown by a heading entry, or <see langword="null"/> for other entries.</summary>
    public ReviewChangeGroupViewModel? GroupHeader => IsGroupHeader ? Group : null;

    /// <summary>Whether this entry displays a pending change.</summary>
    public bool IsChangeRow => Change is not null;

    /// <summary>This entry for its change-row template, or <see langword="null"/> for other entries.</summary>
    public ReviewListEntryViewModel? ChangeEntry => IsChangeRow ? this : null;

    /// <summary>Whether this is the last change row in its group.</summary>
    public bool IsLastChange { get; }
}
