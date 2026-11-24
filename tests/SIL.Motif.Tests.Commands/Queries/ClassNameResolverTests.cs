using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A bracketed class name is checked against PanGloss's inventory before the pattern is sent: a full FieldWorks name, a different case or an id resolves to the token, anything else is sent and refused with the closest names.</summary>
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

        public PhonBatchResult Match(string patternText, IReadOnlyList<string> words, string segmentTest, CancellationToken cancellationToken)
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
    public void ADifferentCaseBecomesTheTokenBeforeTheSendAndTheClassesAreReadOnce()
    {
        var inner = new Knows(Classes);
        var resolver = new ClassNameResolver(inner);
        resolver.Match("[c] [VOICED]", ["pata"], "engine", CancellationToken.None);
        Assert.Equal(["[C] [Voiced]"], inner.Sent);
        resolver.Match("[c] [VOICED]", ["kaki"], "engine", CancellationToken.None);
        Assert.Equal(1, inner.Reads);
        Assert.Equal("[C] [Voiced]", inner.Sent[^1]);
    }

    [Fact]
    public void ATokenIsNeverReadAsAName()
    {
        var inner = new Knows(Classes);
        new ClassNameResolver(inner).Match("[C] [V]", ["pata"], "engine", CancellationToken.None);
        Assert.Equal(["[C] [V]"], inner.Sent);
        Assert.Equal(1, inner.Reads);
        new ClassNameResolver(inner).Match("/p/ #", ["pa"], "engine", CancellationToken.None);
        Assert.Equal(1, inner.Reads);
    }

    private static readonly PhonInventoryClass[] Named =
    [
        new("V", "V", "ncV", "segment", "Vowels"),
        new("C", "C", "ncC", "segment", "Consonants"),
        new("ShV", "ShV", "ncShV", "segment", "Short Vowels"),
        new("T", "T", "ncT", "segment", "Vowels Tones"),
        new("Vowels", "Vowels", "ncVowels", "segment", "Syllable Nuclei"),
    ];

    [Fact]
    public void AFullNameResolvesToTheTokenBeforeTheSendIgnoringCaseAndMayHoldSpaces()
    {
        var inner = new Knows(Named);
        var resolver = new ClassNameResolver(inner);
        resolver.Match("[Short Vowels] [consonants]", ["pata"], "engine", CancellationToken.None);
        resolver.Match("[VOWELS TONES] / [Consonants] _", ["pata"], "engine", CancellationToken.None);
        Assert.Equal(["[ShV] [C]", "[T] / [C] _"], inner.Sent);
        Assert.Equal(1, inner.Reads);
    }

    [Fact]
    public void ANameThatIsATokenIsThatTokenEvenWhenAnotherClassHasItAsItsFullName()
    {
        var inner = new Knows(Named);
        new ClassNameResolver(inner).Match("[Vowels]", ["a"], "engine", CancellationToken.None);
        Assert.Equal(["[Vowels]"], inner.Sent);
    }

    [Fact]
    public void AFullNameTwoClassesShareIsRefusedBeforeAnythingIsSent()
    {
        var inner = new Knows([new("V", "V", "ncV", "segment", "Vowels"), new("W", "W", "ncW", "segment", "vowels")]);
        var refused = Assert.Throws<QueryRefusalException>(() => new ClassNameResolver(inner).Match("[VOWELS]", ["a"], "engine", CancellationToken.None));
        Assert.Equal(RefusalCodes.QueryPhonPatternRefused, refused.Refusal.Code);
        Assert.Contains("V (Vowels)", refused.Refusal.Message);
        Assert.Contains("W (vowels)", refused.Refusal.Message);
        Assert.Empty(inner.Sent);
    }

    [Fact]
    public void SuggestionsConsiderTheFullNamesToo()
    {
        var refused = Assert.Throws<QueryRefusalException>(() => new ClassNameResolver(new Knows(Named)).Match("[Consonant]", ["a"], "engine", CancellationToken.None));
        Assert.Equal("unknown_natural_class", refused.Refusal.Facts["panglossCode"]);
        Assert.Contains("C (Consonants)", refused.Refusal.Message);
    }

    [Fact]
    public void TheRealFieldWorksFixtureResolvesItsFullNameToItsToken()
    {
        var inventory = ResidentPhonMatcher.ReadInventory(RecordedMatch.InventoryWithFullName);
        var inner = new Knows(inventory.Classes);
        new ClassNameResolver(inner).Match("[vowels] [Any]", ["a"], "engine", CancellationToken.None);
        Assert.Equal(["[V] [Any]"], inner.Sent);
    }

    [Fact]
    public void ANameWithSpacesInBracketsResolvesByCase()
    {
        var inner = new Knows(Classes);
        new ClassNameResolver(inner).Match("[nasal stop]", ["a"], "engine", CancellationToken.None);
        Assert.Equal("[Nasal Stop]", inner.Sent[^1]);
    }

    [Fact]
    public void ANameTwoClassesShareIsRefusedWithBothListed()
    {
        var refused = Assert.Throws<QueryRefusalException>(() => new ClassNameResolver(new Knows(Classes)).Match("[stop]", ["a"], "engine", CancellationToken.None));
        Assert.Equal(RefusalCodes.QueryPhonPatternRefused, refused.Refusal.Code);
        Assert.Contains("STP (Stop)", refused.Refusal.Message);
        Assert.Contains("STP2 (Stop)", refused.Refusal.Message);
    }

    [Fact]
    public void AnUnknownNameIsRefusedWithTheClosestLabels()
    {
        var refused = Assert.Throws<QueryRefusalException>(() => new ClassNameResolver(new Knows(Classes)).Match("[Vowels]", ["a"], "engine", CancellationToken.None));
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
            new ClassNameResolver(new Knows(readFails: failure)).Match("[Nope]", ["a"], "engine", CancellationToken.None));
        Assert.Contains("could not read the grammar's natural classes", refused.Refusal.Message);
        Assert.Contains("inventory timed out", refused.Refusal.Message);
        Assert.Equal("unknown_natural_class", refused.Refusal.Facts["panglossCode"]);
    }

    [Fact]
    public void AnEmptyClassListIsSaidInTheRefusal()
    {
        var refused = Assert.Throws<QueryRefusalException>(() =>
            new ClassNameResolver(new Knows([])).Match("[Nope]", ["a"], "engine", CancellationToken.None));
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

    // Recorded v0.30.0 replies, edited only by removing the op or the rows.
    [Fact]
    public void ADescribeWithoutTheInventoryOpRefusesTheClassListAndNameResolutionSaysSo()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(SIL.Motif.Tests.PanGloss.WhyNotRecordedTests.Recorded("describe"))!;
        var interfaces = node["interfaces"] as System.Text.Json.Nodes.JsonObject ?? (System.Text.Json.Nodes.JsonObject)(node["interfaces"] = new System.Text.Json.Nodes.JsonObject());
        var ops = interfaces["serveOps"] as System.Text.Json.Nodes.JsonObject ?? (System.Text.Json.Nodes.JsonObject)(interfaces["serveOps"] = new System.Text.Json.Nodes.JsonObject());
        ops["match"] = System.Text.Json.Nodes.JsonNode.Parse(RecordedMatch.Describe);
        ops.Remove("inventory");
        var capabilities = PanGlossSurface.ReadDescription(node.ToJsonString(), out _)!;
        var detail = Assert.IsType<string>(capabilities.ServeInventoryRefusal());
        var refusal = Assert.Throws<QueryRefusalException>(() =>
            ResidentPhonMatcher.InventoryFrom(new WarmMatchResult.Unsupported(detail), CancellationToken.None));
        Assert.Equal(RefusalCodes.QueryPhonUnavailable, refusal.Refusal.Code);
        Assert.Contains("interfaces.serveOps.inventory", refusal.Refusal.Message);
        var resolved = Assert.Throws<QueryRefusalException>(() =>
            new ClassNameResolver(new Knows(readFails: refusal)).Match("[Nope]", ["a"], "engine", CancellationToken.None));
        Assert.Contains("could not read the grammar's natural classes", resolved.Refusal.Message);
    }

    [Fact]
    public void ARecordedInventoryWithNoClassRowsGivesTheNoClassListMessage()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(RecordedMatch.Inventory)!;
        node["classes"] = new System.Text.Json.Nodes.JsonArray();
        var empty = ResidentPhonMatcher.ReadInventory(node.ToJsonString());
        Assert.Empty(empty.Classes);
        var refused = Assert.Throws<QueryRefusalException>(() =>
            new ClassNameResolver(new Knows(empty.Classes)).Match("[Nope]", ["a"], "engine", CancellationToken.None));
        Assert.Contains("lists no natural classes", refused.Refusal.Message);
        node["complete"] = false;
        node["stopReason"] = "work_limit";
        Assert.False(ResidentPhonMatcher.ReadInventory(node.ToJsonString()).Complete);
    }
}
