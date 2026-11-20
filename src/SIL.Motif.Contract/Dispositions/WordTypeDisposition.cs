using System.Text;

namespace SIL.Motif.Contract.Dispositions;

/// <summary>
/// The one identity of a corpus word type. Coverage joins its ledger to a frequency list on this key and the
/// disposition store seeks on it, so a disposition recorded for a form is found for exactly the forms Coverage
/// treats as one type, pinned by <c>WordTypeKeyTests.TheKeyIsTheKeyCoverageJoinsOn</c>. Canonically equivalent
/// spellings are one type. Case is folded by the corpus tokenizer, not here, so a form is given as the
/// frequency list spells it.
/// </summary>
public static class WordTypeKey
{
    /// <summary>The key of a word: its canonical decomposition (NFD), compared ordinally.</summary>
    public static string Of(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return word.Normalize(NormalizationForm.FormD);
    }
}

/// <summary>The closed set of dispositions a word type can be given when it is not a word to analyse.</summary>
public enum DispositionCategory
{
    /// <summary>A proper name.</summary>
    Name,
    /// <summary>A word borrowed from another language and used in this one.</summary>
    Loan,
    /// <summary>A word of another language, quoted rather than borrowed.</summary>
    OtherLanguage,
    /// <summary>An abbreviation.</summary>
    Abbreviation,
    /// <summary>A numeral written with digits or symbols.</summary>
    Numeral,
    /// <summary>A token that is not a word of the text: markup, an OCR artefact, a stray character.</summary>
    NonWord,
    /// <summary>A misspelling of another word of the language; it names the intended form.</summary>
    Misspelling,
}

/// <summary>Wire names for <see cref="DispositionCategory"/>, which are the names Coverage's state rule reads.</summary>
public static class DispositionCategories
{
    /// <summary>The wire name of a category, such as <c>other-language</c>.</summary>
    public static string Wire(DispositionCategory category) => category switch
    {
        DispositionCategory.Name => "name",
        DispositionCategory.Loan => "loan",
        DispositionCategory.OtherLanguage => "other-language",
        DispositionCategory.Abbreviation => "abbreviation",
        DispositionCategory.Numeral => "numeral",
        DispositionCategory.NonWord => "non-word",
        DispositionCategory.Misspelling => "misspelling",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>Every category in the order they are listed to a person.</summary>
    public static IReadOnlyList<DispositionCategory> All { get; } = Enum.GetValues<DispositionCategory>();

    /// <summary>Every wire name, in the same order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. All.Select(Wire)];

    /// <summary>
    /// Reads a wire name. <c>artifact</c> is accepted as another name for <c>non-word</c>, as Coverage accepts it.
    /// Matching is exact and lower case, so a typo is refused rather than guessed.
    /// </summary>
    public static bool TryParse(string? text, out DispositionCategory category)
    {
        category = default;
        if (text is null) return false;
        if (text == "artifact") text = "non-word";
        foreach (var candidate in All)
            if (Wire(candidate) == text) { category = candidate; return true; }
        return false;
    }
}

/// <summary>One word type's standing disposition, and who recorded it and when.</summary>
/// <param name="Key">The word type key, <see cref="WordTypeKey.Of"/> of the form.</param>
/// <param name="Form">The form as it was first given, for display.</param>
/// <param name="Category">The wire name of the category.</param>
/// <param name="Evidence">Why this is the disposition: what the writer saw. Never empty.</param>
/// <param name="Intended">For a misspelling, the form it was meant to be; otherwise null.</param>
/// <param name="RecordedBy">The person or agent that recorded it.</param>
/// <param name="RecordedUtc">When it was recorded, in UTC.</param>
public sealed record WordTypeDisposition(
    string Key, string Form, string Category, string Evidence, string? Intended, string RecordedBy, string RecordedUtc);

/// <summary>A disposition that no longer stands, with how it ended. Ended rows are kept as history.</summary>
/// <param name="Disposition">What was recorded.</param>
/// <param name="EndedKind"><c>replaced</c> when a later write took its place, <c>cleared</c> when it was removed.</param>
/// <param name="EndedUtc">When it ended, in UTC.</param>
/// <param name="EndedBy">The person or agent that ended it.</param>
/// <param name="EndedNote">The reason given when it was cleared, or null.</param>
public sealed record EndedWordTypeDisposition(
    WordTypeDisposition Disposition, string EndedKind, string EndedUtc, string EndedBy, string? EndedNote);
