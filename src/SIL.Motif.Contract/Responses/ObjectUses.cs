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
    /// neither its morph type nor its entry. A uses query fills the key from the Baseline's
    /// <see cref="ObjectFacts.TimingKey"/>.
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
    /// <summary>The producing run and measurement time for each word supplying this query's evidence.</summary>
    public IReadOnlyDictionary<string, WordMeasurementOrigin> WordOrigins { get; init; } =
        new Dictionary<string, WordMeasurementOrigin>(StringComparer.Ordinal);

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

    /// <summary>
    /// What the Baseline's copy of the FieldWorks project says about the object; <see langword="null"/> when no
    /// object was asked about, or when the Baseline holds nothing the ref names.
    /// </summary>
    public ObjectFacts? Facts { get; init; }
}

/// <summary>
/// What FieldWorks says about the object a ref names, read from the Baseline: its entry, senses, grammatical info
/// and allomorphs for a morpheme, and its kind and home tool for a rule. Each section is empty or
/// <see langword="null"/> when the object has none, and every FieldWorks destination names its tool.
/// </summary>
public sealed record ObjectFacts
{
    /// <summary>The entry the morpheme belongs to, or <see langword="null"/> for a rule outside the lexicon.</summary>
    public ObjectFactsEntry? Entry { get; init; }

    /// <summary>
    /// The entry's senses that carry the grammatical info, or every sense when the ref names none, in entry order.
    /// </summary>
    public IReadOnlyList<ObjectFactsSense> Senses { get; init; } = [];

    /// <summary>The grammatical info, or <see langword="null"/> when the ref names none and its entry has no one.</summary>
    public ObjectFactsGrammaticalInfo? GrammaticalInfo { get; init; }

    /// <summary>
    /// The entry's allomorphs as Lexicon Edit lists them: its lexeme form first, then its other forms in order.
    /// This is display order, not the order a parser tries them in.
    /// </summary>
    public IReadOnlyList<ObjectFactsAllomorph> Allomorphs { get; init; } = [];

    /// <summary>The rule, when the ref names one PanGloss runs: a phonological rule, a compound rule or an affix.</summary>
    public ObjectFactsRule? Rule { get; init; }

    /// <summary>
    /// The kind and key PanGloss's statistics time the object under: an affix under its grammatical info as
    /// <c>morph_rule</c>, a stem under its entry as <c>lex_entry</c>, a rule under itself; <see langword="null"/> when
    /// that cannot be told, such as an affix allomorph whose entry has several grammatical infos.
    /// </summary>
    public TraceTimingKey? TimingKey { get; init; }
}

/// <summary>A lexical entry, as FieldWorks heads it.</summary>
/// <param name="Id">The entry's GUID.</param>
/// <param name="Headword">The headword FieldWorks shows, with the morph type's markers, such as <c>ja-</c>.</param>
public sealed record ObjectFactsEntry(string Id, string Headword)
{
    /// <summary>The lexeme form's morph type, as FieldWorks names it, such as <c>prefix</c> or <c>root</c>.</summary>
    public string? MorphType { get; init; }

    /// <summary>Where FieldWorks shows the entry.</summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }
}

/// <summary>One sense of an entry.</summary>
/// <param name="Id">The sense's GUID.</param>
/// <param name="Number">The sense's number in its entry, as FieldWorks numbers it, such as <c>1</c> or <c>2.1</c>.</param>
public sealed record ObjectFactsSense(string Id, string Number)
{
    /// <summary>The gloss in the best analysis writing system, or <see langword="null"/> when it has none.</summary>
    public string? Gloss { get; init; }

    /// <summary>The definition in the best analysis writing system, or <see langword="null"/> when it has none.</summary>
    public string? Definition { get; init; }

    /// <summary>Where FieldWorks shows the sense: its entry, in Lexicon Edit.</summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }
}

/// <summary>A morpheme's grammatical info: what kind of morpheme it is, where it attaches, and its features.</summary>
/// <param name="Id">The grammatical info's GUID.</param>
/// <param name="Kind">
/// <c>stem</c>, <c>inflectionalAffix</c>, <c>derivationalAffix</c>, <c>unclassifiedAffix</c> or
/// <c>derivationalStep</c>, after FieldWorks' own kinds of grammatical info; <c>unknown</c> for any other.
/// </param>
public sealed record ObjectFactsGrammaticalInfo(string Id, string Kind)
{
    /// <summary>The category: a stem's own, or the one an affix attaches to.</summary>
    public ObjectFactsNamed? Category { get; init; }

    /// <summary>The category a derivational affix makes, or <see langword="null"/> for any other kind.</summary>
    public ObjectFactsNamed? ResultCategory { get; init; }

    /// <summary>The template slots the morpheme fills, each with the templates that use it.</summary>
    public IReadOnlyList<ObjectFactsSlot> Slots { get; init; } = [];

    /// <summary>
    /// The inflection features a derivational affix needs the word to carry already; <see langword="null"/> when
    /// none. An inflectional affix's needs are its allomorphs' <see cref="ObjectFactsAllomorph.RequiredFeatures"/>.
    /// </summary>
    public ObjectFactsFeatures? RequiredFeatures { get; init; }

    /// <summary>
    /// The inflection features the morpheme gives the word: an inflectional affix's, the ones a derivational affix
    /// makes, or a stem's own; <see langword="null"/> when none.
    /// </summary>
    public ObjectFactsFeatures? AddedFeatures { get; init; }
}

/// <summary>A named FieldWorks object, such as a category or a template, and where FieldWorks shows it.</summary>
/// <param name="Id">The object's GUID.</param>
/// <param name="Name">Its name in the best analysis writing system.</param>
public sealed record ObjectFactsNamed(string Id, string Name)
{
    /// <summary>Where FieldWorks shows it.</summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }
}

/// <summary>A template slot a morpheme fills.</summary>
/// <param name="Id">The slot's GUID.</param>
/// <param name="Name">The slot's name in the best analysis writing system.</param>
public sealed record ObjectFactsSlot(string Id, string Name)
{
    /// <summary>Whether a word may leave the slot empty.</summary>
    public bool Optional { get; init; }

    /// <summary>The templates that place the slot, in project order.</summary>
    public IReadOnlyList<ObjectFactsNamed> Templates { get; init; } = [];

    /// <summary>Where FieldWorks shows the slot: its category, in Category Edit.</summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }
}

/// <summary>A feature structure, in FieldWorks' notation and value by value.</summary>
/// <param name="Notation">The structure in FieldWorks' long form, such as <c>[pol:negative]</c>.</param>
/// <param name="Values">Its closed values, in the structure's order.</param>
public sealed record ObjectFactsFeatures(string Notation, IReadOnlyList<ObjectFactsFeatureValue> Values);

/// <summary>One feature and its value.</summary>
/// <param name="Feature">The feature's name, such as <c>Polarity</c>.</param>
/// <param name="Value">The value's name, such as <c>negative</c>.</param>
public sealed record ObjectFactsFeatureValue(string Feature, string Value)
{
    /// <summary>The value's abbreviation, such as <c>neg</c>, which is how the parser writes it.</summary>
    public string? ValueAbbreviation { get; init; }

    /// <summary>Where FieldWorks defines the feature: Inflection Features.</summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }
}

/// <summary>One allomorph of an entry.</summary>
/// <param name="Id">The allomorph's GUID.</param>
/// <param name="Form">The form with its morph type's markers, as FieldWorks writes it, such as <c>ja-</c>.</param>
public sealed record ObjectFactsAllomorph(string Id, string Form)
{
    /// <summary>The allomorph's morph type, as FieldWorks names it.</summary>
    public string? MorphType { get; init; }

    /// <summary>Whether this is the allomorph the ref names.</summary>
    public bool IsAsked { get; init; }

    /// <summary>
    /// The environments the allomorph is written to occur in, in FieldWorks' notation: its phonological
    /// environments first, then an infix's position environments, each once.
    /// </summary>
    public IReadOnlyList<ObjectFactsEnvironment> Environments { get; init; } = [];

    /// <summary>The inflection features an affix allomorph needs the word to carry already; <see langword="null"/> when none.</summary>
    public ObjectFactsFeatures? RequiredFeatures { get; init; }
}

/// <summary>An environment an allomorph occurs in.</summary>
/// <param name="Id">The environment's GUID.</param>
/// <param name="Notation">The environment exactly as FieldWorks stores it, such as <c>/ _ [C]</c>.</param>
public sealed record ObjectFactsEnvironment(string Id, string Notation)
{
    /// <summary>Where FieldWorks shows it: Environments.</summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }
}

/// <summary>A rule PanGloss runs, and the FieldWorks tool that edits it.</summary>
/// <param name="Id">The rule's GUID: an affix's is its grammatical info's.</param>
/// <param name="Kind"><c>affixRule</c>, <c>phonologicalRule</c> or <c>compoundRule</c>, as object facts classify them.</param>
/// <param name="Name">The rule's name, or an affix's headword and gloss.</param>
public sealed record ObjectFactsRule(string Id, string Kind, string Name)
{
    /// <summary>Where FieldWorks edits the rule.</summary>
    public TraceFieldWorksTarget? FieldWorks { get; init; }
}

/// <summary>Some of the Selection's words, in Selection order, and how many of them each meaning holds.</summary>
/// <param name="Words">The words, each with the row every page shows for it.</param>
/// <param name="ByMeaning">The words counted by meaning, most words first.</param>
public sealed record ObjectUseWords(IReadOnlyList<ObjectUseWord> Words, IReadOnlyList<ObjectUseMeaning> ByMeaning)
{
    /// <summary>
    /// How many more of the Selection's words use the object only in an analysis the linguist disapproved, and so
    /// are left out of <see cref="Words"/>; zero for the words an object ran in.
    /// </summary>
    public int NotCountingDisapproved { get; init; }
}

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
