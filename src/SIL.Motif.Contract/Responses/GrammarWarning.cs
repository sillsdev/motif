using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// One finding the parser reported against the grammar as a whole, split into where it applies and what is
/// wrong, with every project object it names resolved to a name a person recognises.
/// </summary>
/// <param name="Severity">
/// The parser's own prefix on the line — <c>warning</c> for something it dropped or distrusted,
/// <c>capability</c> for something it does not model — lower-cased.
/// </param>
/// <param name="Kind">
/// What the finding is about, from the first object <paramref name="Subject"/> names: <c>Entry</c>,
/// <c>Phoneme</c>, <c>Phonological rule</c> and so on. Empty when the subject names no project object.
/// </param>
/// <param name="Subject">
/// Where the finding applies — the parser's context before its first colon — or empty when the line has no
/// context.
/// </param>
/// <param name="Problem">What is wrong there.</param>
/// <param name="Text">The line exactly as the parser wrote it, identifiers and all.</param>
public sealed record GrammarWarning(
    string Severity,
    string Kind,
    IReadOnlyList<GrammarWarningPart> Subject,
    IReadOnlyList<GrammarWarningPart> Problem,
    string Text)
{
    /// <summary>
    /// The parser's own plain-language name for this kind of finding, such as "Partial morpheme analysis", or
    /// <see langword="null"/> when the parser gave it none. Findings with the same name belong together.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>The parser's stable code for this kind of finding, or <see langword="null"/> when it gave none.</summary>
    public string? Code { get; init; }
}

/// <summary>
/// One run of a <see cref="GrammarWarning"/>'s text: plain prose, a quoted value the parser echoed, or a
/// project object it named by identifier.
/// </summary>
/// <param name="Text">What to show: the prose, the value, or the object's name.</param>
/// <param name="Role">
/// <c>text</c> for prose; <c>value</c> for a quoted value that names no object; <c>object</c> for an object
/// the project contains; <c>missing</c> for an identifier the project does not contain, which is usually
/// the finding itself.
/// </param>
/// <param name="ObjectId">The identifier the parser wrote, for <c>object</c> and <c>missing</c> parts.</param>
/// <param name="Kind">For an <c>object</c> part, what sort of object it is: <c>Sense</c>, <c>Allomorph</c>, ….</param>
/// <param name="FieldWorksLink">
/// A <c>silfw:</c> link that opens FieldWorks on this object's record, or <see langword="null"/> when
/// FieldWorks has no tool that shows it.
/// </param>
public sealed record GrammarWarningPart(
    string Text,
    string Role,
    string? ObjectId = null,
    string? Kind = null,
    string? FieldWorksLink = null);
