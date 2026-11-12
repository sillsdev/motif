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

/// <summary>Every lexical entry with the forms, senses and MSA signatures the lexicon checks compare.</summary>
/// <param name="Entries">All entries, in no particular order.</param>
internal sealed record LexiconInput(IReadOnlyList<LexiconEntryFact> Entries);

/// <summary>One lexical entry as the lexicon checks read it.</summary>
/// <param name="EntryGuid">The entry.</param>
/// <param name="MorphType">The entry's lexeme morph type.</param>
/// <param name="LexemeAllomorphGuid">The lexeme form: the entry's last allomorph by ordinal, or null when it has none.</param>
/// <param name="LexemeForms">The forms of the lexeme form, one per writing system, raw.</param>
/// <param name="AllForms">The raw form text of every allomorph of the entry.</param>
/// <param name="Senses">The entry's senses with their glosses.</param>
/// <param name="MsaSignatures">One signature per MSA: kind, categories with their roles, and slots. Empty when the MSAs were not read.</param>
internal sealed record LexiconEntryFact(string EntryGuid, string MorphType, string? LexemeAllomorphGuid,
    IReadOnlyList<LexiconTextFact> LexemeForms, IReadOnlyList<string> AllForms,
    IReadOnlyList<LexiconSenseFact> Senses, IReadOnlyList<string> MsaSignatures);

/// <summary>A piece of text in one writing system.</summary>
internal sealed record LexiconTextFact(string WritingSystem, string Text);

/// <summary>One sense and its glosses.</summary>
internal sealed record LexiconSenseFact(string SenseGuid, IReadOnlyList<LexiconTextFact> Glosses);
