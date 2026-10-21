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
public sealed class SetNoDefaultCompoundingComposerTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ProjectWithNoHcElementGainsOneAndKeepsEveryOtherElement()
    {
        SetParameters("<ParserParameters><CompoundRules><CompoundRule>1</CompoundRule></CompoundRules></ParserParameters>");

        var xml = ApplyIntent(false, true);

        var root = XElement.Parse(xml);
        Assert.Equal("true", root.Element("HC")!.Element("NoDefaultCompounding")!.Value);
        Assert.Equal("1", root.Element("CompoundRules")!.Element("CompoundRule")!.Value);
        Assert.Equal(["CompoundRules", "HC"], root.Elements().Select(e => e.Name.LocalName).ToArray());
    }

    [Fact]
    public void BlankParserParametersBecomeAHcElementWithTheFlag()
    {
        SetParameters("");

        var root = XElement.Parse(ApplyIntent(false, true));

        Assert.Equal("ParserParameters", root.Name.LocalName);
        Assert.Equal("true", root.Element("HC")!.Element("NoDefaultCompounding")!.Value);
    }

    [Fact]
    public void ExistingHcElementKeepsItsOtherChildrenAndOtherElements()
    {
        const string original = "<ParserParameters><HC><NotOnClitics>false</NotOnClitics><Strata>a b</Strata>" +
            "<AcceptUnspecifiedGraphemes>true</AcceptUnspecifiedGraphemes></HC><Other attr=\"x\">keep</Other></ParserParameters>";
        SetParameters(original);

        var root = XElement.Parse(ApplyIntent(false, true));

        var hc = root.Element("HC")!;
        Assert.Equal("true", hc.Element("NoDefaultCompounding")!.Value);
        Assert.Equal("false", hc.Element("NotOnClitics")!.Value);
        Assert.Equal("a b", hc.Element("Strata")!.Value);
        Assert.Equal("true", hc.Element("AcceptUnspecifiedGraphemes")!.Value);
        Assert.Equal("keep", root.Element("Other")!.Value);
        Assert.Equal("x", root.Element("Other")!.Attribute("attr")!.Value);
    }

    [Fact]
    public void ExistingFlagIsUpdatedInPlaceAndCanBeTurnedBackOn()
    {
        const string original = "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";
        SetParameters(original);

        var on = ApplyIntent(true, false);

        Assert.Equal("<ParserParameters><HC><NoDefaultCompounding>false</NoDefaultCompounding><Strata /></HC></ParserParameters>", on);
        Assert.False(NoDefaultCompoundingParserParameters.Read(on));
    }

    [Fact]
    public void SettingTheValueTheProjectAlreadyHoldsChangesNothing()
    {
        const string original = "<ParserParameters>\n  <HC>\n    <NoDefaultCompounding>true</NoDefaultCompounding>\n  </HC>\n</ParserParameters>";
        SetParameters(original);

        Assert.Equal(original, ApplyIntent(true, true));
        var once = ApplyIntent(true, true);
        Assert.Equal(once, ApplyIntent(true, true));

        SetParameters("<ParserParameters><X/></ParserParameters>");
        var first = ApplyIntent(false, true);
        Assert.Equal(first, ApplyIntent(true, true));
    }

    [Fact]
    public void BuildRefusesAStaleExpectedValueAndMalformedXmlWithoutChangingTheProject()
    {
        SetParameters("<ParserParameters/>");
        var stale = Assert.Throws<InvalidOperationException>(() =>
            SetNoDefaultCompoundingComposer.Build(_cache, new(true, false)));
        Assert.Contains("expected NoDefaultCompounding=True", stale.Message, StringComparison.Ordinal);
        Assert.Equal("<ParserParameters/>", _cache.LangProject.MorphologicalDataOA.ParserParameters);

        SetParameters("<ParserParameters>");
        var malformed = Assert.Throws<InvalidOperationException>(() =>
            SetNoDefaultCompoundingComposer.Build(_cache, new(false, true)));
        Assert.Contains("not well-formed", malformed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerOnlyAuthorsAnOperationWithRationaleAndDryRunThenApplyChangesTheProject()
    {
        SetParameters("<ParserParameters><HC><Strata /></HC></ParserParameters>");
        var proposal = Proposal(new(false, true));
        var operation = Assert.Single(proposal.Operations);
        Assert.Equal(MoMorphDataNoDefaultCompoundingOperationKinds.Set, operation.Kind);
        Assert.False(string.IsNullOrWhiteSpace(operation.Rationale));
        Assert.Equal(CanonicalId.FromGuid(_cache.LangProject.MorphologicalDataOA.Guid), operation.Target);
        Assert.Equal("<ParserParameters><HC><Strata /></HC></ParserParameters>",
            _cache.LangProject.MorphologicalDataOA.ParserParameters);

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.False(NoDefaultCompoundingParserParameters.Read(_cache.LangProject.MorphologicalDataOA.ParserParameters));
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoMorphDataNoDefaultCompounding, effect.Field);
        Assert.Equal("false", effect.Before[BooleanFieldAlternatives.Key]);
        Assert.Equal("true", effect.After[BooleanFieldAlternatives.Key]);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.False(receipt.AlreadyApplied);
        Assert.True(NoDefaultCompoundingParserParameters.Read(_cache.LangProject.MorphologicalDataOA.ParserParameters));
        Assert.Contains("<Strata />", _cache.LangProject.MorphologicalDataOA.ParserParameters, StringComparison.Ordinal);
    }

    private void SetParameters(string xml) =>
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.MorphologicalDataOA.ParserParameters = xml);

    private string ApplyIntent(bool expected, bool desired)
    {
        SoundSystemComposerTests.Execute(_cache, SetNoDefaultCompoundingComposer.Build(_cache, new(expected, desired)));
        return _cache.LangProject.MorphologicalDataOA.ParserParameters;
    }

    private Proposal Proposal(SetNoDefaultCompoundingIntent intent) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            SetNoDefaultCompoundingComposer.Build(_cache, intent));
}
