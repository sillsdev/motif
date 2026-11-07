using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Review;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WordRow(
    [property: JsonRequired] string CaseKey,
    [property: JsonRequired] string WordKey,
    [property: JsonRequired] string Word,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WritingSystemId,
    [property: JsonRequired] WordSide Before,
    [property: JsonRequired] WordSide After,
    [property: JsonRequired] IReadOnlyList<ReviewFlagId> FlagIds,
    [property: JsonRequired] IReadOnlyList<string> Causes,
    [property: JsonRequired] IReadOnlyList<string> SetIds,
    [property: JsonRequired] long MembershipsTotal,
    [property: JsonRequired] long ExpectationsTotal,
    [property: JsonRequired] WorkChange Work,
    [property: JsonRequired] Attribution Attribution,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "word";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (CaseKey is null ||
            WordKey is null ||
            Word is null ||
            Before is null ||
            After is null ||
            FlagIds is null ||
            Causes is null ||
            Causes.Any(item => item is null) ||
            SetIds is null ||
            SetIds.Any(item => item is null) ||
            MembershipsTotal is < 0 or > 9007199254740991L ||
            ExpectationsTotal is < 0 or > 9007199254740991L ||
            Work is null ||
            Links is null ||
            Links.Any(item => item is null) ||
            RowKind != "word")
            throw new JsonException("Invalid WordRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WordSide(
    [property: JsonRequired] Completion Completion,
    [property: JsonRequired] IReadOnlyList<string> StopReasons,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ObservedReadingCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ApprovedMatched,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ApprovedMissing,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ApprovedUnknown,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ReferenceMatched,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ReferenceMissing,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ReferenceUnknown,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? DisapprovedReturned,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ReadTarget? ReadingPage) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (StopReasons is null ||
            StopReasons.Any(item => item is null) ||
            ObservedReadingCount is < 0 or > 9007199254740991L ||
            ApprovedMatched is < 0 or > 9007199254740991L ||
            ApprovedMissing is < 0 or > 9007199254740991L ||
            ApprovedUnknown is < 0 or > 9007199254740991L ||
            ReferenceMatched is < 0 or > 9007199254740991L ||
            ReferenceMissing is < 0 or > 9007199254740991L ||
            ReferenceUnknown is < 0 or > 9007199254740991L ||
            DisapprovedReturned is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid WordSide.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WordView(
    [property: JsonRequired] WordRow Row,
    [property: JsonRequired] ReviewPage Memberships,
    [property: JsonRequired] ReviewPage Expectations,
    [property: JsonRequired] ReviewPage Readings,
    [property: JsonRequired] ReviewPage SourceReferences,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ReadTarget? TraceRequest) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Row is null ||
            Memberships is null ||
            Expectations is null ||
            Readings is null ||
            SourceReferences is null)
            throw new JsonException("Invalid WordView.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TokenRow(
    [property: JsonRequired] string CaseId,
    [property: JsonRequired] string WordKey,
    [property: JsonRequired] string OccurrenceId,
    [property: JsonRequired] long Weight,
    [property: JsonRequired] string SourceLocator,
    [property: JsonRequired] WordSide Before,
    [property: JsonRequired] WordSide After,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "token";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (CaseId is null ||
            WordKey is null ||
            OccurrenceId is null ||
            Weight is < 1 or > 9007199254740991L ||
            SourceLocator is null ||
            Before is null ||
            After is null ||
            Links is null ||
            Links.Any(item => item is null) ||
            RowKind != "token")
            throw new JsonException("Invalid TokenRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CellRow(
    [property: JsonRequired] string SetId,
    [property: JsonRequired] string CellId,
    [property: JsonRequired] string LexemeId,
    [property: JsonRequired] IReadOnlyList<FeatureValue> Features,
    [property: JsonRequired] long VariantCount,
    [property: JsonRequired] CellOutcome Before,
    [property: JsonRequired] CellOutcome After,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "cell";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (SetId is null ||
            CellId is null ||
            LexemeId is null ||
            Features is null ||
            Features.Any(item => item is null) ||
            VariantCount is < 0 or > 9007199254740991L ||
            Links is null ||
            Links.Any(item => item is null) ||
            RowKind != "cell")
            throw new JsonException("Invalid CellRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureValue(
    [property: JsonRequired] string FeatureId,
    [property: JsonRequired] string ValueId) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (FeatureId is null ||
            ValueId is null)
            throw new JsonException("Invalid FeatureValue.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiagnosticRow(
    [property: JsonRequired] string EventId,
    [property: JsonRequired] string DiagnosticKey,
    [property: JsonRequired] string Code,
    [property: JsonRequired] DiagnosticChange Change,
    [property: JsonRequired] string Severity,
    [property: JsonRequired] IReadOnlyList<string> SubjectIds,
    [property: JsonRequired] string Description,
    [property: JsonRequired] string Attribution,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "warning";

    /// <summary>The previous severity when a paired diagnostic changed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BeforeSeverity { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (EventId is null ||
            DiagnosticKey is null ||
            Code is null ||
            Severity is null ||
            SubjectIds is null ||
            SubjectIds.Any(item => item is null) ||
            Description is null ||
            Attribution is null ||
            Links is null ||
            Links.Any(item => item is null) ||
            RowKind != "warning")
            throw new JsonException("Invalid DiagnosticRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FindingRow(
    [property: JsonRequired] string EventId,
    [property: JsonRequired] string MeasureId,
    [property: JsonRequired] string FindingId,
    [property: JsonRequired] FindingChange Change,
    [property: JsonRequired] string Group,
    [property: JsonRequired] string Unit,
    [property: JsonRequired] string Tier,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Disposition,
    [property: JsonRequired] string EvidenceDigest,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "parsimony";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (EventId is null ||
            MeasureId is null ||
            FindingId is null ||
            Group is null ||
            Unit is null ||
            Tier is null ||
            EvidenceDigest is null ||
            Links is null ||
            Links.Any(item => item is null) ||
            RowKind != "parsimony")
            throw new JsonException("Invalid FindingRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EffectRow(
    [property: JsonRequired] string OperationId,
    [property: JsonRequired] string Summary,
    [property: JsonRequired] ReadTarget Detail) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "effect";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (OperationId is null ||
            Summary is null ||
            Detail is null ||
            RowKind != "effect")
            throw new JsonException("Invalid EffectRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvidenceRow(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] EvidencePin Pins,
    [property: JsonRequired] IReadOnlyList<Limitation> Limitations) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "evidence";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            Kind is null ||
            Pins is null ||
            Limitations is null ||
            Limitations.Any(item => item is null) ||
            RowKind != "evidence")
            throw new JsonException("Invalid EvidenceRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadingRow(
    [property: JsonRequired] string IdentityDigest,
    [property: JsonRequired] Side Side,
    [property: JsonRequired] IdentityState IdentityState,
    [property: JsonRequired] IReadOnlyList<Morph> Morphs,
    [property: JsonRequired] bool Observed,
    [property: JsonRequired] IReadOnlyList<string> ExpectationIds,
    [property: JsonRequired] IReadOnlyList<string> SourceRefs) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "reading";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (IdentityDigest is null ||
            Morphs is null ||
            Morphs.Any(item => item is null) ||
            ExpectationIds is null ||
            ExpectationIds.Any(item => item is null) ||
            SourceRefs is null ||
            SourceRefs.Any(item => item is null) ||
            RowKind != "reading")
            throw new JsonException("Invalid ReadingRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Morph(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FormId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? MsaId,
    [property: JsonRequired] IReadOnlyList<string> InflTypeIds,
    [property: JsonRequired] string Form,
    [property: JsonRequired] string Gloss) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (InflTypeIds is null ||
            InflTypeIds.Any(item => item is null) ||
            Form is null ||
            Gloss is null)
            throw new JsonException("Invalid Morph.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpectationRow(
    [property: JsonRequired] string Id,
    [property: JsonRequired] ExpectationAssertion Assertion,
    [property: JsonRequired] ReferenceAuthority Reference,
    [property: JsonRequired] string ActorId,
    [property: JsonRequired] string RevisionId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReadingIdentityDigest,
    [property: JsonRequired] IReadOnlyList<string> SourceRefs,
    [property: JsonRequired] ExpectationOutcome Before,
    [property: JsonRequired] ExpectationOutcome After) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "expectation";

    /// <summary>The context of a contextual assertion, when captured.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            ActorId is null ||
            RevisionId is null ||
            SourceRefs is null ||
            SourceRefs.Any(item => item is null) ||
            RowKind != "expectation")
            throw new JsonException("Invalid ExpectationRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceRow(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Citation,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Locator,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Excerpt,
    [property: JsonRequired] bool ExcerptComplete,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ActorId) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "source";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            Citation is null ||
            RowKind != "source")
            throw new JsonException("Invalid SourceRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetRow(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Role,
    [property: JsonRequired] string MembershipDigest,
    [property: JsonRequired] string ExpectationDigest,
    [property: JsonRequired] long Memberships,
    [property: JsonRequired] long Words,
    [property: JsonRequired] long Expectations,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "set";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            Name is null ||
            Role is null ||
            MembershipDigest is null ||
            ExpectationDigest is null ||
            Memberships is < 0 or > 9007199254740991L ||
            Words is < 0 or > 9007199254740991L ||
            Expectations is < 0 or > 9007199254740991L ||
            Links is null ||
            Links.Any(item => item is null) ||
            RowKind != "set")
            throw new JsonException("Invalid SetRow.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TracePage(
    [property: JsonRequired] string TraceId,
    [property: JsonRequired] string ReportId,
    [property: JsonRequired] string CaseKey,
    [property: JsonRequired] Side Side,
    [property: JsonRequired] TraceOrigin Origin,
    [property: JsonRequired] string ParserDigest,
    [property: JsonRequired] string GrammarDigest,
    [property: JsonRequired] string ScopeDigest,
    [property: JsonRequired] string TraceSchema,
    [property: JsonRequired] string Completion,
    [property: JsonRequired] bool CaptureComplete,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? TruncationReason,
    [property: JsonRequired] ReadingAgreement ReadingAgreement,
    [property: JsonRequired] IReadOnlyList<TraceNode> Nodes,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? NextCursor,
    [property: JsonRequired] IReadOnlyList<ReadTarget> Links) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (TraceId is null ||
            ReportId is null ||
            CaseKey is null ||
            ParserDigest is null ||
            GrammarDigest is null ||
            ScopeDigest is null ||
            TraceSchema is null ||
            Completion is null ||
            Nodes is null ||
            Nodes.Any(item => item is null) ||
            Links is null ||
            Links.Any(item => item is null))
            throw new JsonException("Invalid TracePage.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TraceNode(
    [property: JsonRequired] string Id,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ParentId,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string Label,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ReasonCode,
    [property: JsonRequired] IReadOnlyList<string> ObjectRefs,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ReadTarget? Children) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>Whether this node is a bounded stub with omitted producer detail.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DetailTruncated { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Id is null ||
            Kind is null ||
            Label is null ||
            ObjectRefs is null ||
            ObjectRefs.Any(item => item is null))
            throw new JsonException("Invalid TraceNode.");
    }
}

/// <summary>A rule's paired parser-work change in the retained report scope.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleWorkRow([property: JsonRequired] string Kind, [property: JsonRequired] string Key,
    [property: JsonRequired] string IdentityQuality,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Scope,
    [property: JsonRequired] string Name, [property: JsonRequired] string Status,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? PercentChange,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? UnavailableReason) : ReviewRow, IJsonOnDeserialized, IJsonOnSerializing
{
    [JsonRequired]
    public string RowKind { get; init; } = "rule-work";

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Kind) || string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(IdentityQuality) ||
            Name is null || RowKind != "rule-work" || Status is not ("paired" or "new" or "missing" or "unavailable") ||
            (Status == "paired") != (PercentChange is not null) || PercentChange is { } n && !double.IsFinite(n))
            throw new JsonException("Invalid rule work row.");
    }
}
