using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>One sense of an entry as the Baseline holds it.</summary>
/// <param name="SenseId">The sense's portable id.</param>
/// <param name="Outline">FieldWorks' sense number, such as <c>2</c> or <c>2.1</c> for a subsense.</param>
/// <param name="Gloss">The best analysis-language gloss, or null when the sense has none.</param>
/// <param name="GlossWritingSystem">The tag of <paramref name="Gloss"/>, or null when there is no gloss.</param>
/// <param name="Glosses">Every gloss, keyed by writing-system tag.</param>
public sealed record LexicalSenseRead(
    string SenseId, string Outline, string? Gloss, string? GlossWritingSystem, IReadOnlyDictionary<string, string> Glosses);

/// <summary>The answer for one requested id.</summary>
/// <param name="Id">The id that was asked for.</param>
/// <param name="Found">Whether the Baseline holds an entry or sense with that id.</param>
/// <param name="EntryId">The entry read, which is the owner when a sense id was asked for; null when not found.</param>
/// <param name="Headword">The entry's headword, or null when not found or it has none.</param>
/// <param name="SenseId">The sense that was asked for, or null when the id named the entry itself.</param>
/// <param name="Senses">Every sense of the entry, in FieldWorks order with subsenses after their parent.</param>
public sealed record LexicalEntryLookup(
    string Id, bool Found, string? EntryId, string? Headword, string? SenseId, IReadOnlyList<LexicalSenseRead> Senses);

/// <summary>Lexical entries read from the Baseline snapshot, one lookup per requested id, in request order.</summary>
/// <param name="Lookups">One per requested id.</param>
/// <param name="Baseline">The Baseline read.</param>
/// <param name="SourceLastWriteUtc">The FieldWorks save the Baseline represents.</param>
/// <param name="PublishedUtc">When the Baseline was published.</param>
public sealed record LexicalEntryResponse(
    IReadOnlyList<LexicalEntryLookup> Lookups, BaselineToken Baseline, DateTimeOffset SourceLastWriteUtc,
    DateTimeOffset PublishedUtc);
