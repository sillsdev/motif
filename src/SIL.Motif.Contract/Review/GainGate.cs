using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Review;

/// <summary>How the gain gate reacts when a Proposal adds many objects for few attested gains.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<GainGateMode>))]
public enum GainGateMode
{
    [JsonStringEnumMemberName("off")] Off,
    [JsonStringEnumMemberName("advisory")] Advisory,
    [JsonStringEnumMemberName("hold")] Hold,
}

/// <summary>What the gain gate concluded.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<GainOutcome>))]
public enum GainOutcome
{
    [JsonStringEnumMemberName("within-threshold")] WithinThreshold,
    [JsonStringEnumMemberName("under-threshold")] UnderThreshold,
    [JsonStringEnumMemberName("rule-added")] RuleAdded,
    [JsonStringEnumMemberName("no-objects-added")] NoObjectsAdded,
    [JsonStringEnumMemberName("incomplete")] Incomplete,
    [JsonStringEnumMemberName("not-evaluated")] NotEvaluated,
}

/// <summary>The configured gate: its mode and the least attested types gained per object added.</summary>
public sealed record GainGatePolicy
{
    /// <summary>Creates a policy; the threshold must be positive.</summary>
    public GainGatePolicy(GainGateMode mode, double minAttestedPerObject)
    {
        if (!(minAttestedPerObject > 0) || double.IsInfinity(minAttestedPerObject))
            throw new ArgumentOutOfRangeException(nameof(minAttestedPerObject));
        Mode = mode;
        MinAttestedPerObject = minAttestedPerObject;
    }

    /// <summary>Advisory at one and a half attested types per object.</summary>
    public static GainGatePolicy Default { get; } = new(GainGateMode.Advisory, 1.5);

    /// <summary>The gate mode.</summary>
    public GainGateMode Mode { get; }

    /// <summary>The least attested types gained per object added that passes the gate.</summary>
    public double MinAttestedPerObject { get; }
}

/// <summary>The gate verdict and whether it asks for a hold.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GainGateResult(
    [property: JsonRequired] GainOutcome Outcome,
    [property: JsonRequired] GainGateMode Mode,
    [property: JsonRequired] double MinAttestedPerObject,
    [property: JsonRequired] bool Hold);
