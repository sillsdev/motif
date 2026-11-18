using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.HandAnalyses;

namespace SIL.Motif.Contract.HandAnalysis;

/// <summary>Match the OR of exact type/marker pairs, including a clitic with an empty prefix and postfix.</summary>
/// <remarks>
/// No alternatives match no forms. Absent EntryIds leaves entry selection unrestricted. An empty list matches no entries.
/// Equal mode uses the pinned writing-system comparer. Pattern mode retrieves indexed candidates for the shared matcher.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormQuery(string Ws, string NfdText, ImmutableArray<FormTypeMarker> Alternatives,
    FormMatch Mode, FormOrder Order, ImmutableArray<Guid>? EntryIds = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormTypeMarker(Guid TypeId, HandAnalysisMarkers Markers);

[JsonConverter(typeof(StrictStringEnumConverter<FormMatch>))]
public enum FormMatch
{
    [JsonStringEnumMemberName("equal")] Equal,
    [JsonStringEnumMemberName("pattern")] Pattern
}

[JsonConverter(typeof(StrictStringEnumConverter<FormOrder>))]
public enum FormOrder
{
    [JsonStringEnumMemberName("encounter")] Encounter,
    [JsonStringEnumMemberName("usage")] Usage,
    [JsonStringEnumMemberName("chooser")] Chooser
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormHit(Guid Id, string NativeClass, Guid EntryId, bool IsLexeme, int Position,
    ImmutableDictionary<string, string> Forms, Guid? MorphTypeId, bool IsAbstract, FormPattern? Pattern,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? DistinctAnalysisUseCount,
    string EncounterKey, string DisplayKey, string SortKey,
    ImmutableDictionary<string, string> ComparisonKeys);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormPattern(string Discriminator, string Pattern);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LexiconQuery.ByIds), "by-ids")]
[JsonDerivedType(typeof(LexiconQuery.Search), "search")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record LexiconQuery
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ByIds(ImmutableArray<Guid> EntryIds, ImmutableArray<Guid> SenseIds,
        ImmutableArray<Guid> MsaIds, ImmutableArray<Guid> RefIds) : LexiconQuery;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Search(LexicalField Field, LexicalMatch Match, string Ws, string NfdText) : LexiconQuery;
}

[JsonConverter(typeof(StrictStringEnumConverter<LexicalField>))]
public enum LexicalField
{
    [JsonStringEnumMemberName("form")] Form,
    [JsonStringEnumMemberName("headword")] Headword,
    [JsonStringEnumMemberName("sense")] Sense
}

[JsonConverter(typeof(StrictStringEnumConverter<LexicalMatch>))]
public enum LexicalMatch
{
    [JsonStringEnumMemberName("equal")] Equal,
    [JsonStringEnumMemberName("prefix")] Prefix
}

/// <summary>Native lexical closure, with senses in preorder and forms and references in project list order.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexicalFact(Guid EntryId, ImmutableDictionary<string, string> Headword,
    ImmutableDictionary<string, string> Citation, int Homograph, Guid? LexemeFormId,
    ImmutableArray<Guid> AlternateFormIds, ImmutableArray<SenseFact> Senses, ImmutableArray<MsaFact> Msas,
    ImmutableArray<LexicalReferenceFact> References, ImmutableArray<InflectionalTypeFact> InflectionalTypes,
    ImmutableArray<FormHit> CircumfixPartners);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SenseFact(Guid Id, Guid? ParentId, string Number, ImmutableDictionary<string, string> Gloss,
    ImmutableDictionary<string, string> Definition, Guid? MsaId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MsaFact(Guid Id, string NativeClass, Guid? CategoryId, Guid? FromCategoryId,
    Guid? ToCategoryId, string SemanticEqualityKey);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexicalReferenceFact(Guid Id, Guid OwnerId, string NativeKind, int Order,
    ImmutableArray<HandAnalysisVariantComponent> Components, ImmutableArray<Guid> TypeIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InflectionalTypeFact(Guid Id, ImmutableDictionary<string, string> GlossPrepend,
    ImmutableDictionary<string, string> GlossAppend);
