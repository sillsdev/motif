using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.HandAnalyses;

/// <summary>Explicit intent for one Occurrence; schema validation precedes project resolution or any future write.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisInput(string Schema, Guid RequestId, HandAnalysisExpected Expected,
    HandAnalysisActor Actor, HandAnalysisIntentSource IntentSource, HandAnalysisAnchor Occurrence,
    HandAnalysisWordform Wordform, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? CategoryId, IReadOnlyList<HandAnalysisPiece> Pieces,
    IReadOnlyList<HandAnalysisCreation> Creations, HandAnalysisWordGloss WordGloss, bool Approve,
    Guid? StartedFromAnalysisId = null, Guid? SelectedAnalysisId = null, string LexiconPolicy = "AnalysisOnly");

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisExpected(Guid ProjectGuid, string HomeRevision,
    IReadOnlyList<string> OccurrenceHeads, string ContextHash, string SegmentationId,
    Guid? AssignmentId = null, bool? ParseIsCurrent = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisActor(string Kind, string? Label = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisIntentSource(string Kind, string? Locator = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HandAnalysisScriptureAnchor), "scripture")]
[JsonDerivedType(typeof(HandAnalysisTextAnchor), "text")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record HandAnalysisAnchor;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisScriptureAnchor(Guid SourceId, string Versification,
    string Anchor, string VerseHash) : HandAnalysisAnchor;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisTextAnchor(Guid TextId, Guid ParagraphId, Guid SegmentId,
    int Index, int WordPosition) : HandAnalysisAnchor;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisWordform(string Ws, string Surface, string CaseForm, Guid? Id = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisMarkers(string Before, string After);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisForm(string Ws, string Text, HandAnalysisMarkers Markers, Guid? MorphTypeId = null);

/// <summary>References are existing portable ids or declared request-local keys, never labels or cache-local HVOs.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisLink(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Entry,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Allomorph, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Sense,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Msa,
    Guid? InflTypeId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisPiece(string PieceKey, HandAnalysisForm Form,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] HandAnalysisLink? Link,
    IReadOnlyDictionary<string, string>? BundleForm = null, string? VariantRef = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisMsaSpec(string Kind, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Guid? CategoryId);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HandAnalysisEntryCreation), "entry")]
[JsonDerivedType(typeof(HandAnalysisSenseCreation), "sense")]
[JsonDerivedType(typeof(HandAnalysisAllomorphCreation), "allomorph")]
[JsonDerivedType(typeof(HandAnalysisVariantCreation), "variant")]
[JsonDerivedType(typeof(HandAnalysisVariantEntryCreation), "variant-entry")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record HandAnalysisCreation(string Key);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisEntryCreation(string Key, string FormKey, string SenseKey, string MsaKey,
    IReadOnlyDictionary<string, string> Forms, Guid MorphTypeId, IReadOnlyDictionary<string, string> Glosses,
    HandAnalysisMsaSpec Msa) : HandAnalysisCreation(Key);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisSenseCreation(string Key, string Entry,
    IReadOnlyDictionary<string, string> Glosses, HandAnalysisMsaChoice Msa) : HandAnalysisCreation(Key);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisAllomorphCreation(string Key, string Entry,
    IReadOnlyDictionary<string, string> Forms, Guid MorphTypeId, string Mismatch,
    string? MsaKey = null) : HandAnalysisCreation(Key);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisVariantCreation(string Key, string Entry, HandAnalysisVariantComponent Component,
    IReadOnlyList<Guid> VariantTypeIds) : HandAnalysisCreation(Key);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisVariantEntryCreation(string Key, string FormKey, string RefKey,
    IReadOnlyDictionary<string, string> Forms, Guid MorphTypeId, HandAnalysisVariantComponent Component,
    IReadOnlyList<Guid> VariantTypeIds) : HandAnalysisCreation(Key);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisVariantComponent(Guid? EntryId = null, Guid? SenseId = null);

/// <summary>A sense chooses existing/request-local grammatical info or specifies one default MSA family.</summary>
[JsonConverter(typeof(HandAnalysisMsaChoiceConverter))]
public sealed record HandAnalysisMsaChoice(string? Reference, HandAnalysisMsaSpec? Definition);

public sealed class HandAnalysisMsaChoiceConverter : JsonConverter<HandAnalysisMsaChoice>
{
    public override HandAnalysisMsaChoice Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? new(reader.GetString(), null) :
        new(null, JsonSerializer.Deserialize<HandAnalysisMsaSpec>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, HandAnalysisMsaChoice value, JsonSerializerOptions options)
    {
        if (value.Reference is not null) writer.WriteStringValue(value.Reference);
        else JsonSerializer.Serialize(writer, value.Definition, options);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(HandAnalysisNoGloss), "none")]
[JsonDerivedType(typeof(HandAnalysisSelectGloss), "select")]
[JsonDerivedType(typeof(HandAnalysisAutoGloss), "auto")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record HandAnalysisWordGloss(IReadOnlyList<string> EnabledWs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisNoGloss(IReadOnlyList<string> EnabledWs) : HandAnalysisWordGloss(EnabledWs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisSelectGloss(IReadOnlyList<string> EnabledWs, Guid Id) : HandAnalysisWordGloss(EnabledWs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisAutoGloss(IReadOnlyList<string> EnabledWs, IReadOnlyDictionary<string, string> Forms,
    Guid? StartedFromId = null) : HandAnalysisWordGloss(EnabledWs);
