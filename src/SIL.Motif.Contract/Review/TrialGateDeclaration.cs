using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Review;

/// <summary>The exact declared set membership and expectations captured before a Trial.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialGateSet(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string SourceDigest,
    [property: JsonRequired] string MembershipDigest,
    [property: JsonRequired] string ExpectationDigest) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(SourceDigest) ||
            string.IsNullOrWhiteSpace(MembershipDigest) || string.IsNullOrWhiteSpace(ExpectationDigest))
            throw new JsonException("Invalid TrialGateSet.");
    }
}

/// <summary>A project's autonomous policy and exact required scope, frozen with its Trial.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialGateDeclaration(
    [property: JsonRequired] string PolicyDigest,
    [property: JsonRequired] string ScopeDigest,
    [property: JsonRequired] IReadOnlyList<TrialGateSet> WordSets) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(PolicyDigest) || string.IsNullOrWhiteSpace(ScopeDigest) ||
            WordSets is null || WordSets.Count == 0 || WordSets.Any(item => item is null) ||
            WordSets.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != WordSets.Count)
            throw new JsonException("Invalid TrialGateDeclaration.");
    }
}
