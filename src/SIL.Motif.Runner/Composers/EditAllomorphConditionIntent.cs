using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Chooses the LibLCM field that restricts one allomorph.</summary>
public enum AllomorphConditionField
{
    /// <summary>The allomorph's unordered set of phonological environments.</summary>
    PhoneEnv,

    /// <summary>An affix allomorph's ordered disjunction of positions.</summary>
    Position,
}

/// <summary>Replaces the conditions on one existing allomorph after checking its current references.</summary>
/// <param name="Target">Canonical identity of the existing stem or affix allomorph.</param>
/// <param name="Field">The supported condition field for the allomorph's LibLCM class.</param>
/// <param name="ExpectedEnvironments">Current condition identities required for this edit to proceed.</param>
/// <param name="Environments">Requested environment identities; their order matters only for Position.</param>
public sealed record EditAllomorphConditionIntent(CanonicalId Target, AllomorphConditionField Field,
    IReadOnlyList<CanonicalId> ExpectedEnvironments, IReadOnlyList<CanonicalId> Environments);

/// <summary>Parses the closed JSON intent for an allomorph condition edit.</summary>
public static class EditAllomorphConditionIntentParser
{
    /// <summary>Reads canonical identities and requires a known condition field.</summary>
    public static EditAllomorphConditionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditAllomorphCondition", "target", "field",
            "expectedEnvironments", "environments");
        var field = SoundSystemIntentParsing.Text(authored, "field") switch
        {
            "phoneEnv" => AllomorphConditionField.PhoneEnv,
            "position" => AllomorphConditionField.Position,
            var value => throw new ContractParseException(
                $"'EditAllomorphCondition': unsupported condition field '{value}'."),
        };
        var ordered = field == AllomorphConditionField.Position;
        return new EditAllomorphConditionIntent(SoundSystemIntentParsing.Id(authored, "target"), field,
            SoundSystemIntentParsing.Ids(authored, "expectedEnvironments", ordered),
            SoundSystemIntentParsing.Ids(authored, "environments", ordered));
    }
}
