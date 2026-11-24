using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A class is checked against PanGloss's inventory after PanGloss answers unknown_natural_class: a different case or an id resolves, anything else is refused with the closest labels.</summary>
public sealed class ClassNameResolverTests
{
    private static readonly PhonInventoryClass[] Classes =
    [
        new("V", "V", "ncV", "segment"),
        new("C", "C", "ncC", "segment"),
        new("Nasal Stop", "Nasal Stop", "ncNas", "segment"),
        new("Voiced", "Voiced", "ncVoiced", "feature"),
        new("ncV2", "V", "ncV2", "feature"),
        new("STP", "Stop", "ncSTP", "segment"),
        new("STP2", "Stop", "ncSTP2", "segment"),
    ];

    private sealed class Knows(IReadOnlyList<PhonInventoryClass>? classes = null, Exception? readFails = null) : IPhonMatcher
    {
        public string? ParserVersion => "0.30.0";

        public List<string> Sent { get; } = [];

        public int Reads { get; private set; }

        public PhonInventory? Inventory(CancellationToken cancellationToken)
        {
            Reads++;
            if (readFails is not null) throw readFails;
            return classes is null ? null : new PhonInventory(classes, []);
        }

        public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken)
        {
            Sent.Add(patternText);
            var known = (classes ?? Classes).SelectMany(c => new[] { c.Token, c.Name }).Concat(["V", "C"]).Where(t => t is not null).ToHashSet();
            var unknown = System.Text.RegularExpressions.Regex.Matches(patternText, @"\[([^\]]*)\]").Select(m => m.Groups[1].Value)
                .FirstOrDefault(name => !known.Contains(name));
            if (unknown is not null)
                throw ResidentPhonMatcher.Refused(new SIL.Motif.Host.PanGloss.WarmMatchResult.Rejected("unknown_natural_class",
                    $"no natural class named \"{unknown}\"", RecordedMatch.UnknownClass), patternText);
            return new PhonBatchResult([.. words.Select(word => new PhonWordResult(word, "ok", [], null, null, null))], true, null);
        }
    }

    [Fact]
    public void ADifferentCaseBecomesTheTokenAfterPanGlossRefusesItAndTheClassesAreReadOnce()
    {
        var inner = new Knows(Classes);
        var resolver = new ClassNameResolver(inner);
        resolver.Match("[c] [VOICED]", ["pata"], CancellationToken.None);
        Assert.Equal(["[c] [VOICED]", "[C] [Voiced]"], inner.Sent);
        resolver.Match("[c] [VOICED]", ["kaki"], CancellationToken.None);
        Assert.Equal(1, inner.Reads);
        Assert.Equal("[C] [Voiced]", inner.Sent[^1]);
    }

    [Fact]
    public void ATokenIsNeverReadAsAName()
    {
        var inner = new Knows(Classes);
        new ClassNameResolver(inner).Match("[C] [V]", ["pata"], CancellationToken.None);
        Assert.Equal(["[C] [V]"], inner.Sent);
        Assert.Equal(0, inner.Reads);
    }

    [Fact]
    public void ANameWithSpacesInBracketsResolvesByCase()
    {
        var inner = new Knows(Classes);
        new ClassNameResolver(inner).Match("[nasal stop]", ["a"], CancellationToken.None);
        Assert.Equal("[Nasal Stop]", inner.Sent[^1]);
    }

    [Fact]
    public void ANameTwoClassesShareIsRefusedWithBothListed()
    {
        var refused = Assert.Throws<QueryRefusalException>(() => new ClassNameResolver(new Knows(Classes)).Match("[stop]", ["a"], CancellationToken.None));
        Assert.Equal(RefusalCodes.QueryPhonPatternRefused, refused.Refusal.Code);
        Assert.Contains("STP (Stop)", refused.Refusal.Message);
        Assert.Contains("STP2 (Stop)", refused.Refusal.Message);
    }

    [Fact]
    public void AnUnknownNameIsRefusedWithTheClosestLabels()
    {
        var refused = Assert.Throws<QueryRefusalException>(() => new ClassNameResolver(new Knows(Classes)).Match("[Vowels]", ["a"], CancellationToken.None));
        Assert.Equal("unknown_natural_class", refused.Refusal.Facts["panglossCode"]);
        Assert.Contains("closest: V", refused.Refusal.Message);
    }

    [Fact]
    public void ALiteralIsNeverRewrittenEvenWhenItsTextIsAClassNameInBrackets()
    {
        var classes = new PhonInventoryClass[] { new("Stop", "Stop", "ncS", "segment"), new("[b]", "[b]", "ncB", "segment") };
        Assert.Equal("/[stop]/ [Stop]", ClassNameResolver.Rewrite("/[stop]/ [stop]", classes));
        Assert.Equal("[Stop] / # _ /[stop]/", ClassNameResolver.Rewrite("[stop] / # _ /[stop]/", classes));
    }

    [Fact]
    public void AFailedClassReadIsSaidInTheRefusalNotSwallowed()
    {
        var failure = QueryRefusalException.PhonFailed("inventory timed out");
        var refused = Assert.Throws<QueryRefusalException>(() =>
            new ClassNameResolver(new Knows(readFails: failure)).Match("[Nope]", ["a"], CancellationToken.None));
        Assert.Contains("could not read the grammar's natural classes", refused.Refusal.Message);
        Assert.Contains("inventory timed out", refused.Refusal.Message);
        Assert.Equal("unknown_natural_class", refused.Refusal.Facts["panglossCode"]);
    }

    [Fact]
    public void AnEmptyClassListIsSaidInTheRefusal()
    {
        var refused = Assert.Throws<QueryRefusalException>(() =>
            new ClassNameResolver(new Knows([])).Match("[Nope]", ["a"], CancellationToken.None));
        Assert.Contains("lists no natural classes", refused.Refusal.Message);
    }

    [Fact]
    public void SuggestionsAreCappedAtFive()
    {
        var many = Enumerable.Range(0, 12).Select(i => new PhonInventoryClass("Class" + i, "Class" + i, "id" + i, "segment")).ToList();
        Assert.Equal(5, ClassNameResolver.Closest("Clas", many).Count);
    }

    [Fact]
    public void ATypoWithinAFewEditsIsSuggested()
    {
        var classes = new PhonInventoryClass[] { new("Consonants", "Consonants", "a", "segment"), new("V", "V", "b", "segment") };
        Assert.Equal("Consonants", ClassNameResolver.Closest("Consonnts", classes).First().Name);
    }

    [Fact]
    public void AFeatureBundleIsLeftAlone()
    {
        Assert.Equal("[+voice -son] [V]", ClassNameResolver.Rewrite("[+voice -son] [V]", Classes));
    }
}
