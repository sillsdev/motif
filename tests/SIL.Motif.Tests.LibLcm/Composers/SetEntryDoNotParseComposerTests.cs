using System.Text.Json;
using SIL.LCModel;
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
public sealed class SetEntryDoNotParseComposerTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void SetAndClearStageOneOperationAndRoundTripThroughDryRunAndApply()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var target = CanonicalId.FromGuid(entry.Guid);
        Assert.False(entry.DoNotUseForParsing);

        Run(false, true);
        Assert.True(entry.DoNotUseForParsing);
        Run(true, false);
        Assert.False(entry.DoNotUseForParsing);

        void Run(bool before, bool after)
        {
            var operations = SetEntryDoNotParseComposer.Build(_cache, new(target, before, after));
            var op = Assert.Single(operations);
            Assert.Equal(target, op.Target);
            Assert.Equal(LexEntryDoNotUseForParsingOperationKinds.SetDoNotUseForParsing, op.Kind);
            Assert.Equal(before, entry.DoNotUseForParsing);
            var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" },
                CanonicalId.Mint(), null, operations);
            var dry = ScratchDryRun.Of(_cache, proposal);
            Assert.Equal(before, entry.DoNotUseForParsing);
            var effect = Assert.Single(dry.ExpectedEffects);
            Assert.Equal(SnapshotFields.LexEntryDoNotUseForParsing, effect.Field);
            Assert.Equal(before ? "true" : "false", effect.Before["value"]);
            Assert.Equal(after ? "true" : "false", effect.After["value"]);
            ProposalApplier.Apply(_cache, proposal, dry.Anchor, "motif-tests");
        }
    }

    [Fact]
    public void StaleMissingAndWrongTypeTargetsAreRefusedWithoutMutation()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var target = CanonicalId.FromGuid(entry.Guid);
        Assert.Throws<InvalidOperationException>(() => SetEntryDoNotParseComposer.Build(_cache, new(target, true, true)));
        Assert.Throws<InvalidOperationException>(() => SetEntryDoNotParseComposer.Build(_cache,
            new(CanonicalId.FromGuid(pristine.Seed.FirstSenseId), false, true)));
        Assert.Throws<InvalidOperationException>(() => SetEntryDoNotParseComposer.Build(_cache,
            new(CanonicalId.Mint(), false, true)));
        Assert.False(entry.DoNotUseForParsing);
    }

    [Theory]
    [InlineData("{\"target\":\"bad\",\"expectedDoNotParse\":false,\"doNotParse\":true}")]
    [InlineData("{\"expectedDoNotParse\":false,\"doNotParse\":true}")]
    [InlineData("{\"target\":\"bad\",\"expectedDoNotParse\":false,\"doNotParse\":true,\"extra\":1}")]
    public void MalformedIntentsAreRefused(string input) =>
        Assert.Throws<ContractParseException>(() => SetEntryDoNotParseIntentParser.Parse(JsonDocument.Parse(input).RootElement));

    [Fact]
    public void IntentRequiresBothBooleanValues()
    {
        var target = CanonicalId.FromGuid(pristine.Seed.FirstEntryId);
        foreach (var input in new[]
        {
            $"{{\"target\":\"{target}\",\"doNotParse\":true}}",
            $"{{\"target\":\"{target}\",\"expectedDoNotParse\":false}}",
            $"{{\"target\":\"{target}\",\"expectedDoNotParse\":\"false\",\"doNotParse\":true}}"
        })
            Assert.Throws<ContractParseException>(() => SetEntryDoNotParseIntentParser.Parse(JsonDocument.Parse(input).RootElement));
        var valid = JsonDocument.Parse($"{{\"target\":\"{target}\",\"expectedDoNotParse\":false,\"doNotParse\":true}}");
        Assert.Equal(new SetEntryDoNotParseIntent(target, false, true), SetEntryDoNotParseIntentParser.Parse(valid.RootElement));
    }
}
