using System.Globalization;

namespace SIL.Motif.App.ViewModels;

/// <summary>What a page's number counts, which says how the delta badge reads aloud.</summary>
public enum PageDeltaKind
{
    /// <summary>Words the Proposal puts in the Same column of the Compare matrix.</summary>
    SameWords,

    /// <summary>Findings in the Proposal's grammar check.</summary>
    Warnings,

    /// <summary>Parser work steps for the same words, as a percentage of today's.</summary>
    WorkPercent,
}

/// <summary>
/// How far the open Proposal moves one of a page's numbers from today's, drawn as a badge beside the page in the
/// sidebar. A change of nothing, or one whose two sides were not both read, is no delta at all.
/// </summary>
public sealed record PageDelta
{
    private PageDelta(int change, PageDeltaKind kind)
    {
        Change = change;
        Kind = kind;
    }

    /// <summary>The signed change: the Proposal's number less today's, never zero.</summary>
    public int Change { get; }

    /// <summary>What the change counts.</summary>
    public PageDeltaKind Kind { get; }

    /// <summary>The change with its sign, such as "+1", "−2" or "+2%"; the minus is U+2212.</summary>
    public string Text
    {
        get
        {
            var sign = Change > 0 ? "+" : "−";
            var number = Math.Abs(Change).ToString("N0", CultureInfo.CurrentCulture);
            return Kind == PageDeltaKind.WorkPercent ? sign + number + "%" : sign + number;
        }
    }

    /// <summary>What the change counts, and whose number it is, for a screen reader.</summary>
    public string AccessibleName
    {
        get
        {
            var amount = Math.Abs(Change);
            return (Kind, Change > 0) switch
            {
                (PageDeltaKind.SameWords, true) => PluralText.Format(MotifAppStrings.PageDeltaSameMorePattern, amount),
                (PageDeltaKind.SameWords, false) => PluralText.Format(MotifAppStrings.PageDeltaSameFewerPattern, amount),
                (PageDeltaKind.Warnings, true) => PluralText.Format(MotifAppStrings.PageDeltaWarningsMorePattern, amount),
                (PageDeltaKind.Warnings, false) => PluralText.Format(MotifAppStrings.PageDeltaWarningsFewerPattern, amount),
                (_, true) => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PageDeltaWorkMoreFormat, amount),
                _ => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PageDeltaWorkFewerFormat, amount),
            };
        }
    }

    /// <summary>The delta for a change, or null when the number is unknown on a side or the change is nothing.</summary>
    public static PageDelta? Of(int? change, PageDeltaKind kind) =>
        change is { } value and not 0 ? new PageDelta(value, kind) : null;
}
