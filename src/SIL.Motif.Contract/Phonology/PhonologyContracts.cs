using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Phonology;

/// <summary>One authored underlying form, its accepted surface forms, generation context, and optional Citation.</summary>
/// <param name="Id">A stable, unique identifier for this example within its revision.</param>
/// <param name="Underlying">The nonblank underlying form, limited to 256 Unicode scalar values.</param>
/// <param name="Expected">One to sixteen distinct accepted forms, each limited to 256 Unicode scalar values.</param>
/// <param name="Context">The closed source-entry context used to generate the underlying form.</param>
/// <param name="Citation">The optional source Citation for this example.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExample(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("underlying")] string Underlying,
    [property: JsonPropertyName("expected")] IReadOnlyList<string> Expected,
    [property: JsonPropertyName("context")] PhonologyGenerationContext Context,
    [property: JsonPropertyName("citation")] CitationView? Citation = null);

/// <summary>Selects the closed source-entry context used by a phonology example.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PhonologyGenerationContext.HcXmlEntry), "hcXmlEntry")]
[JsonDerivedType(typeof(PhonologyGenerationContext.FwdataEntry), "fwdataEntry")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record PhonologyGenerationContext
{
    private PhonologyGenerationContext() { }

    /// <summary>Selects an entry by its identifier in an HC XML source.</summary>
    /// <param name="EntryId">The nonblank HC XML entry identifier.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record HcXmlEntry(
        [property: JsonPropertyName("entryId"), JsonRequired] string EntryId) : PhonologyGenerationContext;

    /// <summary>Selects a FieldWorks entry and its supported stem MSA.</summary>
    /// <param name="EntryGuid">The FieldWorks entry GUID.</param>
    /// <param name="MsaGuid">The GUID of the entry's MSA.</param>
    /// <param name="MsaClass">The supported MSA class.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record FwdataEntry(
        [property: JsonPropertyName("entryGuid"), JsonRequired] string EntryGuid,
        [property: JsonPropertyName("msaGuid"), JsonRequired] string MsaGuid,
        [property: JsonPropertyName("msaClass"), JsonRequired]
        [property: JsonConverter(typeof(PhonologyMsaClassJsonConverter))]
        PhonologyMsaClass MsaClass) : PhonologyGenerationContext;
}

/// <summary>The FieldWorks MSA class accepted for phonology examples.</summary>
[JsonConverter(typeof(PhonologyMsaClassJsonConverter))]
public enum PhonologyMsaClass
{
    /// <summary>A stem MSA.</summary>
    MoStemMsa
}

/// <summary>Reads and writes the one supported FieldWorks MSA class by its named JSON value.</summary>
public sealed class PhonologyMsaClassJsonConverter : JsonConverter<PhonologyMsaClass>
{
    public override PhonologyMsaClass Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !StringComparer.Ordinal.Equals(reader.GetString(), nameof(PhonologyMsaClass.MoStemMsa)))
            throw new JsonException("Unknown phonology MSA class.");
        return PhonologyMsaClass.MoStemMsa;
    }

    public override void Write(Utf8JsonWriter writer, PhonologyMsaClass value,
        JsonSerializerOptions options)
    {
        if (value != PhonologyMsaClass.MoStemMsa)
            throw new JsonException("Unknown phonology MSA class.");
        writer.WriteStringValue(nameof(PhonologyMsaClass.MoStemMsa));
    }
}

/// <summary>Request-wide ceilings balanced across examples during phonology checking.</summary>
/// <param name="StepCap">The total synthesis-step ceiling, divided evenly among rows with remainder units assigned in request order; unused units are not reassigned.</param>
/// <param name="WorkCap">The total shared inner-work ceiling, divided evenly among rows with remainder units assigned in request order; unused units are not reassigned.</param>
/// <param name="TimeoutMs">The shared monotonic deadline in milliseconds, started after grammar loading and shared Morpher/cache construction.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyLimits(
    [property: JsonPropertyName("stepCap")] ulong StepCap,
    [property: JsonPropertyName("workCap")] ulong WorkCap,
    [property: JsonPropertyName("timeoutMs")] uint TimeoutMs);

/// <summary>Continues an example page within one immutable revision.</summary>
/// <param name="OwnerId">The revision id that owns this cursor.</param>
/// <param name="AfterOrdinal">The last example ordinal returned on the preceding page.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyPageCursor(
    [property: JsonPropertyName("ownerId")] string OwnerId,
    [property: JsonPropertyName("afterOrdinal")] int AfterOrdinal);

/// <summary>Continues an Example Set list within one project.</summary>
/// <param name="ProjectIdentity">The identity of the project that owns the cursor.</param>
/// <param name="AfterSetId">The last Example Set id returned on the preceding page.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologySetListCursor(
    [property: JsonPropertyName("projectIdentity")] string ProjectIdentity,
    [property: JsonPropertyName("afterSetId")] string AfterSetId);

/// <summary>Identifies an immutable Example Set revision and its content digests.</summary>
/// <param name="SetId">The canonical id of the Example Set.</param>
/// <param name="RevisionId">The canonical id of this immutable revision.</param>
/// <param name="ExecutableSha256">The digest of executable example content, excluding review metadata.</param>
/// <param name="CitedRevisionSha256">The digest of the revision including its name and citations.</param>
/// <param name="ExampleCount">The number of examples in this revision.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyRevisionReference(
    [property: JsonPropertyName("setId")] string SetId,
    [property: JsonPropertyName("revisionId")] string RevisionId,
    [property: JsonPropertyName("executableSha256")] string ExecutableSha256,
    [property: JsonPropertyName("citedRevisionSha256")] string CitedRevisionSha256,
    [property: JsonPropertyName("exampleCount")] int ExampleCount);

/// <summary>Describes one saved immutable Example Set revision.</summary>
/// <param name="Revision">The revision identity and content digests.</param>
/// <param name="Name">The Example Set display name.</param>
/// <param name="SavedUtc">The UTC timestamp when this revision was saved.</param>
/// <param name="ProposalId">The optional Proposal associated with this revision.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExampleSetHeader(
    [property: JsonPropertyName("revision")] PhonologyRevisionReference Revision,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("savedUtc")] string SavedUtc,
    [property: JsonPropertyName("proposalId")] string? ProposalId);

/// <summary>Creates the closed, required-field JSON options for phonology contracts.</summary>
public static class PhonologyJson
{
    /// <summary>Creates options that refuse unknown fields and missing constructor arguments.</summary>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(MotifJson.CreateOptions())
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            AllowOutOfOrderMetadataProperties = true,
        };
        options.Converters.Insert(0, new NullablePhonologyRowReasonJsonConverter());
        options.Converters.Insert(0, new PhonologyRowReasonJsonConverter());
        options.Converters.Insert(0, new PhonologyRowCompletionJsonConverter());
        options.Converters.Insert(0, new PhonologyRowDispositionJsonConverter());
        options.Converters.Insert(0, new PhonologyInvocationReasonJsonConverter());
        options.Converters.Insert(0, new PhonologyMsaClassJsonConverter());
        return options;
    }
}
