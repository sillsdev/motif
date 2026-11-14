using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Review;

/// <summary>
/// Words the Proposal newly parses, split into attested (in the project's texts or carrying an approved analysis)
/// and generated (synthetic probe forms), with the grammar objects added and the advisory gate over them.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GainSummary(
    [property: JsonRequired] long AttestedGained,
    [property: JsonRequired] long GeneratedGained,
    [property: JsonRequired] GrammarSizeDelta Size,
    [property: JsonRequired] GainGateResult Gate) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>Attested types gained per object added; null when no object is added.</summary>
    [JsonIgnore]
    public double? AttestedPerObject => Size.ObjectsAdded == 0 ? null : (double)AttestedGained / Size.ObjectsAdded;

    /// <summary>Objects added per attested type gained; null when none is gained.</summary>
    [JsonIgnore]
    public double? ObjectsPerAttestedType => AttestedGained == 0 ? null : (double)Size.ObjectsAdded / AttestedGained;

    /// <summary>Evaluates the gate over the attested gains and the objects added.</summary>
    public static GainSummary Create(long attested, long generated, GrammarSizeDelta size, GainGatePolicy policy,
        bool incomplete)
    {
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(policy);
        var outcome = Evaluate(attested, size, policy, incomplete);
        var gate = new GainGateResult(outcome, policy.Mode, policy.MinAttestedPerObject,
            policy.Mode == GainGateMode.Hold && outcome == GainOutcome.UnderThreshold);
        return new GainSummary(attested, generated, size, gate);
    }

    private static GainOutcome Evaluate(long attested, GrammarSizeDelta size, GainGatePolicy policy, bool incomplete)
    {
        if (policy.Mode == GainGateMode.Off) return GainOutcome.NotEvaluated;
        if (incomplete) return GainOutcome.Incomplete;
        if (size.ObjectsAdded == 0) return GainOutcome.NoObjectsAdded;
        if (size.AddsRule) return GainOutcome.RuleAdded;
        return (double)attested / size.ObjectsAdded >= policy.MinAttestedPerObject
            ? GainOutcome.WithinThreshold
            : GainOutcome.UnderThreshold;
    }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (AttestedGained < 0 || GeneratedGained < 0 || Size is null || Gate is null)
            throw new JsonException("Invalid GainSummary.");
    }
}
