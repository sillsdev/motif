using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The project's senses with each one's Definition in every writing system the preview edits, as runs with the
/// styles and writing systems they use, read from the current Baseline.
/// </summary>
/// <param name="Senses">The senses, in headword order.</param>
/// <param name="Styles">The character styles the Definitions use, resolved per writing system.</param>
/// <param name="WritingSystems">The writing systems shown and used, with direction and font.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SenseDefinitionsResponse(
    IReadOnlyList<SenseDefinitions> Senses, IReadOnlyList<GdrStyle> Styles, IReadOnlyList<GdrWritingSystem> WritingSystems)
{
    /// <summary>The token of the Baseline the senses were read from.</summary>
    public BaselineToken? Baseline { get; init; }
}

/// <summary>One sense: its canonical id, a label a person knows it by, and its Definition by writing system.</summary>
/// <param name="Id">The sense's canonical id.</param>
/// <param name="Label">The entry's headword and the sense's gloss.</param>
/// <param name="Definition">One alternative per writing system shown, empty ones included.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SenseDefinitions(string Id, string Label, IReadOnlyList<RichStringAlternative> Definition);

/// <summary>One writing system's alternative of a MultiString field, as runs; an empty alternative has none.</summary>
/// <param name="WritingSystem">The alternative's writing-system tag.</param>
/// <param name="Runs">The runs, with every property, character style and object data.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RichStringAlternative(string WritingSystem, IReadOnlyList<GdrRun> Runs)
{
    /// <summary>The alternative's text.</summary>
    [JsonIgnore]
    public string Text => string.Concat(Runs.Select(run => run.Text));

    /// <summary>The <see cref="TextRunsDigest"/> of the runs: what an edit of this alternative began from.</summary>
    [JsonIgnore]
    public string Digest => TextRunsDigest.OfRuns(Runs);
}
