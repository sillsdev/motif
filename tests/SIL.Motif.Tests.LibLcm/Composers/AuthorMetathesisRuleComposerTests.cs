using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AuthorMetathesisRuleComposerTests(PristineProjectFixture pristine)
{
    private LcmCache Project()
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "d", "h", "m", "a");
        return cache;
    }

    private static PhonologicalRuleItem Seg(LcmCache cache, string text) => new(Phoneme: RealParserReadings.Phonemes(cache)[text]);

    private static EnvironmentContext Ctx(LcmCache cache, string text) => new(Phoneme: RealParserReadings.Phonemes(cache)[text]);

    private static CanonicalId Author(LcmCache cache, AuthorMetathesisRuleIntent intent) =>
        Created(ApplyOps(cache, AuthorMetathesisRuleComposer.Build(cache, intent)), PhPhonDataPhonRulesOperationKinds.Create);

    private static IPhMetathesisRule Rule(LcmCache cache, CanonicalId id) => (IPhMetathesisRule)Get<IPhSegmentRule>(cache, id);

    [Fact]
    public void AMetathesisRuleStoresItsSwitchesAndEnvironmentsTheWayFieldWorksReadsThem()
    {
        using var cache = Project();
        var id = Author(cache, new("h and C switch", PhonologicalRuleDirection.Simultaneous,
            [Ctx(cache, "a")], Seg(cache, "h"), null, Seg(cache, "m"), [Ctx(cache, "a")]));
        var rule = Rule(cache, id);
        Assert.False(rule.Disabled);
        Assert.Equal(2, rule.Direction);
        Assert.Equal(4, rule.StrucDescOS.Count);
        Assert.Equal(0, rule.LeftEnvIndex);
        Assert.Equal(1, rule.LeftSwitchIndex);
        Assert.Equal(-1, rule.MiddleIndex);
        Assert.Equal(2, rule.RightSwitchIndex);
        Assert.Equal(3, rule.RightEnvIndex);
        Assert.Equal("0 2 -1 1 3", rule.StrucChange.Text);
    }

    [Fact]
    public void AMiddleContextSitsBetweenTheSwitchesAndRemembersItsSide()
    {
        using var cache = Project();
        var id = Author(cache, new("with middle", PhonologicalRuleDirection.Simultaneous, [], Seg(cache, "h"),
            Seg(cache, "a"), Seg(cache, "m"), [], MiddleWithLeft: false));
        var rule = Rule(cache, id);
        Assert.Equal(-1, rule.LeftEnvIndex);
        Assert.Equal(0, rule.LeftSwitchIndex);
        Assert.Equal(1, rule.MiddleIndex);
        Assert.Equal(2, rule.RightSwitchIndex);
        Assert.Equal(-1, rule.RightEnvIndex);
        Assert.False(rule.IsMiddleWithLeftSwitch);
    }

    [Fact]
    public void ARuleKeepsItsPlacementAfterAnExistingRule()
    {
        using var cache = Project();
        var first = Author(cache, new("first", PhonologicalRuleDirection.Simultaneous, [], Seg(cache, "h"), null,
            Seg(cache, "m"), []));
        var second = Author(cache, new("second", PhonologicalRuleDirection.Simultaneous, [], Seg(cache, "m"), null,
            Seg(cache, "d"), [], new Placement(first, null)));
        Assert.Equal([first, second], cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select(Id).ToArray());
    }

    [Fact]
    public void ARuleWithoutAFeatureChangeOrAnOptionalEnvironmentItemIsRefused()
    {
        using var cache = Project();
        var change = new PhonologicalRuleItem(Change: []);
        Assert.Throws<InvalidOperationException>(() => AuthorMetathesisRuleComposer.Build(cache,
            new("bad", PhonologicalRuleDirection.Simultaneous, [], change, null, Seg(cache, "m"), [])));
        var optional = new EnvironmentContext(Phoneme: RealParserReadings.Phonemes(cache)["a"], Minimum: 0, Maximum: 1);
        Assert.Throws<InvalidOperationException>(() => AuthorMetathesisRuleComposer.Build(cache,
            new("bad", PhonologicalRuleDirection.Simultaneous, [optional], Seg(cache, "h"), null, Seg(cache, "m"), [])));
    }

    [Fact]
    public void TheClosedIntentNeedsBothSwitchesAndRejectsUnknownNotation()
    {
        using var cache = Project();
        var h = RealParserReadings.Phonemes(cache)["h"].Value;
        var m = RealParserReadings.Phonemes(cache)["m"].Value;
        object Sw(string id) => new { phoneme = id };
        var good = AuthorMetathesisRuleIntentParser.Parse(Json(JsonSerializer.Serialize(new
        {
            name = "r", direction = "simultaneous", leftSwitch = Sw(h), rightSwitch = Sw(m),
        })));
        Assert.Empty(good.Left);
        Assert.Null(good.Middle);
        void Refused(object intent) => Assert.Throws<ContractParseException>(() =>
            AuthorMetathesisRuleIntentParser.Parse(Json(JsonSerializer.Serialize(intent))));
        Refused(new { name = "r", direction = "simultaneous", leftSwitch = Sw(h) });
        Refused(new { name = "r", direction = "simultaneous", leftSwitch = Sw(h), rightSwitch = Sw(m), output = Array.Empty<object>() });
        Refused(new { name = "r", direction = "simultaneous", leftSwitch = new { phoneme = h, naturalClass = h }, rightSwitch = Sw(m) });
        Refused(new { name = "r", direction = "simultaneous", middleWith = "left", leftSwitch = Sw(h), rightSwitch = Sw(m) });
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
