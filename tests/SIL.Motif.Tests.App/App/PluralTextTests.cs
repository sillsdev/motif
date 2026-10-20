using System.Globalization;
using SIL.Motif.App;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the ICU-style plural helper that formats count sentences stored as .resx values.</summary>
public sealed class PluralTextTests
{
    private const string Changes = "{0, plural, one {# change} other {# changes}}";

    [Theory]
    [InlineData(1, "1 change")]
    [InlineData(0, "0 changes")]
    [InlineData(2, "2 changes")]
    [InlineData(1234, "1,234 changes")]
    public void ChoosesTheEnglishCategory(int count, string expected) =>
        Assert.Equal(expected, PluralText.Format(Changes, count, CultureInfo.InvariantCulture));

    [Fact]
    public void AnExactBranchBeatsTheCategory() =>
        Assert.Equal("No changes", PluralText.Format("{0, plural, =0 {No changes} one {# change} other {# changes}}", 0));

    [Theory]
    [InlineData("{0, plural, one {# change}}")]
    [InlineData("{0, plural, one {# change} other {# changes} junk}")]
    [InlineData("{0, plural, one {# change} other {# changes} one {again}}")]
    [InlineData("{0, plural, one {# change other {# changes}}")]
    [InlineData("{0, plural, one {# change} other {# {x} changes}}")]
    public void AMalformedPatternIsRejected(string pattern) =>
        Assert.Throws<FormatException>(() => PluralText.Validate(pattern));

    [Theory]
    [InlineData("fr", 0, "one"), InlineData("fr", 1, "one"), InlineData("fr", 2, "other")]
    [InlineData("fr", 1_000_000, "many"), InlineData("pt", 0, "one"), InlineData("pt", 5, "other")]
    [InlineData("es", 0, "other"), InlineData("es", 1, "one"), InlineData("es", 2_000_000, "many")]
    [InlineData("ru", 1, "one"), InlineData("ru", 2, "few"), InlineData("ru", 5, "many"),
        InlineData("ru", 11, "many"), InlineData("ru", 21, "one"), InlineData("ru", 22, "few"),
        InlineData("ru", 0, "many"), InlineData("ru", 112, "many")]
    [InlineData("pl", 1, "one"), InlineData("pl", 2, "few"), InlineData("pl", 5, "many"),
        InlineData("pl", 12, "many"), InlineData("pl", 22, "few"), InlineData("pl", 21, "many")]
    [InlineData("ar", 0, "zero"), InlineData("ar", 1, "one"), InlineData("ar", 2, "two"),
        InlineData("ar", 3, "few"), InlineData("ar", 10, "few"), InlineData("ar", 11, "many"),
        InlineData("ar", 99, "many"), InlineData("ar", 100, "other"), InlineData("ar", 103, "few")]
    [InlineData("hi", 0, "one"), InlineData("hi", 1, "one"), InlineData("hi", 2, "other")]
    [InlineData("ja", 1, "other"), InlineData("zh", 1, "other"), InlineData("ko", 1, "other")]
    [InlineData("en", 1, "one"), InlineData("en", 0, "other"), InlineData("de", 2, "other")]
    public void ChoosesTheCldrCategory(string language, int count, string expected) =>
        Assert.Equal(expected, PluralText.CategoryOf(count, language));

    // FieldWorks' UI languages: Build/Installer.legacy.targets ExpectedL10nLocales, plus English.
    private static readonly string[] FieldWorksUiLanguages =
    [
        "en", "ar", "az", "bn", "de", "es", "fa", "fr", "hi", "hu", "id", "km", "ko", "ml", "ms", "my", "ne", "pt", "ru",
        "rw", "sw", "ta", "te", "th", "tr", "ur", "vi", "yo", "zh", "zlm",
    ];

    [Fact]
    public void EveryFieldWorksUiLanguageHasARule() =>
        Assert.Empty(FieldWorksUiLanguages.Except(PluralText.SupportedLanguages, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void ARegionalCultureUsesItsParentRuleAndAnUnknownOneUsesOther()
    {
        Assert.Equal("few", PluralText.Format("{0, plural, few {few} other {other}}", 3, new CultureInfo("ru-RU")));
        Assert.Equal("other", PluralText.Format("{0, plural, one {one} other {other}}", 1, new CultureInfo("zh-CN")));
        Assert.Equal("one", PluralText.Format("{0, plural, one {one} other {other}}", 1, CultureInfo.InvariantCulture));
        Assert.Equal("other", PluralText.Format("{0, plural, one {one} other {other}}", 1, new CultureInfo("ka-GE")));
    }

    [Fact]
    public void EveryResxPluralPatternIsValid()
    {
        foreach (var property in typeof(MotifAppStrings).GetProperties().Where(p => p.Name.EndsWith("Pattern", StringComparison.Ordinal)))
            PluralText.Validate((string)property.GetValue(null)!);
    }

    [Fact]
    public void AValueWithoutAPatternIsNotOneAndStaysAPlainFormat()
    {
        Assert.False(PluralText.IsPattern("Undo change to {0}"));
        Assert.Throws<FormatException>(() => PluralText.Validate("Undo change to {0}"));
        Assert.Equal("Undo change to Gloss", string.Format(CultureInfo.InvariantCulture, "Undo change to {0}", "Gloss"));
    }
}
