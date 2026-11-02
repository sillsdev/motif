using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>A named Selection's chosen inputs, complete parsing policy, revision, and timestamps.</summary>
[method: JsonConstructor]
public sealed record NamedSelectionProjection(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    string CreatedUtc,
    string UpdatedUtc,
    SelectionParsingLimits Limits,
    string Revision)
{
    /// <summary>The word-list file the added words came from and each word's count; null for typed words.</summary>
    public SelectionWordList? WordList { get; init; }

    [JsonIgnore]
    public int? PerWordLimitMs => Limits.TimeMode == SelectionTimeLimitMode.Explicit
        ? Limits.ExplicitPerWordLimitMs : null;

    [JsonIgnore]
    public ParserWorkLimit PerWordParserWorkLimit => Limits.PerWordParserWorkLimit;

    public NamedSelectionProjection(
        string name,
        IReadOnlyList<Guid> textIds,
        IReadOnlyList<string> addedWords,
        string createdUtc,
        string updatedUtc,
        int? perWordLimitMs = 1000,
        ParserWorkLimit? perWordParserWorkLimit = null)
        : this(name, textIds, addedWords, createdUtc, updatedUtc,
            new SelectionParsingLimits(perWordParserWorkLimit ?? ParserWorkLimit.Default,
                perWordLimitMs is null ? SelectionTimeLimitMode.Estimated : SelectionTimeLimitMode.Explicit,
                perWordLimitMs), "0")
    {
    }
}

/// <summary>
/// The saved default Selection, if present, whether first-time setup was skipped, and the separate Parsimony
/// Selection that Parsimony's Text group measures, if one has been chosen.
/// </summary>
public sealed record DefaultSelectionResponse(NamedSelectionProjection? Selection, bool SetupSkipped = false,
    NamedSelectionProjection? ParsimonySelection = null);
