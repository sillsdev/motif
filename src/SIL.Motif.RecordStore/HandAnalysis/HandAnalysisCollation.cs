using System.Collections.Immutable;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>A writing-system definition exactly as the Home stores it.</summary>
/// <param name="Tag">The BCP 47 tag, which is also the key of every multilingual text alternative.</param>
/// <param name="Id">The identity of the stored definition record.</param>
/// <param name="LdmlBytes">The imported LDML bytes, including encoding and byte-order mark.</param>
/// <param name="CollationBinding">Digest of the definition's collation element, recorded at import.</param>
internal sealed record WsDefinition(string Tag, Guid Id, byte[] LdmlBytes, string CollationBinding);

/// <summary>
/// Maps a form to the key whose equality is collation equivalence within its writing system. Identity
/// stays exact NFD text elsewhere; only form lookup and allomorph reuse go through this seam.
/// </summary>
internal interface IFormCollation
{
    /// <summary>Names the exact ICU, collation implementation and definitions that produced the keys.</summary>
    string Version { get; }

    /// <summary>The equivalence key of NFD text in a writing system; equal keys mean equal collation.</summary>
    string Key(string ws, string nfdText);
}

/// <summary>Builds the comparer for the writing systems stored at one published point.</summary>
internal delegate IFormCollation FormCollationFactory(ImmutableArray<WsDefinition> systems);

/// <summary>Compares NFD text ordinally. Used when the host was given no ICU comparer.</summary>
internal sealed class OrdinalFormCollation : IFormCollation
{
    public static readonly OrdinalFormCollation Instance = new();
    public static readonly FormCollationFactory Factory = _ => Instance;
    public string Version => HandAnalysisHost.ComparisonVersionNone;
    public string Key(string ws, string nfdText) => nfdText;
}
