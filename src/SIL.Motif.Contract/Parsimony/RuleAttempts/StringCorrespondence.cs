using System.Globalization;
using System.Text;

namespace SIL.Motif.Contract.Parsimony.RuleAttempts;

/// <summary>Where in the word a correspondence applies.</summary>
public enum CorrespondenceAnchor
{
    /// <summary>The forms differ at the start of the word.</summary>
    Initial,

    /// <summary>The forms differ inside the word.</summary>
    Medial,

    /// <summary>The forms differ at the end of the word.</summary>
    Final,
}

/// <summary>
/// How a variant's form differs from its main form, with every vowel written as <c>V</c> so that final-vowel loss
/// from <c>angka</c> and from <c>ayri</c> is one correspondence. A trailing hyphen is recorded, not compared.
/// </summary>
/// <param name="Removed">The segments the main form has and the variant lacks, vowels as <c>V</c>.</param>
/// <param name="Inserted">The segments the variant has and the main form lacks, vowels as <c>V</c>.</param>
/// <param name="Anchor">Where in the word the difference sits.</param>
/// <param name="Hyphen">Whether the variant form ends in a hyphen the main form lacks.</param>
public sealed record StringCorrespondence(string Removed, string Inserted, CorrespondenceAnchor Anchor, bool Hyphen)
{
    private const string Vowels = "aeiou";

    /// <summary>The stable text of this correspondence, used inside a shape key.</summary>
    public string Key => $"{Removed}>{Inserted}@{Anchor.ToString().ToLowerInvariant()}{(Hyphen ? "+hyphen" : "")}";

    /// <summary>Describes how <paramref name="variant"/> differs from <paramref name="main"/>.</summary>
    /// <param name="main">The main form.</param>
    /// <param name="variant">The variant form.</param>
    public static StringCorrespondence Of(string main, string variant)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(variant);
        var hyphen = variant.EndsWith('-') && !main.EndsWith('-');
        var a = main.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var b = variant.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        if (hyphen) b = b[..^1];
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix &&
               a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;
        var removed = a[prefix..(a.Length - suffix)];
        var inserted = b[prefix..(b.Length - suffix)];
        var anchor = suffix == 0 ? CorrespondenceAnchor.Final
            : prefix == 0 ? CorrespondenceAnchor.Initial : CorrespondenceAnchor.Medial;
        return new StringCorrespondence(Abstract(removed), Abstract(inserted), anchor, hyphen);
    }

    private static string Abstract(string segments)
    {
        var builder = new StringBuilder(segments.Length);
        foreach (var c in segments)
        {
            var baseLetter = c.ToString().Normalize(NormalizationForm.FormD)[0];
            builder.Append(Vowels.Contains(baseLetter) ? 'V' : c);
        }
        return builder.ToString();
    }
}
