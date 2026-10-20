using System.Globalization;
using System.Reflection;
using System.Resources;
using SIL.Motif.App;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the .resx localization strategy as AvaloniaLocalizationTests does in FieldWorks: every accessor resolves from
/// the neutral resx, every resx key has an accessor, and a culture with no satellite falls back to English.
/// </summary>
public sealed class MotifAppStringsTests
{
    private static readonly PropertyInfo[] Accessors = typeof(MotifAppStrings)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType == typeof(string))
        .ToArray();

    [Fact]
    public void EveryAccessorResolvesFromTheNeutralResx()
    {
        Assert.NotEmpty(Accessors);
        foreach (var accessor in Accessors)
        {
            var value = (string?)accessor.GetValue(null);
            Assert.False(string.IsNullOrEmpty(value), $"{accessor.Name} needs a neutral resx entry");
            Assert.False(value.StartsWith("MotifApp.", StringComparison.Ordinal),
                $"{accessor.Name} resolved to its id, so its resx entry is missing or renamed");
        }
    }

    [Fact]
    public void EveryResxKeyHasAnAccessor()
    {
        var manager = new ResourceManager("SIL.Motif.App.MotifAppStrings", typeof(MotifAppStrings).Assembly);
        var keys = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).Order().ToArray();
        var used = Accessors.Select(a => "MotifApp." + a.Name).Order().ToArray();
        Assert.Equal(keys, used);
    }

    [Fact]
    public void AccessorsFallBackToEnglishForACultureWithNoSatellite()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            Assert.Equal("Cancel", MotifAppStrings.Cancel);
            Assert.Equal("Refresh", MotifAppStrings.Refresh);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void CommandsAndLabelsAreTitleCaseAndMessagesAreSentenceCase()
    {
        Assert.Equal("See What Changed", MotifAppStrings.SeeWhatChanged);
        Assert.Equal("Parse the words you chose.", MotifAppStrings.ParseAllWordsTip);
    }

    [Fact]
    public void EveryPluralValueIsAPatternAndNoOtherValueIs()
    {
        foreach (var accessor in Accessors)
        {
            var value = (string)accessor.GetValue(null)!;
            Assert.Equal(accessor.Name.EndsWith("Pattern", StringComparison.Ordinal), PluralText.IsPattern(value));
        }
    }
}
