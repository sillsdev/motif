using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.HandAnalyses;

namespace SIL.Motif.Contract.HandAnalysis;

/// <summary>Project facts are independently available from the other reads on a pin.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ProjectRead.Available), "available")]
[JsonDerivedType(typeof(ProjectRead.Unavailable), "unavailable")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record ProjectRead
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Available(ProjectFacts Facts) : ProjectRead;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Unavailable(SnapshotStamp Stamp, ReadLimitation Limitation) : ProjectRead;
}

/// <summary>Config comes from the pinned settings. Missing settings carry a limitation and a null Config.</summary>
/// <remarks>Project structure and writing-system facts come from the pinned native records and local comparison data.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectFacts(SnapshotStamp Stamp, HandAnalysisConfig? Config, ImmutableArray<MorphTypeFact> MorphTypes,
    ImmutableArray<WritingSystemFact> WritingSystems, ImmutableArray<CategoryFact> Categories, ImmutableArray<ReadLimitation> Limitations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisConfig(LineMode Mode, ImmutableArray<ConfiguredLine> Lines, bool AddWordGlossesToLexicon);

[JsonConverter(typeof(StrictStringEnumConverter<LineMode>))]
public enum LineMode
{
    [JsonStringEnumMemberName("seven-lines")] SevenLines,
    [JsonStringEnumMemberName("gloss-only")] GlossOnly
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfiguredLine(string Kind, string Ws, bool Visible, int Order);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MorphTypeFact(Guid Id, string SemanticRole, MorphFamily Family,
    HandAnalysisMarkers Markers, int Position);

[JsonConverter(typeof(StrictStringEnumConverter<MorphFamily>))]
public enum MorphFamily
{
    Stem,
    Affix
}

/// <summary>Lossless read facts for rendering and comparing alternatives with the pinned comparer.</summary>
/// <remarks>
/// Role order is null for an absent role. Ldml is the complete decoded XML text, including extensions.
/// LdmlBytes retains the exact imported bytes, including encoding and byte-order mark, when byte fidelity is available.
/// ComparisonDefinition carries the effective collation rules from LDML. ComparisonVersion binds the ICU and
/// collation implementation that produced the keys, and agrees with the issuing Stamp.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WritingSystemFact(string Tag, Guid Id, int? VernacularOrder, int? AnalysisOrder,
    bool RightToLeft, string? Font, string? Keyboard, string Ldml, string ComparisonDefinition, string ComparisonVersion,
    ImmutableArray<byte>? LdmlBytes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CategoryFact(Guid Id, Guid? ParentId, int Order,
    ImmutableDictionary<string, string> Names, ImmutableDictionary<string, string> Abbreviations);
