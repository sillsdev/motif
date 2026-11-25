using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>The per-affix repeat count stored as HC/AffixRules/AffixRule in ParserParameters.</summary>
[Collection(LcmCacheParallelCollections.Group0)]
public sealed class SetAffixMaxAppsComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;
    private readonly CanonicalId _msa;
    private readonly CanonicalId _stemMsa;
    private readonly Guid _guid;

    public SetAffixMaxAppsComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        _msa = AddInflectionalPrefix(_cache, "mi", _category).Msa;
        _stemMsa = AddStem(_cache, "kata", _category).Msa;
        _guid = _msa.ToGuid();
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private string Parameters => _cache.LangProject.MorphologicalDataOA.ParserParameters;

    private void SetParameters(string xml) =>
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.MorphologicalDataOA.ParserParameters = xml);

    private string Apply(int expected, int count)
    {
        ApplyOps(_cache, SetAffixMaxAppsComposer.Build(_cache, new(_msa, expected, count)));
        return Parameters;
    }

    private static AffixMaxAppsRefusal Refusal(Action build) => Assert.Throws<AffixMaxAppsRefusal>(build);

    [Fact]
    public void ACountOfTwoIsStoredUnderHcAffixRulesKeyedByTheMsaGuidAndKeepsEveryOtherElement()
    {
        SetParameters("<ParserParameters><CompoundRules><CompoundRule guid=\"x\" maxApps=\"3\"/></CompoundRules>" +
            "<HC><Strata>a b</Strata></HC></ParserParameters>");

        var root = XElement.Parse(Apply(1, 2));

        var rule = Assert.Single(root.Element("HC")!.Element("AffixRules")!.Elements("AffixRule"));
        Assert.Equal(_guid.ToString(), rule.Attribute("guid")!.Value);
        Assert.Equal("2", rule.Attribute("maxApps")!.Value);
        Assert.Equal("a b", root.Element("HC")!.Element("Strata")!.Value);
        Assert.Equal("3", root.Element("CompoundRules")!.Element("CompoundRule")!.Attribute("maxApps")!.Value);
        Assert.Equal(2, AffixMaxApps.Read(Parameters, _guid));
    }

    [Fact]
    public void BlankParserParametersGainAHcElementAndAnUnrelatedAffixKeepsItsOwnCount()
    {
        SetParameters("");
        var other = Guid.NewGuid();
        var first = AffixMaxAppsParserParameters.Write(Parameters, other, 3);
        SetParameters(first);

        var root = XElement.Parse(Apply(1, 2));

        Assert.Equal(2, root.Element("HC")!.Element("AffixRules")!.Elements("AffixRule").Count());
        Assert.Equal(3, AffixMaxApps.Read(Parameters, other));
        Assert.Equal(2, AffixMaxApps.Read(Parameters, _guid));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ChangingACountUpdatesTheAttributeInPlaceAndTouchesNothingElseInTheText(string newLine)
    {
        var original = "<ParserParameters>\n  <HC>\n    <AffixRules>\n      <AffixRule guid=\"{0}\" maxApps=\"2\" />\n    </AffixRules>\n    <Strata />\n  </HC>\n</ParserParameters>"
            .Replace("\n", newLine);
        SetParameters(string.Format(original, _guid));

        var changed = Apply(2, 3);

        Assert.Equal(string.Format(original, _guid).Replace("maxApps=\"2\"", "maxApps=\"3\""), changed);
    }

    [Fact]
    public void ACountOfOneRemovesTheElementAndThenTheEmptyAffixRulesElementLeavingTheRestSemanticallyPreserved()
    {
        const string withOther = "<ParserParameters><HC><AffixRules><AffixRule guid=\"00000000-0000-0000-0000-000000000001\" maxApps=\"3\"/>" +
            "<AffixRule guid=\"{0}\" maxApps=\"2\"/></AffixRules><Strata /></HC></ParserParameters>";
        SetParameters(string.Format(withOther, _guid));

        var oneLeft = Apply(2, 1);
        Assert.Equal("<ParserParameters><HC><AffixRules><AffixRule guid=\"00000000-0000-0000-0000-000000000001\" maxApps=\"3\" /></AffixRules><Strata /></HC></ParserParameters>",
            oneLeft);

        const string only = "<ParserParameters><HC><AffixRules><AffixRule guid=\"{0}\" maxApps=\"2\"/></AffixRules><Strata /></HC></ParserParameters>";
        SetParameters(string.Format(only, _guid));
        Assert.Equal("<ParserParameters><HC><Strata /></HC></ParserParameters>", Apply(2, 1));
    }

    [Fact]
    public void SettingTheCountTheProjectAlreadyHoldsChangesNothingAndOneOnAnUnsetAffixLeavesTheTextAlone()
    {
        const string text = "<ParserParameters>\n  <HC><Strata/></HC>\n</ParserParameters>";
        SetParameters(text);
        Assert.Equal(text, Apply(1, 1));

        var two = Apply(1, 2);
        Assert.Equal(two, Apply(2, 2));
    }

    [Fact]
    public void ACountOutsideOneToTheCapIsRefusedWithARegisteredCodeAndTheProjectIsUntouched()
    {
        SetParameters("<ParserParameters/>");
        foreach (var bad in new[] { 0, -1, AffixMaxApps.Cap + 1, 1000 })
        {
            var refusal = Refusal(() => SetAffixMaxAppsComposer.Build(_cache, new(_msa, 1, bad)));
            Assert.Equal(RefusalCodes.AffixMaxAppsInvalidCount, refusal.Code);
            Assert.True(RefusalCatalog.Specs.ContainsKey(refusal.Code));
        }
        Assert.Equal("<ParserParameters/>", Parameters);
        Assert.Equal(AffixMaxApps.Cap.ToString(), XElement.Parse(Apply(1, AffixMaxApps.Cap)).Descendants("AffixRule").Single().Attribute("maxApps")!.Value);
    }

    [Fact]
    public void AnIdThatIsNotAnAffixMsaIsRefusedWithARegisteredCode()
    {
        foreach (var bad in new[] { _stemMsa, CanonicalId.FromGuid(_cache.LangProject.MorphologicalDataOA.Guid), CanonicalId.Mint() })
        {
            var refusal = Refusal(() => SetAffixMaxAppsComposer.Build(_cache, new(bad, 1, 2)));
            Assert.Equal(RefusalCodes.AffixMaxAppsNotAffixMsa, refusal.Code);
            Assert.True(RefusalCatalog.Specs.ContainsKey(refusal.Code));
        }
    }

    [Fact]
    public void AStaleExpectedCountIsRefusedAndMalformedXmlIsReportedWithoutChangingTheProject()
    {
        SetParameters("<ParserParameters/>");
        var stale = Refusal(() => SetAffixMaxAppsComposer.Build(_cache, new(_msa, 3, 2)));
        Assert.Equal(RefusalCodes.AffixMaxAppsStale, stale.Code);
        Assert.Contains("expected maxApps=3", stale.Message, StringComparison.Ordinal);

        SetParameters("<ParserParameters>");
        var malformed = Assert.Throws<InvalidOperationException>(() => SetAffixMaxAppsComposer.Build(_cache, new(_msa, 1, 2)));
        Assert.Contains("not well-formed", malformed.Message, StringComparison.Ordinal);
        Assert.Equal("<ParserParameters>", Parameters);
    }

    [Fact]
    public void ParserSettingsWithAForeignRootAreRefusedAndLeftAlone()
    {
        SetParameters("<Other><HC/></Other>");

        var refusal = Assert.Throws<InvalidOperationException>(() => SetAffixMaxAppsComposer.Build(_cache, new(_msa, 1, 2)));

        Assert.Contains("not <ParserParameters>", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("<Other><HC/></Other>", Parameters);
    }

    private (CanonicalId Unused, CanonicalId Used) TwoEqualAnalysesOnOneEntry(bool senseUsesSecond)
    {
        IMoInflAffMsa first = null!, second = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            first = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
            second = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(first);
            entry.MorphoSyntaxAnalysesOC.Add(second);
            var pos = _cache.ServiceLocator.GetObject(_category.ToGuid()) as IPartOfSpeech;
            first.PartOfSpeechRA = pos;
            second.PartOfSpeechRA = pos;
            if (!senseUsesSecond) return;
            var sense = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = second;
        });
        return (CanonicalId.FromGuid(first.Guid), CanonicalId.FromGuid(second.Guid));
    }

    [Fact]
    public void ACountOnAnEqualNonRepresentativeAnalysisIsWrittenOnTheRepresentativeAndReported()
    {
        var (unused, used) = TwoEqualAnalysesOnOneEntry(senseUsesSecond: true);
        var intent = new SetAffixMaxAppsIntent(unused, 1, 2);

        var operation = Assert.Single(SetAffixMaxAppsComposer.Build(_cache, intent));

        Assert.Equal(used.Value, operation.After!.Value.GetProperty("msa").GetString());
        var related = SetAffixMaxAppsReport.Related(_cache, intent);
        Assert.Contains(related, item => item.Kind == "representative-affix-analysis" && item.Id == used.Value);
        Assert.Contains(related, item => item.Kind == "equal-affix-analysis" && item.Id == unused.Value);
        ApplyOps(_cache, [operation]);
        Assert.Equal(2, AffixMaxApps.Read(Parameters, used.ToGuid()));
        Assert.Equal(1, AffixMaxApps.Read(Parameters, unused.ToGuid()));
        Assert.Equal(2, AffixMaxApps.ReadFor(Get<ICmObject>(_cache, unused)));
    }

    [Fact]
    public void WhenNoSenseUsesEitherEqualAnalysisTheRequestedOneIsItsOwnRepresentative()
    {
        var (unused, other) = TwoEqualAnalysesOnOneEntry(senseUsesSecond: false);

        var operation = Assert.Single(SetAffixMaxAppsComposer.Build(_cache, new(unused, 1, 2)));

        Assert.Equal(unused.Value, operation.After!.Value.GetProperty("msa").GetString());
        Assert.NotEqual(unused, other);
    }

    private (CanonicalId First, CanonicalId Second) DerivationalPairOnOneEntry(bool senseUsesSecond, bool mixClasses = false)
    {
        IMoMorphSynAnalysis first = null!, second = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var pos = _cache.ServiceLocator.GetObject(_category.ToGuid()) as IPartOfSpeech;
            var firstDeriv = _cache.ServiceLocator.GetInstance<IMoDerivAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(firstDeriv);
            firstDeriv.FromPartOfSpeechRA = pos;
            firstDeriv.ToPartOfSpeechRA = pos;
            first = firstDeriv;
            if (mixClasses)
            {
                var infl = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
                entry.MorphoSyntaxAnalysesOC.Add(infl);
                infl.PartOfSpeechRA = pos;
                second = infl;
            }
            else
            {
                var secondDeriv = _cache.ServiceLocator.GetInstance<IMoDerivAffMsaFactory>().Create();
                entry.MorphoSyntaxAnalysesOC.Add(secondDeriv);
                secondDeriv.FromPartOfSpeechRA = pos;
                secondDeriv.ToPartOfSpeechRA = pos;
                second = secondDeriv;
            }
            var sense = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = senseUsesSecond ? second : first;
        });
        return (CanonicalId.FromGuid(first.Guid), CanonicalId.FromGuid(second.Guid));
    }

    [Fact]
    public void EqualDerivationalAnalysesShareTheRepresentativeTheSenseUses()
    {
        var (unused, used) = DerivationalPairOnOneEntry(senseUsesSecond: true);

        var operation = Assert.Single(SetAffixMaxAppsComposer.Build(_cache, new(unused, 1, 3)));

        Assert.Equal(used.Value, operation.After!.Value.GetProperty("msa").GetString());
    }

    [Fact]
    public void AnalysesOfDifferentClassesAreNotMerged()
    {
        var (deriv, infl) = DerivationalPairOnOneEntry(senseUsesSecond: true, mixClasses: true);
        var intent = new SetAffixMaxAppsIntent(deriv, 1, 2);

        var operation = Assert.Single(SetAffixMaxAppsComposer.Build(_cache, intent));

        Assert.Equal(deriv.Value, operation.After!.Value.GetProperty("msa").GetString());
        Assert.Empty(SetAffixMaxAppsReport.Related(_cache, intent));
        Assert.NotEqual(deriv, infl);
    }

    [Fact]
    public void WhenTheFirstEqualAnalysisIsUnusedTheSecondUsedOneIsTheRepresentative()
    {
        var (first, second) = TwoEqualAnalysesOnOneEntry(senseUsesSecond: true);

        var operation = Assert.Single(SetAffixMaxAppsComposer.Build(_cache, new(first, 1, 2)));

        Assert.Equal(second.Value, operation.After!.Value.GetProperty("msa").GetString());
    }

    [Theory]
    [InlineData("<ParserParameters><HC><MergeMSAs>false</MergeMSAs></HC></ParserParameters>", false)]
    [InlineData("<ParserParameters><HC><MergeMSAs>true</MergeMSAs></HC></ParserParameters>", true)]
    [InlineData("<ParserParameters><HC><Strata>a</Strata></HC></ParserParameters>", true)]
    public void MergeMsasInTheProjectDecidesWhetherEqualAnalysesAreMerged(string xml, bool merged)
    {
        SetParameters(xml);
        var (unused, used) = TwoEqualAnalysesOnOneEntry(senseUsesSecond: true);
        var intent = new SetAffixMaxAppsIntent(unused, 1, 2);

        var operation = Assert.Single(SetAffixMaxAppsComposer.Build(_cache, intent));

        Assert.Equal((merged ? used : unused).Value, operation.After!.Value.GetProperty("msa").GetString());
        Assert.Equal(merged, SetAffixMaxAppsReport.Related(_cache, intent).Count > 0);
        ApplyOps(_cache, [operation]);
        Assert.Equal(2, AffixMaxApps.ReadFor(Get<ICmObject>(_cache, unused)));
    }

    [Fact]
    public void TheHandlerRefusesAnInvalidCountAtApplyToo()
    {
        var operation = new OperationEnvelope(CanonicalId.Mint(), MoMorphDataAffixMaxAppsOperationKinds.Set,
            target: CanonicalId.FromGuid(_cache.LangProject.MorphologicalDataOA.Guid),
            after: JsonSerializer.SerializeToElement(new { msa = _msa.Value, maxApps = 99 }), rationale: "test");

        var refusal = Assert.Throws<AffixMaxAppsRefusal>(() => ApplyOps(_cache, [operation]));

        Assert.Equal(RefusalCodes.AffixMaxAppsInvalidCount, refusal.Code);
    }

    [Fact]
    public void TwoAffixesInOneDraftDoNotShareASlotButTheSameAffixTwiceDoes()
    {
        var other = AddInflectionalPrefix(_cache, "wa", _category).Msa;
        var a = SetAffixMaxAppsComposer.Build(_cache, new(_msa, 1, 2)).Single();
        var b = SetAffixMaxAppsComposer.Build(_cache, new(other, 1, 2)).Single();
        var c = SetAffixMaxAppsComposer.Build(_cache, new(_msa, 1, 3)).Single();

        var slotA = Assert.Single(OperationSlotRegistry.Resolve(a));
        var slotB = Assert.Single(OperationSlotRegistry.Resolve(b));
        var slotC = Assert.Single(OperationSlotRegistry.Resolve(c));

        Assert.NotEqual(slotA.Key, slotB.Key);
        Assert.Equal(slotA.Key, slotC.Key);
        ApplyOps(_cache, [a, b]);
        Assert.Equal(2, AffixMaxApps.Read(Parameters, _guid));
        Assert.Equal(2, AffixMaxApps.Read(Parameters, other.ToGuid()));
    }

    [Fact]
    public void ADryRunReportsTheBeforeAndAfterCountAsTheExpectedEffect()
    {
        var operations = SetAffixMaxAppsComposer.Build(_cache, new(_msa, 1, 2));
        var proposal = Proposal(operations);

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.Equal(1, AffixMaxApps.Read(Parameters, _guid));
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoMorphDataAffixMaxApps, effect.Field);
        Assert.Equal("1", effect.Before["maxApps"]);
        Assert.Equal("2", effect.After["maxApps"]);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 3 ", 3)]
    public void ReadsACountTheWayItIsWritten(string text, int expected)
    {
        var guid = Guid.NewGuid();
        Assert.Equal(expected, AffixMaxApps.Read($"<ParserParameters><HC><AffixRules><AffixRule guid=\"{guid}\" maxApps=\"{text}\"/></AffixRules></HC></ParserParameters>", guid));
    }

    [Theory]
    [InlineData("two")]
    [InlineData("")]
    [InlineData("-1")]
    public void ACountThatIsNotAWholeNumberIsAFormatError(string text)
    {
        var guid = Guid.NewGuid();
        Assert.Throws<FormatException>(() => AffixMaxApps.Read($"<ParserParameters><HC><AffixRules><AffixRule guid=\"{guid}\" maxApps=\"{text}\"/></AffixRules></HC></ParserParameters>", guid));
    }

    [Fact]
    public void TheIntentParserIsClosedAndNeedsAllThreeProperties()
    {
        var ok = SetAffixMaxAppsIntentParser.Parse(JsonDocument.Parse($"{{\"msa\":\"{_msa.Value}\",\"expectedMaxApps\":1,\"maxApps\":2}}").RootElement);
        Assert.Equal((_msa, 1, 2), (ok.Msa, ok.ExpectedMaxApps, ok.MaxApps));
        foreach (var json in new[] { "{\"expectedMaxApps\":1,\"maxApps\":2}", $"{{\"msa\":\"{_msa.Value}\",\"maxApps\":2}}",
            $"{{\"msa\":\"{_msa.Value}\",\"expectedMaxApps\":1,\"maxApps\":\"2\"}}",
            $"{{\"msa\":\"{_msa.Value}\",\"expectedMaxApps\":1,\"maxApps\":2,\"extra\":1}}" })
            Assert.Throws<ContractParseException>(() => SetAffixMaxAppsIntentParser.Parse(JsonDocument.Parse(json).RootElement));
    }
}
