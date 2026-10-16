namespace SIL.Motif.Commands;

/// <summary>
/// The closed list of Subjects a composer can serve: topics a linguist or an agent names when looking for the
/// composer that fits a problem. A Subject joins the list with its first composer, so every id here is served by
/// at least one registered composer. <see cref="Reserved"/> names the Subjects with no composer yet. Each id in
/// either list has a title, description and terms in Help's <c>subjects.json</c>.
/// </summary>
internal static class ComposerSubjects
{
    /// <summary>Every Subject id, in kebab-case, in the order Help lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "sound-inventory",
        "natural-classes",
        "phonological-rules",
        "conditioned-allomorphy",
        "affix-slots-and-templates",
        "inflection-features",
        "noun-class-and-gender",
        "inflection-classes",
        "stem-alternation",
        "lexical-entries-and-glosses",
        "senses-and-definitions",
        "texts-and-analyses",
        "parsimony-dispositions",
        "infixation",
        "reduplication",
    ];

    /// <summary>
    /// Subjects no composer serves yet, plus <c>outside-authoring</c> for requests that are not authoring at all. A
    /// goal that best fits one of these is answered with no match.
    /// </summary>
    public static IReadOnlyList<string> Reserved { get; } =
    [
        "example-sentences",
        "grammar-explanation",
        "outside-authoring",
    ];
}
