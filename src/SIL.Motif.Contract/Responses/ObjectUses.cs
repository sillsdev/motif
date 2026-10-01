namespace SIL.Motif.Contract.Responses;

/// <summary>
/// A FieldWorks or parser object to look up by identity: a morpheme's allomorph and grammatical info, and the kind
/// and key PanGloss's statistics time it under. Its label and gloss are for display and are never matched.
/// </summary>
/// <remarks>
/// A stored analysis uses the ref when its morph names every id the ref gives: both ids name one morpheme, the
/// grammatical info alone names every allomorph of it, and the allomorph alone names that form in any role. A ref
/// that gives neither id names no morpheme, such as a rule; one without a timing key names nothing Timing records.
/// </remarks>
public sealed record ObjectUseRef
{
    /// <summary>The GUID of the allomorph, or <see langword="null"/> to match any.</summary>
    public string? AllomorphId { get; init; }

    /// <summary>The GUID of the grammatical info, or <see langword="null"/> to match any.</summary>
    public string? GrammaticalInfoId { get; init; }

    /// <summary>The object kind in PanGloss's statistics, such as <c>phon_rule</c>, <c>morph_rule</c> or <c>lex_entry</c>.</summary>
    public string? TimingKind { get; init; }

    /// <summary>The object's key in PanGloss's statistics: its FieldWorks GUID when that identity is authored.</summary>
    public string? TimingKey { get; init; }

    /// <summary>What the window calls the object, such as <c>kat</c>; display only.</summary>
    public string? Label { get; init; }

    /// <summary>A morpheme's gloss; display only.</summary>
    public string? Gloss { get; init; }

    /// <summary>
    /// The ref for one morph of a reading: its allomorph and grammatical info. It carries no timing key, because
    /// PanGloss times an affix under its grammatical info and a stem under its entry, and a reading's morph names
    /// neither its morph type nor its entry.
    /// </summary>
    public static ObjectUseRef ForMorpheme(ParserReadingMorph morph)
    {
        ArgumentNullException.ThrowIfNull(morph);
        return new ObjectUseRef
        {
            AllomorphId = morph.AllomorphId,
            GrammaticalInfoId = morph.GrammaticalInfoId,
            Label = morph.Form,
            Gloss = morph.Gloss,
        };
    }

    /// <summary>The ref for a name in a trace reading, by the key PanGloss's statistics time it under.</summary>
    public static ObjectUseRef ForTimingKey(TraceTimingKey key, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ObjectUseRef { TimingKind = key.Kind, TimingKey = key.Key, Label = label };
    }
}

/// <summary>
/// What the stored Assessment says about an object, a set of words, or both: the Selection's words whose stored
/// analyses use the object, the words it ran in, and the morphemes the set of words shares.
/// </summary>
/// <param name="AssessmentId">The stored Assessment the words and timings come from.</param>
public sealed record ObjectUsesResponse(string AssessmentId)
{
    /// <summary>Whether FieldWorks has been saved since the Baseline this Assessment measured.</summary>
    public bool IsStale { get; init; }

    /// <summary>The object asked about, or <see langword="null"/> when only a set of words was.</summary>
    public ObjectUseRef? Ref { get; init; }

    /// <summary>
    /// The Selection's words with a stored analysis, other than a disapproved one, that uses the object;
    /// <see langword="null"/> when the ref names no allomorph or grammatical info.
    /// </summary>
    public ObjectUseWords? Uses { get; init; }

    /// <summary>
    /// The Selection's words the object ran in, from the stored per-word timings, with its calls and self time in
    /// each; <see langword="null"/> when the ref has no timing key.
    /// </summary>
    public ObjectUseWords? RanIn { get; init; }

    /// <summary>
    /// The morphemes at least two of the asked-about words use, most words first;
    /// <see langword="null"/> when no set of words was asked about.
    /// </summary>
    public IReadOnlyList<SharedMorpheme>? Shared { get; init; }

    /// <summary>The asked-about words the Assessment does not hold, as asked for.</summary>
    public IReadOnlyList<string> UnknownWords { get; init; } = [];
}

/// <summary>Some of the Selection's words, in Selection order, and how many of them each meaning holds.</summary>
/// <param name="Words">The words, each with the row every page shows for it.</param>
/// <param name="ByMeaning">The words counted by meaning, most words first.</param>
public sealed record ObjectUseWords(IReadOnlyList<ObjectUseWord> Words, IReadOnlyList<ObjectUseMeaning> ByMeaning);

/// <summary>One word that uses an object, or that the object ran in.</summary>
/// <param name="Row">The word's row, as every page shows it.</param>
public sealed record ObjectUseWord(WordRow Row)
{
    /// <summary>
    /// How many times PanGloss called the object in this word, over every direction it recorded;
    /// <see langword="null"/> for a use, or when PanGloss does not count that kind's calls.
    /// </summary>
    public int? Calls { get; init; }

    /// <summary>
    /// The object's self time in this word, in nanoseconds, over every direction PanGloss timed;
    /// <see langword="null"/> for a use, or when PanGloss does not time that kind.
    /// </summary>
    public long? ElapsedNs { get; init; }
}

/// <summary>How many of some words hold one meaning.</summary>
/// <param name="Meaning">The meaning, in the Matrix's words, such as <c>Lost</c>.</param>
/// <param name="Tone">The tone that meaning takes.</param>
/// <param name="Words">How many of the words hold it.</param>
public sealed record ObjectUseMeaning(string Meaning, WordRowTone Tone, int Words);

/// <summary>A morpheme some words share: one allomorph in one grammatical info, matched by identity.</summary>
/// <param name="Morpheme">The morph as the first word that uses it shows it.</param>
/// <param name="Words">The words that use it, in Selection order.</param>
public sealed record SharedMorpheme(ParserReadingMorph Morpheme, IReadOnlyList<string> Words)
{
    /// <summary>How many words use it.</summary>
    public int Count => Words.Count;
}
