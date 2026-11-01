using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class SetNotOnCliticsComposerTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ProjectWithNoHcElementHoldsTrueAndGainsOneAndKeepsEveryOtherElement()
    {
        SetParameters("<ParserParameters><CompoundRules><CompoundRule>1</CompoundRule></CompoundRules></ParserParameters>");
        Assert.True(NotOnCliticsParserParameters.Read(_cache.LangProject.MorphologicalDataOA.ParserParameters));

        var xml = ApplyIntent(true, false);

        var root = XElement.Parse(xml);
        Assert.Equal("false", root.Element("HC")!.Element("NotOnClitics")!.Value);
        Assert.Equal("1", root.Element("CompoundRules")!.Element("CompoundRule")!.Value);
        Assert.Equal(["CompoundRules", "HC"], root.Elements().Select(e => e.Name.LocalName).ToArray());
        Assert.False(NotOnCliticsParserParameters.Read(xml));
    }

    [Fact]
    public void BlankParserParametersHoldTrueAndBecomeAHcElementWithTheFlag()
    {
        SetParameters("");

        var root = XElement.Parse(ApplyIntent(true, false));

        Assert.Equal("ParserParameters", root.Name.LocalName);
        Assert.Equal("false", root.Element("HC")!.Element("NotOnClitics")!.Value);
    }

    [Fact]
    public void ExistingHcElementKeepsItsOtherChildrenAndOtherElements()
    {
        const string original = "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata>a b</Strata>" +
            "<AcceptUnspecifiedGraphemes>true</AcceptUnspecifiedGraphemes></HC><Other attr=\"x\">keep</Other></ParserParameters>";
        SetParameters(original);

        var root = XElement.Parse(ApplyIntent(true, false));

        var hc = root.Element("HC")!;
        Assert.Equal("false", hc.Element("NotOnClitics")!.Value);
        Assert.Equal("true", hc.Element("NoDefaultCompounding")!.Value);
        Assert.Equal("a b", hc.Element("Strata")!.Value);
        Assert.Equal("true", hc.Element("AcceptUnspecifiedGraphemes")!.Value);
        Assert.Equal("keep", root.Element("Other")!.Value);
        Assert.Equal("x", root.Element("Other")!.Attribute("attr")!.Value);
    }

    [Fact]
    public void ExistingFlagIsUpdatedInPlaceAndCanBeTurnedBackOn()
    {
        const string original = "<ParserParameters><HC><NotOnClitics>false</NotOnClitics><Strata /></HC></ParserParameters>";
        SetParameters(original);

        var on = ApplyIntent(false, true);

        Assert.Equal("<ParserParameters><HC><NotOnClitics>true</NotOnClitics><Strata /></HC></ParserParameters>", on);
        Assert.True(NotOnCliticsParserParameters.Read(on));
    }

    [Fact]
    public void SettingTheValueTheProjectAlreadyHoldsChangesNothing()
    {
        const string original = "<ParserParameters>\n  <HC>\n    <NotOnClitics>false</NotOnClitics>\n  </HC>\n</ParserParameters>";
        SetParameters(original);

        Assert.Equal(original, ApplyIntent(false, false));

        SetParameters("<ParserParameters><X/></ParserParameters>");
        var first = ApplyIntent(true, true);
        Assert.Equal(first, ApplyIntent(true, true));
        Assert.True(NotOnCliticsParserParameters.Read(first));
    }

    [Fact]
    public void BuildRefusesAStaleExpectedValueAndMalformedXmlWithoutChangingTheProject()
    {
        SetParameters("<ParserParameters/>");
        var stale = Assert.Throws<InvalidOperationException>(() =>
            SetNotOnCliticsComposer.Build(_cache, new(false, false)));
        Assert.Contains("expected NotOnClitics=False", stale.Message, StringComparison.Ordinal);
        Assert.Equal("<ParserParameters/>", _cache.LangProject.MorphologicalDataOA.ParserParameters);

        SetParameters("<ParserParameters>");
        var malformed = Assert.Throws<InvalidOperationException>(() =>
            SetNotOnCliticsComposer.Build(_cache, new(true, false)));
        Assert.Contains("not well-formed", malformed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerOnlyAuthorsAnOperationWithRationaleAndDryRunThenApplyChangesTheProject()
    {
        SetParameters("<ParserParameters><HC><Strata /></HC></ParserParameters>");
        var proposal = Proposal(new(true, false));
        var operation = Assert.Single(proposal.Operations);
        Assert.Equal(MoMorphDataNotOnCliticsOperationKinds.Set, operation.Kind);
        Assert.False(string.IsNullOrWhiteSpace(operation.Rationale));
        Assert.Equal(CanonicalId.FromGuid(_cache.LangProject.MorphologicalDataOA.Guid), operation.Target);
        Assert.Equal("<ParserParameters><HC><Strata /></HC></ParserParameters>",
            _cache.LangProject.MorphologicalDataOA.ParserParameters);

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.True(NotOnCliticsParserParameters.Read(_cache.LangProject.MorphologicalDataOA.ParserParameters));
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoMorphDataNotOnClitics, effect.Field);
        Assert.Equal("true", effect.Before[BooleanFieldAlternatives.Key]);
        Assert.Equal("false", effect.After[BooleanFieldAlternatives.Key]);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.False(receipt.AlreadyApplied);
        Assert.False(NotOnCliticsParserParameters.Read(_cache.LangProject.MorphologicalDataOA.ParserParameters));
        Assert.Contains("<Strata />", _cache.LangProject.MorphologicalDataOA.ParserParameters, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(" 1 ", true)]
    public void ReadsTheValueTheWayTheLoaderCastsIt(string text, bool expected) =>
        Assert.Equal(expected, NotOnCliticsParserParameters.Read($"<ParserParameters><HC><NotOnClitics>{text}</NotOnClitics></HC></ParserParameters>"));

    [Theory]
    [InlineData("")]
    [InlineData("True")]
    [InlineData("yes")]
    public void AValueTheLoaderCannotReadIsRefusedAtReadAndCompose(string text)
    {
        var xml = $"<ParserParameters><HC><NotOnClitics>{text}</NotOnClitics></HC></ParserParameters>";
        Assert.Throws<FormatException>(() => NotOnCliticsParserParameters.Read(xml));
        SetParameters(xml);

        var refusal = Assert.Throws<InvalidOperationException>(() => SetNotOnCliticsComposer.Build(_cache, new(true, false)));

        Assert.Contains("not 0, 1, true or false", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(xml, _cache.LangProject.MorphologicalDataOA.ParserParameters);
    }

    private void SetParameters(string xml) =>
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.MorphologicalDataOA.ParserParameters = xml);

    private string ApplyIntent(bool expected, bool desired)
    {
        SoundSystemComposerTests.Execute(_cache, SetNotOnCliticsComposer.Build(_cache, new(expected, desired)));
        return _cache.LangProject.MorphologicalDataOA.ParserParameters;
    }

    private Proposal Proposal(SetNotOnCliticsIntent intent) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            SetNotOnCliticsComposer.Build(_cache, intent));
}
