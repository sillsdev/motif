using System.Text.Json;
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
public sealed class SetEntryAttributionComposerTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Tag = "lang-sme@9ba7be7 GPL-3.0";
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose() => _cache.Dispose();

    private ILexEntry First => _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
    private ILexEntry Second => _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.SecondEntryId);

    private SetEntryAttributionIntent Intent(string tag, string? ws, params ILexEntry[] entries) =>
        new(AttributionTag.Parse(tag, "test"), entries.Select(entry => new AttributedEntry(CanonicalId.FromGuid(entry.Guid))).ToArray(), ws);

    private string Bibliography(ILexEntry entry, string ws = "en") =>
        entry.Bibliography.get_String(_cache.WritingSystemFactory.GetWsFromStr(ws)).Text ?? string.Empty;

    private void Set(ILexEntry entry, string text, string ws = "en") =>
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            entry.Bibliography.set_String(_cache.WritingSystemFactory.GetWsFromStr(ws),
                TsStringUtils.MakeString(text, _cache.WritingSystemFactory.GetWsFromStr(ws))));

    private void Run(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "motif-tests");
    }

    [Fact]
    public void TheTagIsWrittenIntoAnEmptyBibliographyThroughDryRunAndApply()
    {
        var entry = First;
        Assert.Equal(string.Empty, Bibliography(entry));
        var operations = SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, entry, Second));

        Assert.Equal(2, operations.Count);
        Assert.All(operations, operation => Assert.Equal(AttributionOperationKinds.AppendToBibliography, operation.Kind));
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(string.Empty, Bibliography(entry));
        var effect = dry.ExpectedEffects.First(item => item.CanonicalId == CanonicalId.FromGuid(entry.Guid));
        Assert.Equal(SnapshotFields.LexEntryBibliography, effect.Field);
        Assert.Equal(Tag, effect.After["en"]);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "motif-tests");

        Assert.Equal(Tag, Bibliography(entry));
        Assert.Equal(Tag, Bibliography(Second));
    }

    [Fact]
    public void RunningItAgainStagesNothingAndAddsNoSecondCopy()
    {
        Run(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));

        Assert.Empty(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));
        Assert.Equal(Tag, Bibliography(First));
    }

    [Fact]
    public void OnlyTheEntriesWithoutTheTagAreStaged()
    {
        Run(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));

        var operations = SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First, Second));

        Assert.Equal(CanonicalId.FromGuid(Second.Guid), Assert.Single(operations).Target);
    }

    [Fact]
    public void ExistingBibliographyTextIsKeptAndTheTagFollowsIt()
    {
        Set(First, "Smith 2019, p. 4");

        Run(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));

        Assert.Equal("Smith 2019, p. 4; " + Tag, Bibliography(First));
        Assert.Empty(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));
    }

    [Fact]
    public void ATagAlreadyAmongOtherTextIsRecognised()
    {
        Set(First, "Smith 2019; " + Tag + "; Jones 2020");

        Assert.Empty(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));
    }

    [Fact]
    public void ANewerRevisionOfTheSameSourceIsAddedBesideTheFirst()
    {
        Run(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));

        Run(SetEntryAttributionComposer.Build(_cache, Intent("lang-sme@c0ffee1 GPL-3.0", null, First)));

        Assert.Equal(Tag + "; lang-sme@c0ffee1 GPL-3.0", Bibliography(First));
    }

    [Fact]
    public void TheSameSourceAndRevisionUnderAnotherLicenceIsRefusedNotStatedTwice()
    {
        Run(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));

        var refusal = Assert.Throws<ContractParseException>(() =>
            SetEntryAttributionComposer.Build(_cache, Intent("lang-sme@9ba7be7 MIT", null, First)));

        Assert.Contains("GPL-3.0", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("a person to decide", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(Tag, Bibliography(First));
    }

    [Fact]
    public void TheDefaultAnalysisWritingSystemIsUsedAndAnotherAnalysisOneCanBeChosen()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var french);
            _cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(french);
        });

        var byDefault = Assert.Single(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));
        var chosen = Assert.Single(SetEntryAttributionComposer.Build(_cache, Intent(Tag, "fr", First)));

        Assert.Equal("en", byDefault.After!.Value.GetProperty("ws").GetString());
        Assert.Equal("fr", chosen.After!.Value.GetProperty("ws").GetString());
        Run([chosen]);
        Assert.Equal(Tag, Bibliography(First, "fr"));
        Assert.Equal(string.Empty, Bibliography(First, "en"));
    }

    [Fact]
    public void TheTagAlreadyInAnyAnalysisWritingSystemOfTheFieldIsANoOp()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var french);
            _cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Add(french);
        });
        Set(First, "Smith 2019; " + Tag, "fr");

        Assert.Empty(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));
        Assert.Empty(SetEntryAttributionComposer.Build(_cache, Intent(Tag, "fr", First)));
    }

    [Fact]
    public void ProseThatOnlyLooksLikeANoticeIsKeptAndNeverRefused()
    {
        Set(First, "Attribution: Data courtesy of the Sami Parliament");

        Run(SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First)));

        Assert.EndsWith("; " + Tag, Bibliography(First), StringComparison.Ordinal);
        Assert.StartsWith("Attribution: Data courtesy", Bibliography(First), StringComparison.Ordinal);
    }

    [Fact]
    public void FormattedMultiRunTextKeepsItsRunsWhenTheTagIsAppended()
    {
        var english = _cache.WritingSystemFactory.GetWsFromStr("en");
        var other = _cache.WritingSystemFactory.GetWsFromStr(SeededProject.RightToLeftTag);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var builder = TsStringUtils.MakeString("Smith ", english).GetBldr();
            builder.ReplaceTsString(builder.Length, builder.Length, TsStringUtils.MakeString("2019", english));
            builder.SetIntPropValues(6, 10, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, (int)FwTextToggleVal.kttvForceOn);
            builder.ReplaceTsString(builder.Length, builder.Length, TsStringUtils.MakeString(" kitab", other));
            First.Bibliography.set_String(english, builder.GetString());
        });

        var operations = SetEntryAttributionComposer.Build(_cache, Intent(Tag, null, First));
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal("Smith 2019 kitab; " + Tag, dry.ExpectedEffects.Single().After["en"]);
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "motif-tests");

        var result = First.Bibliography.get_String(english);
        Assert.Equal("Smith 2019 kitab; " + Tag, result.Text);
        Assert.Equal(4, result.RunCount);
        Assert.Equal(1, result.get_Properties(1).GetIntPropValues((int)FwTextPropType.ktptBold, out _));
        Assert.Equal(other, result.get_Properties(2).GetIntPropValues((int)FwTextPropType.ktptWs, out _));
        Assert.Equal(english, result.get_Properties(3).GetIntPropValues((int)FwTextPropType.ktptWs, out _));
    }

    [Fact]
    public void AVernacularOnlyWritingSystemIsRefusedAndNamesTheAnalysisOnes()
    {
        var refusal = Assert.Throws<ContractParseException>(() =>
            SetEntryAttributionComposer.Build(_cache, Intent(Tag, SeededProject.RightToLeftTag, First)));

        Assert.Contains("not an analysis writing system", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("en", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUnknownEntryIsRefusedTogetherWithItsLabel()
    {
        var intent = new SetEntryAttributionIntent(AttributionTag.Parse(Tag, "test"),
        [
            new(CanonicalId.FromGuid(First.Guid), "line 1"),
            new(CanonicalId.Mint(), "line 2"),
            new(CanonicalId.FromGuid(pristine.Seed.FirstSenseId), "line 3"),
        ]);

        var refusal = Assert.Throws<ContractParseException>(() => SetEntryAttributionComposer.Build(_cache, intent));

        Assert.Contains("2 problem(s)", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("entry 2 (line 2)", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("entry 3 (line 3)", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, Bibliography(First));
    }

    [Fact]
    public void TheTagMustBeSourceAtRevisionThenLicence()
    {
        foreach (var bad in new[] { "lang-sme GPL-3.0", "lang-sme@9ba7be7", "a b@c GPL", " lang-sme@9ba7be7 GPL-3.0", "x@y GPL;MIT", "x@y@z GPL" })
            Assert.Throws<ContractParseException>(() => AttributionTag.Parse(bad, "test"));
        Assert.Equal("CC BY-SA 4.0", AttributionTag.Parse("tam-gloss@1a2b CC BY-SA 4.0", "test").Licence);
    }
}
