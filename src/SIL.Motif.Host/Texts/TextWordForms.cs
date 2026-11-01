using SIL.LCModel;

namespace SIL.Motif.Host.Texts;

/// <summary>What one wordform in a Text counts as for Text Coverage.</summary>
public enum TextWordKind
{
    /// <summary>A word in a vernacular writing system; Text Coverage counts it whether or not it parses.</summary>
    Vernacular,

    /// <summary>A token of digits only, counted separately because it is not a word to parse.</summary>
    Digits,

    /// <summary>A word recorded only in a non-vernacular writing system, counted separately.</summary>
    OtherWritingSystem,
}

/// <summary>Splits a Text wordform's spellings into the vernacular words that count and the tokens that do not.</summary>
public static class TextWordForms
{
    /// <summary>
    /// Classifies <paramref name="wordform"/>: its vernacular spellings when it has any non-empty one that is not
    /// digits only, <see cref="TextWordKind.Digits"/> when its vernacular spelling is digits only, and
    /// <see cref="TextWordKind.OtherWritingSystem"/> when it is recorded in no vernacular writing system.
    /// </summary>
    /// <param name="cache">The project the wordform belongs to, which names its vernacular writing systems.</param>
    /// <param name="wordform">The wordform an occurrence in a Text refers to.</param>
    /// <param name="normalize">Applied to each vernacular spelling; the spellings are then distinct and ordered.</param>
    public static (TextWordKind Kind, string[] Forms) Classify(
        LcmCache cache, IWfiWordform wordform, Func<string, string> normalize)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(wordform);
        ArgumentNullException.ThrowIfNull(normalize);
        var vernacular = cache.ServiceLocator.WritingSystems.VernacularWritingSystems;
        var available = wordform.Form.AvailableWritingSystemIds.ToHashSet();
        var forms = new List<string>();
        var sawVernacular = false;
        foreach (var writingSystem in vernacular)
        {
            if (!available.Contains(writingSystem.Handle)) continue;
            var text = wordform.Form.get_String(writingSystem.Handle)?.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            sawVernacular = true;
            if (text.All(char.IsDigit)) continue;
            forms.Add(normalize(text));
        }
        var distinct = forms.Where(form => form.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 0) return (TextWordKind.Vernacular, distinct);
        return (sawVernacular ? TextWordKind.Digits : TextWordKind.OtherWritingSystem, []);
    }
}
