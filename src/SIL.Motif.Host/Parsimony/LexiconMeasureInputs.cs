namespace SIL.Motif.Host.Parsimony;

/// <summary>The stem MSAs that name no part of speech, with the count of every stem MSA.</summary>
/// <param name="StemMsaCount">How many stem MSAs the facts hold.</param>
/// <param name="Missing">The stem MSAs with no part of speech, in no particular order.</param>
internal sealed record StemMsaInput(long StemMsaCount, IReadOnlyList<UncategorisedStemMsa> Missing);

/// <summary>One stem MSA with no part of speech.</summary>
/// <param name="MsaGuid">The MSA.</param>
/// <param name="EntryGuid">The entry that owns it.</param>
/// <param name="EntryHasGlosses">Whether any sense of the entry has a non-blank gloss.</param>
internal sealed record UncategorisedStemMsa(string MsaGuid, string EntryGuid, bool EntryHasGlosses);

/// <summary>The glosses that may echo their entry's form, with the count of every glossed sense.</summary>
/// <param name="GlossedSenses">How many senses have a non-blank gloss.</param>
/// <param name="Candidates">
/// Gloss and allomorph form pairs whose text the query could not rule out as an echo.
/// </param>
internal sealed record GlossEchoInput(long GlossedSenses, IReadOnlyList<GlossEchoCandidate> Candidates);

/// <summary>One gloss of a sense next to one allomorph form of its entry.</summary>
/// <param name="EntryGuid">The entry.</param>
/// <param name="SenseGuid">The sense.</param>
/// <param name="LexemeAllomorphGuid">The entry's lexeme form, or null when it has no allomorph.</param>
/// <param name="Gloss">The raw gloss text.</param>
/// <param name="Form">The raw form text.</param>
internal sealed record GlossEchoCandidate(string EntryGuid, string SenseGuid, string? LexemeAllomorphGuid,
    string Gloss, string Form);

/// <summary>
/// Every lexical entry with the forms, glosses and MSA signatures the duplicate check compares.
/// </summary>
/// <param name="Entries">All entries, in no particular order.</param>
internal sealed record LexiconInput(IReadOnlyList<LexiconEntryFact> Entries);

/// <summary>One lexical entry as the duplicate check reads it.</summary>
/// <param name="EntryGuid">The entry.</param>
/// <param name="MorphType">The entry's lexeme morph type.</param>
/// <param name="LexemeAllomorphGuid">
/// The lexeme form: the entry's last allomorph by ordinal, or null when it has none.
/// </param>
/// <param name="LexemeForms">The forms of the lexeme form, one per writing system, raw.</param>
/// <param name="HasSense">Whether the entry owns any sense, glossed or not.</param>
/// <param name="Glosses">Every gloss of every sense, raw.</param>
/// <param name="MsaSignatures">One signature per MSA: kind, categories with their roles, and slots.</param>
internal sealed record LexiconEntryFact(string EntryGuid, string MorphType, string? LexemeAllomorphGuid,
    IReadOnlyList<LexiconTextFact> LexemeForms, bool HasSense, IReadOnlyList<LexiconTextFact> Glosses,
    IReadOnlyList<string> MsaSignatures);

/// <summary>A piece of text in one writing system.</summary>
internal sealed record LexiconTextFact(string WritingSystem, string Text);
