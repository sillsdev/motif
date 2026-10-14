using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The named thresholds used to identify a large increase in ambiguity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferencePolicy : IJsonOnDeserialized
{
    /// <summary>The current policy.</summary>
    public ProposalDifferencePolicy() : this("proposal-difference/v1", 3, 2) { }

    /// <summary>Creates the current policy with positive absolute and relative thresholds.</summary>
    public ProposalDifferencePolicy(string version, int absoluteIncrease, int ratio)
    {
        Version = version;
        AbsoluteIncrease = absoluteIncrease;
        Ratio = ratio;
        Validate();
    }

    /// <summary>The policy identifier.</summary>
    [JsonRequired]
    public string Version { get; init; }

    /// <summary>The smallest increase in parse count that counts as an ambiguity blow-up.</summary>
    [JsonRequired]
    public int AbsoluteIncrease { get; init; }

    /// <summary>The smallest ratio of after to before parse count that counts as an ambiguity blow-up.</summary>
    [JsonRequired]
    public int Ratio { get; init; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        try { Validate(); }
        catch (ArgumentException exception) { throw new JsonException(exception.Message, exception); }
    }

    private void Validate()
    {
        ClosedShape.RequireNonNull(nameof(ProposalDifferencePolicy), (nameof(Version), Version));
        if (Version != "proposal-difference/v1") throw new ArgumentException("Unsupported Difference policy.", nameof(Version));
        if (AbsoluteIncrease <= 0) throw new ArgumentOutOfRangeException(nameof(AbsoluteIncrease));
        if (Ratio <= 0) throw new ArgumentOutOfRangeException(nameof(Ratio));
    }
}

/// <summary>A mechanical change established from two complete parser searches.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<ProposalDifferenceChange>))]
public enum ProposalDifferenceChange
{
    [JsonStringEnumMemberName("gained")]
    Gained,
    [JsonStringEnumMemberName("lost")]
    Lost,
    [JsonStringEnumMemberName("changed")]
    Changed,
    [JsonStringEnumMemberName("ambiguity-up")]
    AmbiguityUp
}

/// <summary>A stable advisory category for a paired parser observation.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<ProposalDifferenceBucket>))]
public enum ProposalDifferenceBucket
{
    [JsonStringEnumMemberName("delta.regression")]
    Regression,
    [JsonStringEnumMemberName("delta.new-competing-parse")]
    NewCompetingParse,
    [JsonStringEnumMemberName("delta.overgeneration")]
    Overgeneration,
    [JsonStringEnumMemberName("delta.new-wrong-reading")]
    NewWrongReading,
    [JsonStringEnumMemberName("delta.analysis-changed")]
    AnalysisChanged,
    [JsonStringEnumMemberName("delta.ambiguity-blowup")]
    AmbiguityBlowup,
    [JsonStringEnumMemberName("delta.lost-parse")]
    LostParse,
    [JsonStringEnumMemberName("delta.improved.approved-match")]
    ImprovedApprovedMatch,
    [JsonStringEnumMemberName("delta.improved.unique")]
    ImprovedUnique,
    [JsonStringEnumMemberName("delta.improved.negative-rejection")]
    ImprovedNegativeRejection
}

/// <summary>A bucket and the captured analysis witnesses supporting its membership.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceBucketMembership(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<ProposalDifferenceBucket>))]
    ProposalDifferenceBucket Bucket,
    [property: JsonRequired] IReadOnlyList<string> AnalysisIds,
    [property: JsonRequired] bool ObservedAfterIncomplete) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceBucketMembership), (nameof(AnalysisIds), AnalysisIds));
}

/// <summary>Agreement and mechanical changes against one frozen human-decision capture.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceCaseFacts(
    [property: JsonRequired] ProposalDifferencePolicy Policy,
    [property: JsonRequired] HumanAgreementFacts Before,
    [property: JsonRequired] HumanAgreementFacts After,
    [property: JsonRequired] bool CompleteComparison,
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumListConverter<ProposalDifferenceChange>))]
    IReadOnlyList<ProposalDifferenceChange> Changes,
    [property: JsonRequired] IReadOnlyList<HumanAgreementProducedIdentity> ObservedBefore,
    [property: JsonRequired] IReadOnlyList<HumanAgreementProducedIdentity> ObservedAfter,
    [property: JsonRequired] IReadOnlyList<HumanAgreementProducedIdentity> GainedIdentities,
    [property: JsonRequired] IReadOnlyList<HumanAgreementProducedIdentity> LostIdentities,
    [property: JsonRequired] IReadOnlyList<ProposalDifferenceBucketMembership> Buckets) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceCaseFacts), (nameof(Policy), Policy),
            (nameof(Before), Before), (nameof(After), After), (nameof(Changes), Changes),
            (nameof(ObservedBefore), ObservedBefore), (nameof(ObservedAfter), ObservedAfter),
            (nameof(GainedIdentities), GainedIdentities), (nameof(LostIdentities), LostIdentities),
            (nameof(Buckets), Buckets));

    /// <summary>Whether any advisory category calls for checking a possible adverse effect.</summary>
    public bool CouldBeBad => Buckets.Any(item => item.Bucket is ProposalDifferenceBucket.Regression or
        ProposalDifferenceBucket.NewCompetingParse or ProposalDifferenceBucket.Overgeneration or
        ProposalDifferenceBucket.NewWrongReading or ProposalDifferenceBucket.AnalysisChanged or
        ProposalDifferenceBucket.AmbiguityBlowup or ProposalDifferenceBucket.LostParse);

    /// <summary>Whether any category records improvement against captured human decisions.</summary>
    public bool Improved => Buckets.Any(item => item.Bucket is ProposalDifferenceBucket.ImprovedApprovedMatch or
        ProposalDifferenceBucket.ImprovedUnique or ProposalDifferenceBucket.ImprovedNegativeRejection);
}
