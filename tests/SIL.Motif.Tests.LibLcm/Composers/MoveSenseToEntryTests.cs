using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class MoveSenseToEntryTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public MoveSenseToEntryTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ASenseMovesToANewEntryWhileTheRootKeepsItsOtherSensesAndTheirAnalysis()
    {
        var root = Root("ayu", sharedAnalysis: true, "tree", "kaayu", "maayu");
        var operations = Compose(root.Senses[1], "kaayu");

        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "move-sense-tests");

        var entryId = operations.Single(op => op.Kind == LexDbEntriesOperationKinds.Create).EntityId!.Value;
        var created = (ILexEntry)Repository.GetObject(entryId.ToGuid());
        Assert.Equal("kaayu", ((IMoStemAllomorph)created.LexemeFormOA).Form.get_String(_cache.DefaultVernWs).Text);
        var moved = Assert.Single(created.SensesOS);
        Assert.Equal(root.Senses[1], moved.Guid);
        Assert.Equal("kaayu", moved.Gloss.get_String(_cache.DefaultAnalWs).Text);
        var newMsa = Assert.IsAssignableFrom<IMoStemMsa>(Assert.Single(created.MorphoSyntaxAnalysesOC));
        Assert.Equal(_seed.PartOfSpeechId, newMsa.PartOfSpeechRA?.Guid);
        Assert.Equal(newMsa.Guid, moved.MorphoSyntaxAnalysisRA.Guid);

        var old = Entry(root.Entry);
        Assert.Equal([root.Senses[0], root.Senses[2]], old.SensesOS.Select(sense => sense.Guid));
        var kept = Assert.Single(old.MorphoSyntaxAnalysesOC);
        Assert.Equal(root.Msa, kept.Guid);
        Assert.All(old.SensesOS, sense => Assert.Equal(root.Msa, sense.MorphoSyntaxAnalysisRA.Guid));
    }

    [Fact]
    public void ASenseWithExamplesAndSubsensesKeepsThemAndTheSubsensesFollowTheNewAnalysis()
    {
        var root = Root("tuig", sharedAnalysis: true, "year", "tinuig");
        ILexSense sub = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var sense = (ILexSense)Repository.GetObject(root.Senses[1]);
            sense.Definition.set_String(_cache.DefaultAnalWs, "a yearly thing");
            for (var i = 0; i < 2; i++)
            {
                var example = _cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create();
                sense.ExamplesOS.Add(example);
                example.Example.set_String(_cache.DefaultVernWs, "ex" + i);
            }
            sub = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            sense.SensesOS.Add(sub);
            sub.Gloss.set_String(_cache.DefaultAnalWs, "sub");
            sub.MorphoSyntaxAnalysisRA = sense.MorphoSyntaxAnalysisRA;
        });

        var proposal = Proposal(Compose(root.Senses[1], "tinuig"));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "move-sense-tests");

        var moved = (ILexSense)Repository.GetObject(root.Senses[1]);
        var created = (ILexEntry)moved.Owner;
        Assert.NotEqual(root.Entry, created.Guid);
        Assert.Equal("a yearly thing", moved.Definition.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal(["ex0", "ex1"], moved.ExamplesOS.Select(e => e.Example.get_String(_cache.DefaultVernWs).Text));
        Assert.Same(moved, sub.Owner);
        Assert.Equal("sub", sub.Gloss.get_String(_cache.DefaultAnalWs).Text);
        var newMsa = Assert.Single(created.MorphoSyntaxAnalysesOC);
        Assert.Equal(newMsa.Guid, moved.MorphoSyntaxAnalysisRA.Guid);
        Assert.Equal(newMsa.Guid, sub.MorphoSyntaxAnalysisRA.Guid);
        Assert.Equal(root.Msa, Assert.Single(Entry(root.Entry).MorphoSyntaxAnalysesOC).Guid);
    }

    [Fact]
    public void WhenNoRemainingSenseUsesTheOldAnalysisItIsRemovedAndTheRootStaysValid()
    {
        var root = Root("bisagra", sharedAnalysis: false, "hinge", "bisan");
        var proposal = Proposal(Compose(root.Senses[1], "bisan"));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "move-sense-tests");

        var old = Entry(root.Entry);
        var only = Assert.Single(old.SensesOS);
        Assert.Equal(root.Senses[0], only.Guid);
        var analysis = Assert.Single(old.MorphoSyntaxAnalysesOC);
        Assert.Equal(root.Msa, analysis.Guid);
        Assert.Equal(analysis.Guid, only.MorphoSyntaxAnalysisRA.Guid);
        Assert.False(Repository.IsValidObjectId(root.OtherMsa!.Value));
    }

    [Fact]
    public void AnExplicitAnalysisReplacesTheCopyAndMustMatchTheMorphType()
    {
        var root = Root("ayu", sharedAnalysis: true, "tree", "kaayu", "maayu");
        var other = CreateCategory("Verbo");
        var intent = new MoveSenseToEntryIntent(Id(root.Senses[1]), "kaayu", Vern(), Id(MoMorphTypeTags.kguidMorphStem),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, Id(other)));
        var proposal = Proposal(MoveSenseToEntryComposer.Build(_cache, intent));
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "move-sense-tests");
        var created = (ILexEntry)((ILexSense)Repository.GetObject(root.Senses[1])).Owner;
        Assert.Equal(other.Guid, ((IMoStemMsa)Assert.Single(created.MorphoSyntaxAnalysesOC)).PartOfSpeechRA.Guid);

        var mismatch = intent with
        {
            Sense = Id(root.Senses[2]),
            MorphType = Id(MoMorphTypeTags.kguidMorphSuffix),
            Msa = new LexicalEntryMsa(LexicalEntryMsaKind.Stem, Id(other)),
        };
        Assert.Contains("affix msa", Assert.Throws<InvalidOperationException>(() =>
            MoveSenseToEntryComposer.Build(_cache, mismatch)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingTheOnlySenseOfAnEntryIsRefusedAsFieldWorksDoes()
    {
        var root = Root("solo", sharedAnalysis: true, "alone");
        var refused = Assert.Throws<InvalidOperationException>(() => Compose(root.Senses[0], "solo2"));
        Assert.Contains("only this sense", refused.Message, StringComparison.Ordinal);

        var two = Root("duo", sharedAnalysis: true, "one", "two");
        var operations = Compose(two.Senses[1], "duo2");
        var move = Assert.Single(operations, op => op.Kind == LexSenseMoveOperationKinds.MoveToEntry);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(two.Senses[0]).Delete());
        var stale = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
        Assert.Contains("only sense", stale.ToString(), StringComparison.Ordinal);
        Assert.Equal(LexSenseMoveOperationKinds.MoveToEntry, move.Kind);
    }

    [Fact]
    public void ASenseThatParserAnalysesOrADifferentSubsenseAnalysisBindIsRefused()
    {
        var root = Root("ayu", sharedAnalysis: false, "tree", "kaayu");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var sense = (ILexSense)Repository.GetObject(root.Senses[1]);
            var sub = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            sense.SensesOS.Add(sub);
            sub.MorphoSyntaxAnalysisRA = Entry(root.Entry).SensesOS[0].MorphoSyntaxAnalysisRA;
        });
        Assert.Contains("sub-sense", Assert.Throws<InvalidOperationException>(() =>
            Compose(root.Senses[1], "kaayu")).Message, StringComparison.Ordinal);

        var bound = Root("tuig", sharedAnalysis: true, "year", "tinuig");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            analysis.MorphBundlesOS.Add(bundle);
            bundle.SenseRA = (ILexSense)Repository.GetObject(bound.Senses[1]);
        });
        Assert.Contains("morph bundles", Assert.Throws<InvalidOperationException>(() =>
            Compose(bound.Senses[1], "tinuig")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRunEffectsAndFootprintDescribeTheMoveWithoutChangingTheProjectOrNeedingTheNewEntry()
    {
        var root = Root("ayu", sharedAnalysis: false, "tree", "kaayu");
        var operations = Compose(root.Senses[1], "kaayu");
        var proposal = Proposal(operations);
        var move = Assert.Single(operations, op => op.Kind == LexSenseMoveOperationKinds.MoveToEntry);
        var payload = LexSenseMovePayload.Parse(move.After!.Value);
        Assert.Equal(root.OtherMsa!.Value, payload.DeleteMsa!.Value.ToGuid());
        Assert.Contains(move.DependsOn, d => d.OperationId == operations.Single(op =>
            op.Kind == LexDbEntriesOperationKinds.Create).OperationId);

        var digestBefore = FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal);
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var fromSenses = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexEntrySenses &&
                                                                    e.CanonicalId == Id(root.Entry));
        Assert.Equal(2, fromSenses.Before.Count);
        Assert.Single(fromSenses.After);
        Assert.Contains("movedSense", fromSenses.Preview!.Value.GetRawText(), StringComparison.Ordinal);
        var toSenses = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexEntrySenses &&
                                                                  e.CanonicalId == payload.Entry);
        Assert.Empty(toSenses.Before);
        Assert.Equal(Id(root.Senses[1]).Value, Assert.Single(toSenses.After).Value);
        var analysis = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexSenseMorphoSyntaxAnalysis &&
                                                                  e.CanonicalId == Id(root.Senses[1]));
        Assert.Equal(root.OtherMsa!.Value, CanonicalId.Parse(Assert.Single(analysis.Before).Value).ToGuid());
        Assert.Equal(payload.Msa.Value, Assert.Single(analysis.After).Value);
        var analyses = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexEntryMorphoSyntaxAnalyses &&
                                                                  e.CanonicalId == Id(root.Entry));
        Assert.Equal(2, analyses.Before.Count);
        Assert.Single(analyses.After);

        Assert.Equal(2, Entry(root.Entry).SensesOS.Count);
        Assert.False(Repository.IsValidObjectId(payload.Entry.ToGuid()));
        Assert.Equal(digestBefore, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
    }

    [Fact]
    public void TwoMovesOutOfOneRootInOneProposalDoNotCollideAndTheRootKeepsOneSense()
    {
        var root = Root("ayu", sharedAnalysis: true, "tree", "kaayu", "maayu");
        var first = Compose(root.Senses[1], "kaayu");
        var second = Compose(root.Senses[2], "maayu");
        Assert.Null(LexSenseMovePayload.Parse(second.Single(op => op.Kind == LexSenseMoveOperationKinds.MoveToEntry)
            .After!.Value).DeleteMsa);
        var proposal = Proposal([.. first, .. second]);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "move-sense-tests");

        Assert.Equal([root.Senses[0]], Entry(root.Entry).SensesOS.Select(sense => sense.Guid));
        Assert.Equal(root.Msa, Assert.Single(Entry(root.Entry).MorphoSyntaxAnalysesOC).Guid);
        Assert.NotEqual(((ILexSense)Repository.GetObject(root.Senses[1])).Owner.Guid,
            ((ILexSense)Repository.GetObject(root.Senses[2])).Owner.Guid);
    }

    [Fact]
    public void ThePayloadIsClosedAndNamesEveryIdentityItNeeds()
    {
        using var unknown = JsonDocument.Parse(
            $"{{\"entry\":\"{Id(Guid.NewGuid()).Value}\",\"msa\":\"{Id(Guid.NewGuid()).Value}\",\"fromEntry\":\"{Id(Guid.NewGuid()).Value}\",\"deleteMsa\":null,\"force\":true}}");
        using var missing = JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(() => LexSenseMovePayload.Parse(unknown.RootElement));
        Assert.ThrowsAny<Exception>(() => LexSenseMovePayload.Parse(missing.RootElement));
    }

    private IReadOnlyList<OperationEnvelope> Compose(Guid sense, string form) =>
        MoveSenseToEntryComposer.Build(_cache, new MoveSenseToEntryIntent(
            Id(sense), form, Vern(), Id(MoMorphTypeTags.kguidMorphStem)));

    private sealed record RootSeed(Guid Entry, Guid Msa, Guid? OtherMsa, Guid[] Senses);

    /// <summary>A root entry; its last sense has its own analysis unless the senses share one.</summary>
    private RootSeed Root(string form, bool sharedAnalysis, params string[] glosses)
    {
        var services = _cache.ServiceLocator;
        var senses = new List<Guid>();
        Guid entryId = default, msaId = default;
        Guid? otherId = null;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var lexemeForm = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = lexemeForm;
            lexemeForm.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            lexemeForm.Form.set_String(_cache.DefaultVernWs, form);
            var pos = services.GetInstance<IPartOfSpeechRepository>().GetObject(_seed.PartOfSpeechId);
            var msa = StemMsa(entry, pos);
            msaId = msa.Guid;
            for (var i = 0; i < glosses.Length; i++)
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(_cache.DefaultAnalWs, glosses[i]);
                var last = i == glosses.Length - 1 && i > 0;
                if (last && !sharedAnalysis)
                {
                    var other = StemMsa(entry, pos);
                    otherId = other.Guid;
                    sense.MorphoSyntaxAnalysisRA = other;
                }
                else sense.MorphoSyntaxAnalysisRA = msa;
                senses.Add(sense.Guid);
            }
        });
        return new RootSeed(entryId, msaId, otherId, senses.ToArray());
    }

    private IMoStemMsa StemMsa(ILexEntry entry, IPartOfSpeech pos)
    {
        var msa = _cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
        entry.MorphoSyntaxAnalysesOC.Add(msa);
        msa.PartOfSpeechRA = pos;
        return msa;
    }

    private IPartOfSpeech CreateCategory(string name)
    {
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            category = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(_cache.DefaultAnalWs, name);
        });
        return category;
    }

    private ILexEntry Entry(Guid id) => _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(id);

    private ICmObjectRepository Repository => _cache.ServiceLocator.GetInstance<ICmObjectRepository>();

    private string Vern() => _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);

    private static CanonicalId Id(Guid guid) => CanonicalId.FromGuid(guid);

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
        operations);
}
