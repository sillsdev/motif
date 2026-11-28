namespace SIL.Motif.Contract.Requests;

/// <summary>Groups the corpus word types that end or start in a string, or match a pattern, by candidate stem. It reads and never writes.</summary>
/// <param name="ProjectPath">The known project whose Texts supply the words and whose lexicon supplies the known stems.</param>
/// <param name="Kind">The grouping: suffix groups words by the stem before an ending, prefix groups them by the stem after a start.</param>
/// <param name="Ending">For kind suffix: the string the words end in, such as an affix without its hyphen. Give exactly one of ending, start and pattern.</param>
/// <param name="Start">For kind prefix: the string the words start with. Give exactly one of ending, start and pattern.</param>
/// <param name="Pattern">An anchored pattern over the whole folded word with one named group, for example (?&lt;stem&gt;.+)(?:an|on). It has no backtracking, so it cannot run away, and it is at most 256 characters.</param>
/// <param name="ParseState">Which words to list: parsed (has a parse, none approved), unparsed (no parse), approved (a human approved a parse) or all. The default is all.</param>
/// <param name="Limit">The most stem groups on one page. The default is 50 and the cap is 200; a larger value is reduced to the cap and the response says so.</param>
/// <param name="Cursor">Opaque cursor from a previous response's nextCursor; omit for the first page of stem groups.</param>
/// <param name="MinStem">The fewest graphemes a stem may have. The default is 2, so single-letter stems are left out; 1 lists them too.</param>
/// <param name="AsOf">Read the project as of baseline (the current Baseline) or as of a Proposal id (that Proposal's latest Trial copy). Omit to read the saved project.</param>
/// <param name="Fold">The fold set that makes two spellings one, such as nfc,case (the default), a named set or project for the project's own fold rules.</param>
public sealed record GroupRequest(string ProjectPath, string Kind, string? Ending = null, string? Start = null,
    string? Pattern = null, string? ParseState = null, int? Limit = null, string? Cursor = null, string? AsOf = null,
    string? Fold = null, int? MinStem = null)
{
    /// <summary>The stem groups to skip that <see cref="Cursor"/> names, or -1 when it is not a cursor this API issued. A method, so it is not a schema property.</summary>
    public int CursorOffset() => SIL.Motif.Contract.Responses.ResultPaging.OffsetOrInvalid(Cursor);
}
