using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class SetProjectAttributionComposerTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Tag = "lang-sme@9ba7be7 GPL-3.0";
    private const string Notice = "Attribution: " + Tag;
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose() => _cache.Dispose();

    private SetProjectAttributionIntent Intent(string tag, string? ws = null) => new(AttributionTag.Parse(tag, "test"), ws);

    private string Description(string ws = "en") =>
        _cache.LangProject.Description.get_String(_cache.WritingSystemFactory.GetWsFromStr(ws)).Text ?? string.Empty;

    private void SetDescription(string text) => NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        _cache.LangProject.Description.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString(text, _cache.DefaultAnalWs)));

    private void Run(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string> { ["project"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "motif-tests");
    }

    [Fact]
    public void TheNoticeIsWrittenIntoAnEmptyDescriptionThroughDryRunAndApply()
    {
        Assert.Equal(string.Empty, Description());
        var operations = SetProjectAttributionComposer.Build(_cache, Intent(Tag));
        var operation = Assert.Single(operations);
        Assert.Equal(AttributionOperationKinds.AppendToDescription, operation.Kind);
        Assert.Equal(CanonicalId.FromGuid(_cache.LangProject.Guid), operation.Target);

        var proposal = new Proposal(new Dictionary<string, string> { ["project"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(string.Empty, Description());
        var effect = Assert.Single(dry.ExpectedEffects);
        Assert.Equal(SnapshotFields.LangProjectDescription, effect.Field);
        Assert.Equal(Notice, effect.After["en"]);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "motif-tests");

        Assert.Equal(Notice, Description());
    }

    [Fact]
    public void RunningItAgainStagesNothing()
    {
        Run(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));

        Assert.Empty(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));
        Assert.Equal(Notice, Description());
    }

    [Fact]
    public void ExistingDescriptionTextIsKeptAndTheNoticeFollowsOnANewLine()
    {
        SetDescription("Dictionary of North Sami.");

        Run(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));

        Assert.Equal("Dictionary of North Sami.\u2028" + Notice, Description());
        Assert.Empty(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));
    }

    [Fact]
    public void ASecondRevisionIsAddedAsASecondNoticeAndAnotherLicenceIsRefused()
    {
        Run(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));

        Run(SetProjectAttributionComposer.Build(_cache, Intent("lang-sme@c0ffee1 GPL-3.0")));
        Assert.Equal(Notice + "\u2028Attribution: lang-sme@c0ffee1 GPL-3.0", Description());

        var refusal = Assert.Throws<ContractParseException>(() =>
            SetProjectAttributionComposer.Build(_cache, Intent("lang-sme@9ba7be7 MIT")));
        Assert.Contains("a person to decide", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoticeInAnyAnalysisWritingSystemIsANoOpAndAVernacularOneIsRefused()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var french);
            _cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(french);
        });
        Run(SetProjectAttributionComposer.Build(_cache, Intent(Tag, "fr")));

        Assert.Equal(Notice, Description("fr"));
        Assert.Empty(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));
        Assert.Throws<ContractParseException>(() =>
            SetProjectAttributionComposer.Build(_cache, Intent(Tag, SeededProject.RightToLeftTag)));
    }

    [Fact]
    public void ProseBeginningAttributionIsKeptAndNeverRefused()
    {
        SetDescription("Attribution: Data courtesy of the Sami Parliament.");

        Run(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));

        Assert.Equal("Attribution: Data courtesy of the Sami Parliament.\u2028" + Notice, Description());
    }

    [Fact]
    public void FormattedMultiRunDescriptionKeepsItsRunsWhenTheNoticeIsAppended()
    {
        var english = _cache.DefaultAnalWs;
        var other = _cache.WritingSystemFactory.GetWsFromStr(SeededProject.RightToLeftTag);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var builder = TsStringUtils.MakeString("North ", english).GetBldr();
            builder.ReplaceTsString(builder.Length, builder.Length, TsStringUtils.MakeString("Sami", english));
            builder.SetIntPropValues(6, 10, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, (int)FwTextToggleVal.kttvForceOn);
            builder.ReplaceTsString(builder.Length, builder.Length, TsStringUtils.MakeString(" kitab", other));
            _cache.LangProject.Description.set_String(english, builder.GetString());
        });

        Run(SetProjectAttributionComposer.Build(_cache, Intent(Tag)));

        var result = _cache.LangProject.Description.get_String(english);
        Assert.Equal("North Sami kitab\u2028" + Notice, result.Text);
        Assert.Equal(1, result.get_Properties(1).GetIntPropValues((int)FwTextPropType.ktptBold, out _));
        Assert.Equal(other, result.get_Properties(2).GetIntPropValues((int)FwTextPropType.ktptWs, out _));
    }

    [Fact]
    public void ThePayloadIsClosedAndNeverEmpty()
    {
        Assert.Throws<ContractParseException>(() => AttributionAppendPayload.Parse(
            System.Text.Json.JsonSerializer.SerializeToElement(new { ws = "en", text = "", separator = ";" }), AttributionOperationKinds.AppendToDescription));
        Assert.Throws<ContractParseException>(() => AttributionAppendPayload.Parse(
            System.Text.Json.JsonSerializer.SerializeToElement(new { ws = "en", text = "x", separator = ";", extra = 1 }), AttributionOperationKinds.AppendToDescription));
    }
}
