using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>The tag to state in the project's Description, and the analysis writing system it is written in; omitted uses the default.</summary>
public sealed record SetProjectAttributionIntent(AttributionTag Tag, string? AnalysisWritingSystem = null);

/// <summary>Reads a closed intent <c>{ "tag", "analysisWs"? }</c>.</summary>
public static class SetProjectAttributionIntentParser
{
    private const string ConstructName = "SetProjectAttribution";

    /// <summary>Requires the tag; an unknown property is refused.</summary>
    public static SetProjectAttributionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "tag", "analysisWs");
        var analysisWs = authored.TryGetProperty("analysisWs", out _) ? SoundSystemIntentParsing.Text(authored, "analysisWs") : null;
        if (analysisWs is not null && string.IsNullOrWhiteSpace(analysisWs))
            throw new ContractParseException($"'{ConstructName}': 'analysisWs' must name a writing system.");
        return new SetProjectAttributionIntent(AttributionTag.Parse(SoundSystemIntentParsing.Text(authored, "tag"), ConstructName), analysisWs);
    }
}

/// <summary>
/// Stages one <c>project/langProject/appendDescription</c> that adds the project's Attribution notice after the Description
/// text already there, or none when the notice is already in it.
/// </summary>
public static class SetProjectAttributionComposer
{
    private const string ConstructName = "SetProjectAttribution";
    private const string Separator = "\u2028";
    private const string Rationale =
        "State the source, revision and licence of imported data once in the project's Description, so the Attribution " +
        "stays with the project. Text the Description already holds is kept.";

    /// <summary>Reads the Description in the chosen writing system and returns the one write, or none.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetProjectAttributionIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var ws = AttributionTag.AnalysisWritingSystem(cache, intent.AnalysisWritingSystem, ConstructName);
        bool present;
        try { present = intent.Tag.IsInAnyAnalysisAlternative(cache, cache.LangProject.Description, AttributionTag.NoticePrefix); }
        catch (InvalidOperationException failure) { throw new ContractParseException($"'{ConstructName}': {failure.Message}"); }
        if (present) return [];
        return [new OperationEnvelope((mintId ?? (() => CanonicalId.Mint()))(), AttributionOperationKinds.AppendToDescription,
            target: CanonicalId.FromGuid(cache.LangProject.Guid),
            after: JsonSerializer.SerializeToElement(new { ws, text = intent.Tag.Notice, separator = Separator }), rationale: Rationale)];
    }
}
