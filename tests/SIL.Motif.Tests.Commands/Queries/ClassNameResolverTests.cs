using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A class may be written by its full name; PanGloss knows abbreviations, so Motif maps the name after PanGloss answers unknown_natural_class.</summary>
public sealed class ClassNameResolverTests
{
    private static readonly PhonValue[] Classes =
    [
        PhonValues.ClassValue("Vowels", "V", 3),
        PhonValues.ClassValue("Consonants", "C", 22),
        PhonValues.ClassValue("Exact featured V", "F-V", 3),
        PhonValues.ClassValue("Nasals", "N", 3),
        PhonValues.ClassValue("Stops", "STOP", 5),
        PhonValues.ClassValue("Stops", "STP2", 5),
    ];

    private sealed class Knows(params string[] abbreviations) : IPhonMatcher
    {
        public string? ParserVersion => "0.28.1";

        public List<string> Sent { get; } = [];

        public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            Sent.Add(patternText);
            var unknown = System.Text.RegularExpressions.Regex.Matches(patternText, @"\[([^\]]*)\]").Select(m => m.Groups[1].Value)
                .FirstOrDefault(name => !abbreviations.Contains(name));
            if (unknown is not null)
                throw ResidentPhonMatcher.Refused(new SIL.Motif.Host.PanGloss.WarmMatchResult.Rejected("unknown_natural_class",
                    $"no natural class named \"{unknown}\"", RecordedMatch.UnknownClass), patternText);
            return new PhonBatchResult([.. words.Select(word => new PhonWordResult(word, "ok", [], null, null, null))], true, null);
        }
    }

    private static ClassNameResolver Resolver(Knows inner, int[]? reads = null) =>
        new(inner, () =>
        {
            if (reads is not null) reads[0]++;
            return Classes;
        });

    [Fact]
    public void AFullNameBecomesItsAbbreviationAfterPanGlossRefusesIt()
    {
        var inner = new Knows("V", "C");
        var reads = new[] { 0 };
        var resolver = Resolver(inner, reads);
        resolver.Match("[consonants] [VOWELS]", ["pata"], CancellationToken.None);
        Assert.Equal(["[consonants] [VOWELS]", "[C] [V]"], inner.Sent);
        resolver.Match("[consonants] [VOWELS]", ["kaki"], CancellationToken.None);
        Assert.Equal(1, reads[0]);
        Assert.Equal("[C] [V]", inner.Sent[^1]);
    }

    [Fact]
    public void AnAbbreviationIsNeverReadAsAName()
    {
        var inner = new Knows("V", "C");
        var reads = new[] { 0 };
        Resolver(inner, reads).Match("[C] [V]", ["pata"], CancellationToken.None);
        Assert.Equal(["[C] [V]"], inner.Sent);
        Assert.Equal(0, reads[0]);
    }

    [Fact]
    public void ANameWithSpacesInBracketsResolves()
    {
        var inner = new Knows("F-V");
        Resolver(inner).Match("[Exact featured V]", ["a"], CancellationToken.None);
        Assert.Equal("[F-V]", inner.Sent[^1]);
    }

    [Fact]
    public void ANameTwoClassesShareIsRefusedWithBothListed()
    {
        var refused = Assert.Throws<QueryRefusalException>(() => Resolver(new Knows("V")).Match("[stops]", ["a"], CancellationToken.None));
        Assert.Equal(RefusalCodes.QueryPhonPatternRefused, refused.Refusal.Code);
        Assert.Contains("STOP (Stops)", refused.Refusal.Message);
        Assert.Contains("STP2 (Stops)", refused.Refusal.Message);
    }

    [Fact]
    public void AnUnknownNameIsRefusedWithTheClosestAbbreviationsAndNames()
    {
        var refused = Assert.Throws<QueryRefusalException>(() => Resolver(new Knows("V")).Match("[Vowel]", ["a"], CancellationToken.None));
        Assert.Equal("unknown_natural_class", refused.Refusal.Facts["panglossCode"]);
        Assert.Contains("closest: V (Vowels)", refused.Refusal.Message);
    }

    [Fact]
    public void SuggestionsAreCappedAtFive()
    {
        var many = Enumerable.Range(0, 12).Select(i => PhonValues.ClassValue("Class" + i, "K" + i, 1)).ToList();
        Assert.Equal(5, ClassNameResolver.Closest("Clas", many).Count);
    }

    [Fact]
    public void ATypoWithinAFewEditsIsSuggested()
    {
        Assert.Equal("Consonants", ClassNameResolver.Closest("Consonnts", Classes).First().Name);
    }

    [Fact]
    public void AFeatureBundleIsLeftAlone()
    {
        Assert.Equal("[+voice -son] [V]", ClassNameResolver.Rewrite("[+voice -son] [V]", Classes));
    }
}
