using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Review;

/// <summary>
/// The grammar objects a Proposal adds, by kind. Entries exclude variant entries, which count as variants;
/// senses, MSAs and allomorphs inside an entry the Proposal creates belong to that entry and are not counted.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GrammarSizeDelta(
    [property: JsonRequired] long Entries,
    [property: JsonRequired] long Senses,
    [property: JsonRequired] long Msas,
    [property: JsonRequired] long Allomorphs,
    [property: JsonRequired] long Variants,
    [property: JsonRequired] long AffixProcesses,
    [property: JsonRequired] long PhonologicalRules,
    [property: JsonRequired] long Environments,
    [property: JsonRequired] long ExceptionFeatures,
    [property: JsonRequired] long ExceptionFeatureHosts,
    [property: JsonRequired] long OtherObjects) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>Nothing added.</summary>
    public static GrammarSizeDelta None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Every counted object. Exception feature hosts are references to existing objects, so they are reported
    /// beside the total, not in it.
    /// </summary>
    [JsonIgnore]
    public long ObjectsAdded => Entries + Senses + Msas + Allomorphs + Variants + AffixProcesses +
        PhonologicalRules + Environments + ExceptionFeatures + OtherObjects;

    /// <summary>Whether the Proposal adds a rule: a phonological rule or an affix process.</summary>
    [JsonIgnore]
    public bool AddsRule => PhonologicalRules > 0 || AffixProcesses > 0;

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        long[] counts = [Entries, Senses, Msas, Allomorphs, Variants, AffixProcesses, PhonologicalRules,
            Environments, ExceptionFeatures, ExceptionFeatureHosts, OtherObjects];
        if (counts.Any(count => count is < 0 or > 9007199254740991L))
            throw new JsonException("Invalid GrammarSizeDelta.");
    }
}
