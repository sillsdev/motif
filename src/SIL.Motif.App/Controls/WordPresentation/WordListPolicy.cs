using System.Collections.Immutable;
using System.Globalization;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>The optional semantic columns a word list may display.</summary>
[Flags]
public enum WordListColumnSet
{
    None = 0,
    Tick = 1,
    FieldWorks = 2,
    PanGloss = 4,
    Meaning = 8,
    Warnings = 16,
    Places = 32,
    Time = 64,
    Read = 128,
    Opinion = 256,
    All = Tick | FieldWorks | PanGloss | Meaning | Warnings | Places | Time | Read,
}

/// <summary>A column in the stable order shared by a word-list header and its rows.</summary>
public enum WordListColumn
{
    Tick,
    Word,
    Opinion,
    FieldWorks,
    PanGloss,
    Meaning,
    Warnings,
    Places,
    Time,
    Read,
    Next,
}

/// <summary>
/// The structural choices for one list. Word and Next are always present; morphology is content within its producer
/// column and is rejected when that producer column is absent.
/// </summary>
public sealed record WordListSchema
{
    private const WordListColumnSet OptionalColumns = WordListColumnSet.Tick | WordListColumnSet.Opinion |
        WordListColumnSet.FieldWorks |
        WordListColumnSet.PanGloss | WordListColumnSet.Meaning | WordListColumnSet.Warnings |
        WordListColumnSet.Places | WordListColumnSet.Time | WordListColumnSet.Read;

    private WordListSchema(
        WordListColumnSet columns,
        bool fieldWorksMorphology,
        bool panGlossMorphology)
    {
        Columns = columns;
        HasFieldWorksMorphology = fieldWorksMorphology;
        HasPanGlossMorphology = panGlossMorphology;
    }

    /// <summary>The optional semantic columns this list enables.</summary>
    public WordListColumnSet Columns { get; }

    /// <summary>Whether the FieldWorks cell contains selectable morphology runs.</summary>
    public bool HasFieldWorksMorphology { get; }

    /// <summary>Whether the PanGloss cell contains selectable morphology runs.</summary>
    public bool HasPanGlossMorphology { get; }

    /// <summary>Creates a schema after validating its columns and morphology producers.</summary>
    public static WordListSchema Create(
        WordListColumnSet columns,
        bool hasFieldWorksMorphology = false,
        bool hasPanGlossMorphology = false)
    {
        if ((columns & ~OptionalColumns) != 0)
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "The list contains an unknown column.");
        if (hasFieldWorksMorphology && !columns.HasFlag(WordListColumnSet.FieldWorks))
            throw new ArgumentException("FieldWorks morphology requires its producer column.", nameof(hasFieldWorksMorphology));
        if (hasPanGlossMorphology && !columns.HasFlag(WordListColumnSet.PanGloss))
            throw new ArgumentException("PanGloss morphology requires its producer column.", nameof(hasPanGlossMorphology));
        return new WordListSchema(columns, hasFieldWorksMorphology, hasPanGlossMorphology);
    }

    /// <summary>Returns enabled columns in display order, including the mandatory word and next-step tracks.</summary>
    public ImmutableArray<WordListColumn> OrderedColumns(bool showsMeaning = true)
    {
        var columns = ImmutableArray.CreateBuilder<WordListColumn>(10);
        Add(WordListColumn.Tick, WordListColumnSet.Tick);
        columns.Add(WordListColumn.Word);
        Add(WordListColumn.Opinion, WordListColumnSet.Opinion);
        Add(WordListColumn.FieldWorks, WordListColumnSet.FieldWorks);
        Add(WordListColumn.PanGloss, WordListColumnSet.PanGloss);
        if (showsMeaning) Add(WordListColumn.Meaning, WordListColumnSet.Meaning);
        Add(WordListColumn.Warnings, WordListColumnSet.Warnings);
        Add(WordListColumn.Places, WordListColumnSet.Places);
        Add(WordListColumn.Time, WordListColumnSet.Time);
        Add(WordListColumn.Read, WordListColumnSet.Read);
        columns.Add(WordListColumn.Next);
        return columns.ToImmutable();

        void Add(WordListColumn column, WordListColumnSet flag)
        {
            if (Columns.HasFlag(flag)) columns.Add(column);
        }
    }
}

/// <summary>Named list schemas used by pages that present words.</summary>
public static class WordListSchemas
{
    /// <summary>The complete word list used by Matrix and Lists.</summary>
    public static readonly WordListSchema All = WordListSchema.Create(
        WordListColumnSet.All, hasFieldWorksMorphology: true, hasPanGlossMorphology: true);

    /// <summary>The word list without its bulk-selection column.</summary>
    public static readonly WordListSchema FixFirst = WordListSchema.Create(
        WordListColumnSet.All & ~WordListColumnSet.Tick, hasFieldWorksMorphology: true, hasPanGlossMorphology: true);

    /// <summary>The results and elapsed time shown for each Timing rule.</summary>
    public static readonly WordListSchema Timing = WordListSchema.Create(
        WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss | WordListColumnSet.Time);

    /// <summary>The elapsed time shown beside Overview's slowest words.</summary>
    public static readonly WordListSchema Overview = WordListSchema.Create(WordListColumnSet.Time);

    /// <summary>The current and proposed analyses shown for a pending Review change.</summary>
    public static readonly WordListSchema Review = WordListSchema.Create(
        WordListColumnSet.Opinion | WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss,
        hasFieldWorksMorphology: true, hasPanGlossMorphology: true);

    /// <summary>The parser result and occurrence count shown for a changed word.</summary>
    public static readonly WordListSchema WhatChanged = WordListSchema.Create(
        WordListColumnSet.PanGloss | WordListColumnSet.Places);

    /// <summary>The source analysis, parser result and occurrence count shown for a warning word.</summary>
    public static readonly WordListSchema Warnings = WordListSchema.Create(
        WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss | WordListColumnSet.Places);

    /// <summary>The word, opinion, morphology, parser result, meaning, places, time and read state.</summary>
    public static readonly WordListSchema WordList = WordListSchema.Create(
        WordListColumnSet.Tick | WordListColumnSet.FieldWorks | WordListColumnSet.PanGloss |
        WordListColumnSet.Meaning | WordListColumnSet.Places | WordListColumnSet.Time | WordListColumnSet.Read,
        hasFieldWorksMorphology: true);
}

/// <summary>Measured heading and bounded track widths for one word-list column.</summary>
internal readonly record struct WordColumnSizing(
    WordListColumn Column,
    double MinimumWidth,
    double PreferredWidth,
    double MaximumWidth,
    double HeadingWidth);

/// <summary>
/// Token bounds, measured headings and stable chrome widths supplied by the list owner for policy resolution.
/// </summary>
internal sealed class WordListSizing
{
    private readonly ImmutableDictionary<WordListColumn, WordColumnSizing> _columns;

    /// <summary>Creates sizing inputs for every column using measured headings and token-defined bounds.</summary>
    public WordListSizing(
        double outerChromeWidth,
        double reservedVerticalScrollbarWidth,
        double columnGap,
        double stackBreakpoint,
        IEnumerable<WordColumnSizing> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        OuterChromeWidth = CheckWidth(outerChromeWidth, nameof(outerChromeWidth));
        ReservedVerticalScrollbarWidth = CheckWidth(reservedVerticalScrollbarWidth, nameof(reservedVerticalScrollbarWidth));
        ColumnGap = CheckWidth(columnGap, nameof(columnGap));
        StackBreakpoint = CheckWidth(stackBreakpoint, nameof(stackBreakpoint));
        var values = columns.ToArray();
        if (values.Length != Enum.GetValues<WordListColumn>().Length ||
            values.Select(column => column.Column).Distinct().Count() != values.Length)
            throw new ArgumentException("Sizing must include each list column exactly once.", nameof(columns));
        foreach (var value in values)
        {
            if (!Enum.IsDefined(value.Column))
                throw new ArgumentOutOfRangeException(nameof(columns), value.Column, "The sizing contains an unknown column.");
            CheckWidth(value.MinimumWidth, nameof(columns));
            CheckWidth(value.PreferredWidth, nameof(columns));
            CheckWidth(value.MaximumWidth, nameof(columns));
            CheckWidth(value.HeadingWidth, nameof(columns));
            if (value.MinimumWidth > value.PreferredWidth || value.PreferredWidth > value.MaximumWidth)
                throw new ArgumentException("A track must satisfy minimum ≤ preferred ≤ maximum.", nameof(columns));
        }
        values = [.. values.OrderBy(column => column.Column)];
        _columns = values.ToImmutableDictionary(column => column.Column);
        Columns = values.ToImmutableArray();
    }

    /// <summary>Chrome subtracted from the parent's logical width before tracks are resolved.</summary>
    public double OuterChromeWidth { get; }

    /// <summary>The stable width reserved for the list's vertical scrollbar.</summary>
    public double ReservedVerticalScrollbarWidth { get; }

    /// <summary>The gap between adjacent data tracks.</summary>
    public double ColumnGap { get; }

    /// <summary>The minimum supported content width below which every row stacks its actions under the word instead of using columns.</summary>
    public double StackBreakpoint { get; }

    /// <summary>The immutable sizing inputs in column order.</summary>
    public ImmutableArray<WordColumnSizing> Columns { get; }

    internal WordColumnSizing For(WordListColumn column) => _columns[column];

    internal bool HasSameValues(WordListSizing other) =>
        OuterChromeWidth.Equals(other.OuterChromeWidth) &&
        ReservedVerticalScrollbarWidth.Equals(other.ReservedVerticalScrollbarWidth) &&
        ColumnGap.Equals(other.ColumnGap) && StackBreakpoint.Equals(other.StackBreakpoint) &&
        Columns.AsSpan().SequenceEqual(other.Columns.AsSpan());

    private static double CheckWidth(double width, string parameter)
    {
        if (!double.IsFinite(width) || width < 0)
            throw new ArgumentOutOfRangeException(parameter, width, "A logical width must be finite and nonnegative.");
        return width;
    }
}

/// <summary>The assigned width of one semantic word-list column.</summary>
internal readonly record struct WordListTrack(WordListColumn Column, double Width);

/// <summary>
/// An immutable list-wide layout shared by its heading and every realized row. It contains no measurements from
/// individual words.
/// </summary>
internal sealed record WordListLayout(
    WordListSchema Schema,
    bool ShowsMeaning,
    ImmutableArray<WordListTrack> Tracks,
    double AvailableWidth,
    double OuterChromeWidth,
    double ReservedVerticalScrollbarWidth,
    double ContentWidth,
    double TrackAreaWidth,
    double ColumnGap,
    bool Stacked,
    long StyleRevision,
    string UiLocale,
    long Revision)
{
    /// <summary>The heading the page gave its Meaning column, which a stacked row uses as that cell's caption.</summary>
    public string MeaningHeader { get; init; } = WordListHeadings.DefaultMeaning;

    /// <summary>Returns the assigned width of a visible column.</summary>
    public double WidthOf(WordListColumn column)
    {
        foreach (var track in Tracks)
            if (track.Column == column) return track.Width;
        throw new KeyNotFoundException($"The layout has no {column} column.");
    }
}

/// <summary>Resolves one bounded layout per distinct list width, schema and text-style context.</summary>
internal sealed class WordListPolicy
{
    private WordListLayout? _lastLayout;
    private WordListSchema? _lastSchema;
    private WordListSizing? _lastSizing;
    private double _lastAvailableWidth;
    private bool _lastShowsMeaning;
    private long _lastStyleRevision;
    private string? _lastUiLocale;
    private string _lastMeaningHeader = WordListHeadings.DefaultMeaning;
    private long _revision;

    /// <summary>Resolves immutable track widths from the parent's slot and module-owned sizing inputs.</summary>
    public WordListLayout Resolve(
        double availableLogicalWidth,
        WordListSchema schema,
        bool showsMeaning,
        long styleRevision,
        string? uiLocale,
        WordListSizing sizing,
        string meaningHeader = WordListHeadings.DefaultMeaning)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(sizing);
        if (!double.IsFinite(availableLogicalWidth) || availableLogicalWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(availableLogicalWidth), availableLogicalWidth,
                "The list owner must provide a finite nonnegative logical width.");
        if (styleRevision < 0) throw new ArgumentOutOfRangeException(nameof(styleRevision));
        var locale = string.IsNullOrWhiteSpace(uiLocale) ? CultureInfo.CurrentUICulture.Name : uiLocale.Trim();
        if (_lastLayout is not null &&
            _lastAvailableWidth.Equals(availableLogicalWidth) && _lastSchema == schema &&
            _lastShowsMeaning == showsMeaning && _lastMeaningHeader == meaningHeader && _lastStyleRevision == styleRevision &&
            string.Equals(_lastUiLocale, locale, StringComparison.Ordinal) && _lastSizing!.HasSameValues(sizing))
            return _lastLayout;

        var columns = schema.OrderedColumns(showsMeaning);
        var contentWidth = Math.Max(0,
            availableLogicalWidth - sizing.OuterChromeWidth - sizing.ReservedVerticalScrollbarWidth);
        var gapWidth = Math.Max(0, columns.Length - 1) * sizing.ColumnGap;
        var trackBudget = Math.Max(0, contentWidth - gapWidth);
        var minimumWidths = columns.ToDictionary(column => column, column => Minimum(sizing.For(column)));
        var minimumTrackWidth = minimumWidths.Values.Sum();
        var stacked = contentWidth < sizing.StackBreakpoint || minimumTrackWidth > trackBudget;
        var widths = columns.ToDictionary(column => column, column => minimumWidths[column]);

        if (!stacked)
        {
            var fixedColumns = columns.Where(column => !HasMorphology(schema, column)).ToArray();
            GrowTowardPreferred(widths, fixedColumns, trackBudget, sizing);
            var morphologyColumns = columns.Where(column => HasMorphology(schema, column)).ToArray();
            DistributeRemaining(widths, morphologyColumns, trackBudget - widths.Values.Sum(), sizing);
        }

        // A stacked list has no tracks: each row reflows inside the slot instead of scrolling sideways.
        var tracks = ImmutableArray.CreateBuilder<WordListTrack>(stacked ? 0 : columns.Length);
        if (!stacked)
            foreach (var column in columns)
                tracks.Add(new WordListTrack(column, widths[column]));

        var trackAreaWidth = stacked ? contentWidth : Math.Max(contentWidth, widths.Values.Sum() + gapWidth);
        var layout = new WordListLayout(schema, showsMeaning, tracks.MoveToImmutable(), availableLogicalWidth,
            sizing.OuterChromeWidth, sizing.ReservedVerticalScrollbarWidth, contentWidth, trackAreaWidth,
            sizing.ColumnGap, stacked, styleRevision, locale, ++_revision) { MeaningHeader = meaningHeader };
        _lastAvailableWidth = availableLogicalWidth;
        _lastSchema = schema;
        _lastShowsMeaning = showsMeaning;
        _lastMeaningHeader = meaningHeader;
        _lastStyleRevision = styleRevision;
        _lastUiLocale = locale;
        _lastSizing = sizing;
        _lastLayout = layout;
        return layout;
    }

    private static double Minimum(WordColumnSizing sizing) => Math.Max(sizing.MinimumWidth, sizing.HeadingWidth);

    private static double Preferred(WordColumnSizing sizing) =>
        Math.Max(Minimum(sizing), Math.Min(sizing.MaximumWidth, sizing.PreferredWidth));

    private static bool HasMorphology(WordListSchema schema, WordListColumn column) => column switch
    {
        WordListColumn.FieldWorks => schema.HasFieldWorksMorphology,
        WordListColumn.PanGloss => schema.HasPanGlossMorphology,
        _ => false,
    };

    private static void GrowTowardPreferred(
        Dictionary<WordListColumn, double> widths,
        IReadOnlyList<WordListColumn> dataColumns,
        double trackBudget,
        WordListSizing sizing)
    {
        var active = dataColumns.Where(column => widths[column] < Preferred(sizing.For(column))).ToList();
        var remaining = Math.Max(0, trackBudget - widths.Values.Sum());
        while (remaining > 0 && active.Count > 0)
        {
            var share = remaining / active.Count;
            var used = 0d;
            foreach (var column in active.ToArray())
            {
                var capacity = Preferred(sizing.For(column)) - widths[column];
                var addition = Math.Min(share, capacity);
                widths[column] += addition;
                used += addition;
                if (addition >= capacity) active.Remove(column);
            }
            if (used <= 0) break;
            remaining -= used;
        }
    }

    private static void DistributeRemaining(
        Dictionary<WordListColumn, double> widths,
        IReadOnlyList<WordListColumn> morphologyColumns,
        double remaining,
        WordListSizing sizing)
    {
        var active = morphologyColumns.ToList();
        while (remaining > 0 && active.Count > 0)
        {
            var share = remaining / active.Count;
            var used = 0d;
            foreach (var column in active.ToArray())
            {
                var capacity = sizing.For(column).MaximumWidth - widths[column];
                var addition = Math.Min(share, Math.Max(0, capacity));
                widths[column] += addition;
                used += addition;
                if (addition >= capacity) active.Remove(column);
            }
            if (used <= 0) break;
            remaining -= used;
        }
    }
}
