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
