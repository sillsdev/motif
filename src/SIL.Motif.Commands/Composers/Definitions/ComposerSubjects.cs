namespace SIL.Motif.Commands;

/// <summary>
/// The closed list of Subjects a composer can serve: topics a linguist or an agent names when looking for the
/// composer that fits a problem. A Subject joins the list with its first composer, so every id here is served by
/// at least one registered composer. Reserved and not yet listed: infixation, reduplication, example-sentences and
/// grammar-explanation. Each listed id has a title and description in Help's <c>subjects.json</c>.
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
    ];
}
