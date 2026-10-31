using System.Globalization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One part of some words' parser work: a kind of rule, one rule, or the other work the parser recorded without a
/// named kind. Its share is always of the words' whole work, never of the named kinds' work.
/// </summary>
/// <param name="Label">The kind's or rule's name as the window shows it.</param>
/// <param name="Kind">The stored kind, for a rule; empty for unattributed work.</param>
/// <param name="WorkSteps">The work steps the parser recorded, or null when not recorded.</param>
/// <param name="Share">The share of the words' whole work, or <see langword="null"/> when none was kept.</param>
/// <param name="Words">How many words the parser recorded this part's work in.</param>
/// <param name="Source">The command's row behind it, or <see langword="null"/> for unattributed work.</param>
public sealed record TimingShare(string Label, string Kind, long? WorkSteps, double? Share, int Words,
    TimingAggregateRow? Source)
{
    /// <summary>Whether this row carries the response's unattributed work.</summary>
    public bool IsNotAttributed { get; init; }

    /// <summary>The window's name for this part's stored kind.</summary>
    public string KindLabel => Kind.Length == 0 ? string.Empty : KindName(Kind);

    /// <summary>The work steps with their unit, for a sentence or a legend.</summary>
    public string WorkText => WorkSteps is { } work ? FormatWorkSteps(work) : MotifAppStrings.TimingNotRecorded;

    /// <summary>The work steps as a bare count, for a table column that names the unit.</summary>
    public string WorkCountText => WorkSteps is { } work ? work.ToString("N0", CultureInfo.CurrentCulture) : MotifAppStrings.TimingNotRecorded;

    public string ShareText => Share is { } share ? FormatPercent(share) :
        IsNotAttributed ? MotifAppStrings.TimingNotRecorded : "—";

    public string WordsText => Words.ToString("N0", CultureInfo.CurrentCulture);

    public string CallsText => Source is null ? string.Empty : Source.Calls is { } calls
        ? Source.CallsArePartial
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TimingCallsPartialFormat,
                calls.ToString("N0", CultureInfo.CurrentCulture))
            : PluralText.Format(MotifAppStrings.TimingCallsPattern, (int)calls)
        : MotifAppStrings.TimingNotCounted;

    /// <summary>
    /// The work by kind of rule, largest first, each a share of <paramref name="total"/>, followed by the work the
    /// parser did not attribute to an object.
    /// </summary>
    /// <param name="work">The work steps by object, as the command made them add up to the whole.</param>
    /// <param name="total">The words' whole work steps, which the shares divide.</param>
    /// <param name="kindRows">The command's time rows by kind, for how many words each kind touched.</param>
    /// <param name="unattributedLabel">What the window calls the remainder.</param>
    /// <param name="unattributedWords">How many words the remainder covers.</param>
    /// <param name="alwaysShowUnattributed">Whether to list the remainder when it is zero.</param>
    public static IReadOnlyList<TimingShare> ByKind(IReadOnlyList<TimingWorkAggregate> work, long total,
        IReadOnlyList<TimingAggregateRow> kindRows, string unattributedLabel, int unattributedWords,
        bool alwaysShowUnattributed)
    {
        var kinds = work.Where(row => !row.IsUnattributed).GroupBy(row => row.Kind)
            .Select(group => (Kind: group.Key, Steps: group.Sum(row => row.WorkSteps)))
            .OrderByDescending(kind => kind.Steps).ThenBy(kind => kind.Kind, StringComparer.Ordinal)
            .Select(kind =>
            {
                var source = kindRows.FirstOrDefault(row => row.Key == kind.Kind);
                return new TimingShare(KindName(kind.Kind), kind.Kind, kind.Steps, ShareOf(kind.Steps, total),
                    source?.WordsTouched ?? 0, source);
            }).ToList();
        var other = work.Where(row => row.IsUnattributed).Sum(row => row.WorkSteps);
        if (other > 0 || alwaysShowUnattributed)
            kinds.Add(new TimingShare(unattributedLabel, string.Empty, other, ShareOf(other, total),
                unattributedWords, null) { IsNotAttributed = true });
        return kinds;
    }

    /// <summary>The fraction of <paramref name="total"/> that <paramref name="steps"/> is, or null when there is no work.</summary>
    public static double? ShareOf(long steps, long total) => total > 0 ? (double)steps / total : null;

    /// <summary>Formats a work count with its unit; a median between two whole counts is rounded to a whole count.</summary>
    /// <param name="workSteps">The parser's work steps.</param>
    public static string FormatWorkSteps(double workSteps) =>
        PluralText.Format(MotifAppStrings.WorkStepsPattern, (long)Math.Round(workSteps, MidpointRounding.AwayFromZero));

    /// <summary>Formats recorded times as whole milliseconds, tenths below ten, and seconds from ten seconds.</summary>
    /// <param name="milliseconds">The measured time.</param>
    public static string FormatDuration(double milliseconds)
    {
        var culture = CultureInfo.CurrentCulture;
        if (Math.Round(milliseconds) >= 10_000) return (milliseconds / 1000).ToString("N1", culture) + " s";
        if (milliseconds >= 10) return milliseconds.ToString("N0", culture) + " ms";
        if (milliseconds > 0 && milliseconds < 0.05) return "<0.1 ms";
        return milliseconds.ToString("#,0.#", culture) + " ms";
    }

    /// <summary>Shows whole percentages, with positive shares below one percent shown as less than one percent.</summary>
    /// <param name="share">The fraction of the whole.</param>
    public static string FormatPercent(double share)
    {
        var culture = CultureInfo.CurrentCulture;
        if (share > 0 && share < 0.01) return "<" + 0.01.ToString("P0", culture);
        return share.ToString("P0", culture);
    }

    /// <summary>The window's name for a stored parser kind.</summary>
    public static string KindName(string kind) => kind switch
    {
        "morph_rule" => MotifAppStrings.TimingKindMorphologicalRules,
        "phon_rule" => MotifAppStrings.TimingKindPhonologicalRules,
        "root_index" => MotifAppStrings.TimingKindRootLookup,
        "lex_entry" => MotifAppStrings.TimingKindLexicalEntries,
        _ => kind.Replace('_', ' '),
    };
}

/// <summary>One row of the Timing page's by-rule table, and whether it is the chosen rule.</summary>
/// <param name="Share">The rule's work and its share of the words' whole work.</param>
/// <param name="IsChosen">Whether the side card describes this rule.</param>
public sealed record TimingRuleRow(TimingShare Share, bool IsChosen)
{
    public TimingRuleRow(TimingShare share, bool isChosen, string? warningCode) : this(share, isChosen) =>
        WarningCode = warningCode;

    /// <summary>The rule's timing row, as the command reported it.</summary>
    public TimingAggregateRow Row => Share.Source!;

    public string? WarningCode { get; }

    public bool HasGrammarWarning => WarningCode is not null;

    public static string GrammarWarningTooltip => MotifAppStrings.TimingGrammarWarningTip;

    /// <summary>The rule as the inspector looks it up, by the kind and key the timings record it under.</summary>
    public InspectorSubject InspectSubject => InspectorSubject.Rule(Row.TimingKey, Row.Name,
        Row.IdentityQuality is { Length: > 0 } quality ? quality : MotifAppStrings.TimingUnknownQuality);
}
