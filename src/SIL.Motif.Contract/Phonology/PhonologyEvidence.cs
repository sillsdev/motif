using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Phonology;

[JsonConverter(typeof(PhonologyInvocationReasonJsonConverter))]
public enum PhonologyInvocationReason { AbsentProducer, ProducerUnavailable, InvocationRefused, InvocationTimedOut, Cancelled }

public sealed class PhonologyInvocationReasonJsonConverter() : StrictPhonologyEnumConverter<PhonologyInvocationReason>;

[JsonConverter(typeof(PhonologyRowDispositionJsonConverter))]
public enum PhonologyRowDisposition { Agree, Disagree, Unavailable }

public sealed class PhonologyRowDispositionJsonConverter() : StrictPhonologyEnumConverter<PhonologyRowDisposition>;

[JsonConverter(typeof(PhonologyRowCompletionJsonConverter))]
public enum PhonologyRowCompletion { Complete, Incomplete, NotStarted }

public sealed class PhonologyRowCompletionJsonConverter() : StrictPhonologyEnumConverter<PhonologyRowCompletion>;

[JsonConverter(typeof(PhonologyRowReasonJsonConverter))]
public enum PhonologyRowReason { EmptyLexicon, UnknownEntry, AmbiguousContext, MsaNotOwnedByEntry, InvalidShape, InvalidStratum, ResultLimit, WorkCap, StepCap, Timeout }

public sealed class PhonologyRowReasonJsonConverter() : StrictPhonologyEnumConverter<PhonologyRowReason>;

public sealed class NullablePhonologyRowReasonJsonConverter : JsonConverter<PhonologyRowReason?>
{
    private static readonly PhonologyRowReasonJsonConverter Inner = new();

    public override PhonologyRowReason? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => reader.TokenType == JsonTokenType.Null
        ? null
        : Inner.Read(ref reader, typeof(PhonologyRowReason), options);

    public override void Write(Utf8JsonWriter writer, PhonologyRowReason? value, JsonSerializerOptions options)
    {
        if (value is { } reason) Inner.Write(writer, reason, options);
        else writer.WriteNullValue();
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyBaselineSource(
    [property: JsonPropertyName("baselineToken"), JsonRequired] BaselineToken BaselineToken);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyDependency(
    [property: JsonPropertyName("path"), JsonRequired] string Path,
    [property: JsonPropertyName("sha256"), JsonRequired] string Sha256);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PhonologyInvocationBinding.Produced), "produced")]
[JsonDerivedType(typeof(PhonologyInvocationBinding.Unavailable), "unavailable")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record PhonologyInvocationBinding
{
    private PhonologyInvocationBinding() { }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Produced(
        [property: JsonPropertyName("grammarSha256"), JsonRequired] string GrammarSha256,
        [property: JsonPropertyName("requestFileSha256"), JsonRequired] string RequestFileSha256,
        [property: JsonPropertyName("producerRequestSha256"), JsonRequired] string ProducerRequestSha256,
        [property: JsonPropertyName("producerBuild"), JsonRequired] string ProducerBuild,
        [property: JsonPropertyName("executableSha256"), JsonRequired] string ExecutableSha256,
        [property: JsonPropertyName("outputSha256"), JsonRequired] string OutputSha256,
        [property: JsonPropertyName("outputBytes"), JsonRequired] int OutputBytes,
        [property: JsonPropertyName("measuredEntryCount"), JsonRequired] ulong MeasuredEntryCount,
        [property: JsonPropertyName("agreeCount"), JsonRequired] int AgreeCount,
        [property: JsonPropertyName("disagreeCount"), JsonRequired] int DisagreeCount,
        [property: JsonPropertyName("unavailableCount"), JsonRequired] int UnavailableCount) : PhonologyInvocationBinding;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Unavailable(
        [property: JsonPropertyName("reason"), JsonRequired, JsonConverter(typeof(PhonologyInvocationReasonJsonConverter))] PhonologyInvocationReason Reason,
        [property: JsonPropertyName("detail"), JsonRequired] string Detail) : PhonologyInvocationBinding;

}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PhonologyRowEvidence.Produced), "produced")]
[JsonDerivedType(typeof(PhonologyRowEvidence.Unavailable), "unavailable")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record PhonologyRowEvidence
{
    private PhonologyRowEvidence() { }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Produced(
        [property: JsonPropertyName("requestedContext"), JsonRequired] PhonologyGenerationContext RequestedContext,
        [property: JsonPropertyName("resolvedContext"), JsonRequired] PhonologyGenerationContext? ResolvedContext,
        [property: JsonPropertyName("underlying"), JsonRequired] string Underlying,
        [property: JsonPropertyName("expected"), JsonRequired] IReadOnlyList<string> Expected,
        [property: JsonPropertyName("effectiveLimits"), JsonRequired] PhonologyLimits EffectiveLimits,
        [property: JsonPropertyName("disposition"), JsonRequired, JsonConverter(typeof(PhonologyRowDispositionJsonConverter))] PhonologyRowDisposition Disposition,
        [property: JsonPropertyName("completion"), JsonRequired, JsonConverter(typeof(PhonologyRowCompletionJsonConverter))] PhonologyRowCompletion Completion,
        [property: JsonPropertyName("expectedObserved"), JsonRequired] IReadOnlyList<bool> ExpectedObserved,
        [property: JsonPropertyName("observed"), JsonRequired] string? Observed,
        [property: JsonPropertyName("reason"), JsonRequired, JsonConverter(typeof(NullablePhonologyRowReasonJsonConverter))] PhonologyRowReason? Reason,
        [property: JsonPropertyName("steps"), JsonRequired] ulong Steps,
        [property: JsonPropertyName("workSteps"), JsonRequired] ulong WorkSteps,
        [property: JsonPropertyName("capped"), JsonRequired] bool Capped,
        [property: JsonPropertyName("timedOut"), JsonRequired] bool TimedOut,
        [property: JsonPropertyName("orderedRuleIds"), JsonRequired] IReadOnlyList<string> OrderedRuleIds,
        [property: JsonPropertyName("visitedRuleIds"), JsonRequired] IReadOnlyList<string> VisitedRuleIds,
        [property: JsonPropertyName("appliedRuleIds"), JsonRequired] IReadOnlyList<string> AppliedRuleIds) : PhonologyRowEvidence;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Unavailable(
        [property: JsonPropertyName("reason"), JsonRequired, JsonConverter(typeof(PhonologyInvocationReasonJsonConverter))] PhonologyInvocationReason Reason,
        [property: JsonPropertyName("detail"), JsonRequired] string Detail) : PhonologyRowEvidence;

}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PhonologyRowSummary.Produced), "produced")]
[JsonDerivedType(typeof(PhonologyRowSummary.Unavailable), "unavailable")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record PhonologyRowSummary
{
    private PhonologyRowSummary() { }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Produced(
        [property: JsonPropertyName("requestedContext"), JsonRequired] PhonologyGenerationContext RequestedContext,
        [property: JsonPropertyName("resolvedContext"), JsonRequired] PhonologyGenerationContext? ResolvedContext,
        [property: JsonPropertyName("underlying"), JsonRequired] string Underlying,
        [property: JsonPropertyName("expected"), JsonRequired] IReadOnlyList<string> Expected,
        [property: JsonPropertyName("effectiveLimits"), JsonRequired] PhonologyLimits EffectiveLimits,
        [property: JsonPropertyName("disposition"), JsonRequired, JsonConverter(typeof(PhonologyRowDispositionJsonConverter))] PhonologyRowDisposition Disposition,
        [property: JsonPropertyName("completion"), JsonRequired, JsonConverter(typeof(PhonologyRowCompletionJsonConverter))] PhonologyRowCompletion Completion,
        [property: JsonPropertyName("expectedObserved"), JsonRequired] IReadOnlyList<bool> ExpectedObserved,
        [property: JsonPropertyName("observed"), JsonRequired] string? Observed,
        [property: JsonPropertyName("reason"), JsonRequired, JsonConverter(typeof(NullablePhonologyRowReasonJsonConverter))] PhonologyRowReason? Reason,
        [property: JsonPropertyName("steps"), JsonRequired] ulong Steps,
        [property: JsonPropertyName("workSteps"), JsonRequired] ulong WorkSteps,
        [property: JsonPropertyName("capped"), JsonRequired] bool Capped,
        [property: JsonPropertyName("timedOut"), JsonRequired] bool TimedOut,
        [property: JsonPropertyName("orderedRuleCount"), JsonRequired] int OrderedRuleCount,
        [property: JsonPropertyName("visitedRuleCount"), JsonRequired] int VisitedRuleCount,
        [property: JsonPropertyName("appliedRuleCount"), JsonRequired] int AppliedRuleCount,
        [property: JsonPropertyName("appliedRuleIds"), JsonRequired] IReadOnlyList<string> AppliedRuleIds,
        [property: JsonPropertyName("appliedRulesOmitted"), JsonRequired] int AppliedRulesOmitted) : PhonologyRowSummary;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Unavailable(
        [property: JsonPropertyName("disposition"), JsonRequired, JsonConverter(typeof(PhonologyRowDispositionJsonConverter))] PhonologyRowDisposition Disposition,
        [property: JsonPropertyName("reason"), JsonRequired, JsonConverter(typeof(PhonologyInvocationReasonJsonConverter))] PhonologyInvocationReason Reason,
        [property: JsonPropertyName("detail"), JsonRequired] string Detail) : PhonologyRowSummary
    {
        // A fixed disposition keeps a contradictory payload from reading as an unavailable summary.
        private readonly bool _fixedDisposition = Disposition == PhonologyRowDisposition.Unavailable
            ? true : throw new JsonException("An unavailable summary must have unavailable disposition.");

        /// <summary>Creates the unavailable summary for a reason, with its fixed disposition.</summary>
        public static Unavailable For(PhonologyInvocationReason reason, string detail) =>
            new(PhonologyRowDisposition.Unavailable, reason, detail);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyAssessmentHeader(
    [property: JsonPropertyName("assessmentId"), JsonRequired] string AssessmentId,
    [property: JsonPropertyName("assessor"), JsonRequired] string Assessor,
    [property: JsonPropertyName("savedUtc"), JsonRequired] string SavedUtc,
    [property: JsonPropertyName("exampleSet"), JsonRequired] PhonologyExampleSetHeader ExampleSet,
    [property: JsonPropertyName("source"), JsonRequired] PhonologyBaselineSource Source,
    [property: JsonPropertyName("requestedLimits"), JsonRequired] PhonologyLimits RequestedLimits,
    [property: JsonPropertyName("invocation"), JsonRequired] PhonologyInvocationBinding Invocation);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExampleSummary(
    [property: JsonPropertyName("ordinal"), JsonRequired] int Ordinal,
    [property: JsonPropertyName("example"), JsonRequired] PhonologyExample Example,
    [property: JsonPropertyName("evidence"), JsonRequired] PhonologyRowSummary Evidence);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExampleDetail(
    [property: JsonPropertyName("ordinal"), JsonRequired] int Ordinal,
    [property: JsonPropertyName("example"), JsonRequired] PhonologyExample Example,
    [property: JsonPropertyName("evidence"), JsonRequired] PhonologyRowEvidence Evidence);

/// <summary>Only exact named string tags represent retained phonology classifications.</summary>
public class StrictPhonologyEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A named phonology tag is required.");
        var tag = reader.GetString();
        foreach (var value in Enum.GetValues<T>())
            if (tag == JsonNamingPolicy.CamelCase.ConvertName(value.ToString())) return value;
        throw new JsonException("Unknown phonology tag.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Unknown phonology tag.");
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }
}
