using System.Text.Json;
using SIL.Motif.Contract.Parsimony.RuleAttempts;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony.RuleAttempts;

public sealed class RuleAttemptCheckTests
{
    private sealed class MemoryLedger : IRuleAttemptLedger
    {
        private readonly Dictionary<string, RuleAttempt> _byShape = [];
        public void Record(RuleAttempt attempt) => _byShape[attempt.ShapeKey] = attempt;
        public RuleAttempt? Find(string shapeKey) => _byShape.GetValueOrDefault(shapeKey);
    }

    private static AddedObjectShape Variant(string id, string main, string variant, string type = "free",
        string[]? env = null, string[]? feat = null) =>
        new(id, type, env ?? ["env1"], feat ?? [], StringCorrespondence.Of(main, variant));

    private static List<AddedObjectShape> FinalVowelLoss(int n) =>
        [.. Enumerable.Range(0, n).Select(i => Variant($"v{i}", "kata" + i + "a", "kata" + i + "-"))];

    [Theory]
    [InlineData("angka", "angk", "V>@final")]
    [InlineData("ayri", "ayr", "V>@final")]
    [InlineData("radiyu", "radyu", "V>@medial")]
    [InlineData("sustansiya", "sustansya", "V>@medial")]
    [InlineData("angka", "angk-", "V>@final+hyphen")]
    public void CorrespondenceWritesVowelsAsV(string main, string variant, string key) =>
        Assert.Equal(key, StringCorrespondence.Of(main, variant).Key);

    [Fact]
    public void FourOfOneShapeNeedNoAttemptAndFiveDo()
    {
        Assert.Empty(RuleAttemptCheck.Evaluate(FinalVowelLoss(4), new MemoryLedger()));
        var finding = Assert.Single(RuleAttemptCheck.Evaluate(FinalVowelLoss(5), new MemoryLedger()));
        Assert.True(finding.Hold);
        Assert.Equal(5, finding.Group.Count);
    }

    [Fact]
    public void DifferentEnvironmentFeatureOrVariantTypeSplitsTheGroup()
    {
        var mixed = FinalVowelLoss(3);
        mixed.AddRange(Enumerable.Range(0, 3).Select(i => Variant($"e{i}", "bata", "bat-", env: ["env2"])));
        mixed.AddRange(Enumerable.Range(0, 3).Select(i => Variant($"f{i}", "bata", "bat-", feat: ["f1"])));
        mixed.AddRange(Enumerable.Range(0, 3).Select(i => Variant($"t{i}", "bata", "bat-", type: "spelling")));
        Assert.Empty(RuleAttemptCheck.Evaluate(mixed, new MemoryLedger()));
    }

    [Fact]
    public void ACompleteRecordedAttemptReleasesTheHold()
    {
        var ledger = new MemoryLedger();
        var key = FinalVowelLoss(5)[0].Key;
        ledger.Record(new RuleAttempt(key, "a drops word-finally", "64 last-reading losses", "attested losses"));
        Assert.False(Assert.Single(RuleAttemptCheck.Evaluate(FinalVowelLoss(5), ledger)).Hold);
    }

    [Fact]
    public void AnIncompleteAttemptStillHolds()
    {
        var ledger = new MemoryLedger();
        ledger.Record(new RuleAttempt(FinalVowelLoss(5)[0].Key, "a drops", " ", "attested losses"));
        Assert.True(Assert.Single(RuleAttemptCheck.Evaluate(FinalVowelLoss(5), ledger)).Hold);
    }

    [Fact]
    public void WithNoLedgerEveryLargeGroupHoldsAndRecordingIsRefused()
    {
        Assert.True(Assert.Single(RuleAttemptCheck.Evaluate(FinalVowelLoss(6), NoRuleAttemptLedger.Instance)).Hold);
        Assert.Throws<NotSupportedException>(() =>
            NoRuleAttemptLedger.Instance.Record(new RuleAttempt("k", "r", "t", "w")));
    }

    [Fact]
    public void IntentsAreReadFromAPlanShape()
    {
        var forms = new Dictionary<string, string> { ["m1"] = "angka", ["m2"] = "ayri", ["e1"] = "kuha" };
        var intents = new[] { "angk", "ayr", "angk" }.Select((form, i) => JsonDocument.Parse(
            $$$"""{"verb":"compose-author-variant-entry","intent":{"component":"m{{{i % 2 + 1}}}","variantType":"T","form":"{{{form}}}","environments":["E"]}}""")
            .RootElement).ToList();
        intents.Add(JsonDocument.Parse("""{"verb":"compose-make-variant-of","intent":{"entry":"e1","component":"m1","variantType":"T"}}""").RootElement);
        intents.Add(JsonDocument.Parse("""{"verb":"compose-other","intent":{}}""").RootElement);
        var shapes = VariantIntentShapes.FromIntents(intents, id => forms.GetValueOrDefault(id));
        Assert.Equal(4, shapes.Count);
        Assert.Equal(3, shapes.Count(s => s.Correspondence.Key == "V>@final" && s.EnvironmentIds.Count == 1));
    }
}
