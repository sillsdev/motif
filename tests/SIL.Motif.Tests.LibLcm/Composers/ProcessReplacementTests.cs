using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class ProcessReplacementTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();
    public void Dispose() => _cache.Dispose();

    [Fact]
    public void ProhibitionRetargetPreservesAdjacencyAndDisabledAndRejectsCrossEntryReplacement()
    {
        var entries = _cache.LangProject.LexDbOA.Entries.Take(2).ToArray();
        var rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        var alternate = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            entries[0].AlternateFormsOS.Add(alternate);
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.FirstAllomorphRA = entries[0].LexemeFormOA;
            rule.RestOfAllosRS.Add(entries[1].LexemeFormOA);
            rule.Adjacency = 3;
            rule.Disabled = true;
        });
        var expected = AdhocReferenceState.Read(rule);
        var after = expected with { First = Id(alternate) };
        var operations = EditAdhocProhibitionReferencesComposer.Build(_cache, new(Id(rule), expected, after));
        SoundSystemComposerTests.Execute(_cache, operations);
        Assert.Equal(alternate.Guid, rule.FirstAllomorphRA.Guid);
        Assert.Equal(3, rule.Adjacency);
        Assert.True(rule.Disabled);
        var error = Assert.Throws<InvalidOperationException>(() => EditAdhocProhibitionReferencesComposer.Build(_cache,
            new(Id(rule), AdhocReferenceState.Read(rule), after with { First = Id(entries[1].LexemeFormOA) })));
        Assert.Contains("same entry", error.Message);
    }

    [Fact]
    public void DeletionChecksExpectedStateAndKeepsEveryReferencedForm()
    {
        var (entry, process) = SeedProcess();
        var other = _cache.LangProject.LexDbOA.Entries.First(item => item.Guid != entry.Guid).LexemeFormOA;
        var rule = Rule(process, other);
        var expected = AdhocReferenceState.Read(rule);
        Assert.Throws<InvalidOperationException>(() => DeleteAdhocProhibitionComposer.Build(_cache,
            new(Id(rule), expected with { Disabled = true })));
        var proposal = Proposal(DeleteAdhocProhibitionComposer.Build(_cache, new(Id(rule), expected)));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.True(rule.IsValidObject);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "process-tests");
        Assert.False(rule.IsValidObject);
        Assert.True(process.IsValidObject);
        Assert.True(other.IsValidObject);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Preview?.GetRawText().Contains("deletedProhibition") == true);
    }

    [Fact]
    public void AllRemainingProhibitionReferencesAreNamedBeforeDeletingAProcess()
    {
        var (entry, process) = SeedProcess();
        var other = _cache.LangProject.LexDbOA.Entries.First(item => item.Guid != entry.Guid).LexemeFormOA;
        var first = Rule(process, other);
        var second = Rule(other, process);
        var error = Assert.Throws<InvalidOperationException>(() => DeleteAffixProcessComposer.Build(_cache, new(Id(entry), Id(process))));
        Assert.Contains(Id(first).Value, error.Message);
        Assert.Contains(Id(second).Value, error.Message);
        Assert.Contains("FirstAllomorph", error.Message);
        Assert.Contains("RestOfAllos[0]", error.Message);
        Assert.Contains("compose-delete-adhoc-prohibition", error.Message);
        Assert.True(process.IsValidObject);
    }

    [Fact]
    public void RetargetAndDeleteInOneProposalKeepsProhibitionsAndPromotesThePlainForm()
    {
        var (entry, process) = SeedProcess();
        var other = _cache.LangProject.LexDbOA.Entries.First(item => item.Guid != entry.Guid).LexemeFormOA;
        var first = Rule(process, other);
        var second = Rule(other, process);
        var alternate = ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache,
            new(Id(entry), "paN", Vern(_cache), Id(process.MorphTypeRA))));
        var survivor = Get<IMoAffixAllomorph>(_cache, Created(alternate, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms));
        var firstBefore = AdhocReferenceState.Read(first);
        var secondBefore = AdhocReferenceState.Read(second);
        var edits = EditAdhocProhibitionReferencesComposer.Build(_cache,
            new(Id(first), firstBefore, firstBefore with { First = Id(survivor) })).Concat(
            EditAdhocProhibitionReferencesComposer.Build(_cache,
                new(Id(second), secondBefore, secondBefore with { Rest = [Id(survivor)] }))).ToArray();
        var graph = OwnedGraphGuard.Members(process).Select(Id).ToArray();
        var senses = entry.SensesOS.Select(Id).ToArray();
        var msas = entry.MorphoSyntaxAnalysesOC.Select(Id).ToArray();
        var firstId = Id(first);
        var secondId = Id(second);
        var processId = Id(process);
        var path = _cache.ProjectId.Path;
        new FwDataProjectLoader().Save(_cache);
        IReadOnlyList<OperationEnvelope> deletion;
        using (var scratch = new FwDataProjectLoader().LoadScratchCache(path))
        {
            SoundSystemComposerTests.Execute(scratch, edits);
            deletion = DeleteAffixProcessComposer.Build(scratch, new(Id(entry), processId, Id(survivor)));
        }
        var delete = Assert.Single(deletion);
        var orderedDelete = new OperationEnvelope(delete.OperationId, delete.Kind, target: delete.Target, after: delete.After,
            dependsOn: edits.Select(edit => new OperationDependency(edit.OperationId)).ToArray());
        var proposal = Proposal(new[] { orderedDelete }.Concat(edits.Reverse()).ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.True(process.IsValidObject);
        Assert.Equal(processId.ToGuid(), first.FirstAllomorphRA.Guid);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "process-tests");
        Assert.Equal(survivor.Guid, entry.LexemeFormOA.Guid);
        Assert.Empty(entry.AlternateFormsOS);
        Assert.All(graph, member => Assert.False(Exists(_cache, member)));
        Assert.Equal(senses, entry.SensesOS.Select(Id));
        Assert.Equal(msas, entry.MorphoSyntaxAnalysesOC.Select(Id));
        Assert.Equal(survivor.Guid, first.FirstAllomorphRA.Guid);
        Assert.Equal(survivor.Guid, Assert.Single(second.RestOfAllosRS).Guid);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(path);
        Assert.Equal(survivor.Guid, Get<ILexEntry>(reopened, Id(entry)).LexemeFormOA.Guid);
        Assert.Equal(survivor.Guid, Get<IMoAlloAdhocProhib>(reopened, firstId).FirstAllomorphRA.Guid);
        Assert.Equal(survivor.Guid, Assert.Single(Get<IMoAlloAdhocProhib>(reopened, secondId).RestOfAllosRS).Guid);
    }

    [Fact]
    public void GraphChangeAfterCompositionAndNewReferenceAfterDryRunBothRefuse()
    {
        var (entry, process) = SeedProcess();
        var operations = DeleteAffixProcessComposer.Build(_cache, new(Id(entry), Id(process)));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => process.IsAbstract = true);
        Assert.Throws<InvalidOperationException>(() => SoundSystemComposerTests.Execute(_cache, operations));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => process.IsAbstract = false);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var other = _cache.LangProject.LexDbOA.Entries.First(item => item.Guid != entry.Guid).LexemeFormOA;
        Rule(process, other);
        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "process-tests"));
        Assert.True(process.IsValidObject);
    }

    [Fact]
    public void ChangingAnExistingPlainReplacementAfterDryRunRefusesApply()
    {
        var (entry, process) = SeedProcess();
        var created = ApplyOps(_cache, AuthorAlternateFormComposer.Build(_cache,
            new(Id(entry), "paN", Vern(_cache), Id(process.MorphTypeRA))));
        var survivor = Get<IMoAffixAllomorph>(_cache, Created(created, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms));
        var proposal = Proposal(DeleteAffixProcessComposer.Build(_cache, new(Id(entry), Id(process), Id(survivor))));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            survivor.Form.set_String(_cache.DefaultVernWs, "different"));
        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "process-tests"));
        Assert.True(process.IsValidObject);
        Assert.Equal(process.Guid, entry.LexemeFormOA.Guid);
    }

    [Fact]
    public void FailureAfterWholeGraphDeletionRollsBackTheGraphAndKeepsTheAppliedLogEmpty()
    {
        var (entry, process) = SeedProcess();
        var members = OwnedGraphGuard.Members(process).Select(Id).ToArray();
        var proposal = Proposal(DeleteAffixProcessComposer.Build(_cache, new(Id(entry), Id(process))));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var prior = ProjectAppliedLog.ReadAll(_cache).Count;
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "process-tests", "", (_, _) => throw new InvalidOperationException("injected failure")));
        Assert.All(members, member => Assert.True(Exists(_cache, member)));
        Assert.Equal(prior, ProjectAppliedLog.ReadAll(_cache).Count);
    }

    [Fact]
    public void MorphemeRestAndLegacyAllomorphOccurrencesRetargetInTheirOriginalOrder()
    {
        var (entry, process) = SeedProcess();
        var other = _cache.LangProject.LexDbOA.Entries.First(item => item.Guid != entry.Guid);
        var alternate = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
        var msa = _cache.ServiceLocator.GetInstance<IMoUnclassifiedAffixMsaFactory>().Create();
        var morphRule = _cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
        var alloRule = Rule(other.LexemeFormOA, process);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            entry.AlternateFormsOS.Add(alternate);
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(morphRule);
            morphRule.FirstMorphemeRA = other.MorphoSyntaxAnalysesOC.First();
            morphRule.RestOfMorphsRS.Add(entry.MorphoSyntaxAnalysesOC.First());
            alloRule.AllomorphsRS.Add(process);
            alloRule.AllomorphsRS.Add(process);
        });
        var morphBefore = AdhocReferenceState.Read(morphRule);
        var alloBefore = AdhocReferenceState.Read(alloRule);
        ApplyOps(_cache, EditAdhocProhibitionReferencesComposer.Build(_cache,
            new(Id(morphRule), morphBefore, morphBefore with { Rest = [Id(msa)] })));
        ApplyOps(_cache, EditAdhocProhibitionReferencesComposer.Build(_cache,
            new(Id(alloRule), alloBefore, alloBefore with { Rest = [Id(alternate)], Allomorphs = [Id(alternate), Id(alternate)] })));
        Assert.Equal(msa.Guid, Assert.Single(morphRule.RestOfMorphsRS).Guid);
        Assert.Equal(new[] { alternate.Guid, alternate.Guid }, alloRule.AllomorphsRS.Select(item => item.Guid));
    }

    [Fact]
    public void UnknownDestinationsAndWrongReplacementEntriesRefuseWithoutMutation()
    {
        var (entry, process) = SeedProcess();
        var other = _cache.LangProject.LexDbOA.Entries.First(item => item.Guid != entry.Guid);
        var rule = Rule(process, other.LexemeFormOA);
        var before = AdhocReferenceState.Read(rule);
        Assert.ThrowsAny<Exception>(() => EditAdhocProhibitionReferencesComposer.Build(_cache,
            new(Id(rule), before, before with { First = CanonicalId.Mint() })));
        Assert.Throws<InvalidOperationException>(() => EditAdhocProhibitionReferencesComposer.Build(_cache,
            new(Id(rule), before, before with { Disabled = true })));
        Assert.Throws<InvalidOperationException>(() => DeleteAffixProcessComposer.Build(_cache,
            new(Id(other), Id(process))));
        Assert.Equal(process.Guid, rule.FirstAllomorphRA.Guid);
    }

    [Fact]
    public void BundleAndCustomReferencesIntoTheOwnedGraphAreEnumeratedAndBlockDeletion()
    {
        var (entry, process) = SeedProcess();
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var contextClass = metadata.GetClassIds().Single(id => metadata.GetClassName(id) == "PhContextOrVar");
        var field = metadata.AddCustomField("LexEntry", "ProcessInputReference",
            SIL.LCModel.Core.Cellar.CellarPropertyType.ReferenceAtomic, contextClass);
        var other = _cache.LangProject.LexDbOA.Entries.First(item => item.Guid != entry.Guid);
        var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
        var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
                SIL.LCModel.Core.Text.TsStringUtils.MakeString("process-test", _cache.DefaultVernWs));
            wordform.AnalysesOC.Add(analysis);
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = process;
            _cache.DomainDataByFlid.SetObjProp(other.Hvo, field, process.InputOS[0].Hvo);
        });
        var error = Assert.Throws<InvalidOperationException>(() => DeleteAffixProcessComposer.Build(_cache, new(Id(entry), Id(process))));
        Assert.Contains("WfiMorphBundle", error.Message);
        Assert.Contains(Id(bundle).Value, error.Message);
        Assert.Contains("ProcessInputReference", error.Message);
        Assert.Contains(Id(process.InputOS[0]).Value, error.Message);
        Assert.True(process.IsValidObject);
    }

    private (ILexEntry, IMoAffixProcess) SeedProcess()
    {
        RealParserProject.PrepareForParsing(_cache, "p", "a", "N", "b", "g", "o");
        var category = AddCategory(_cache, "Verb");
        var phone = _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.First();
        var created = ApplyOps(_cache, AuthorAffixProcessComposer.Build(_cache,
            new(Id(_cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix)),
                "prefix", Anal(_cache), new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, category),
                [new("rest", Remaining: true)], [new(Insert: [Id(phone)]), new(Copy: "rest")])));
        var process = Get<IMoAffixProcess>(_cache, Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
        return ((ILexEntry)process.Owner, process);
    }

    private IMoAlloAdhocProhib Rule(IMoForm first, IMoForm rest)
    {
        var rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.FirstAllomorphRA = first;
            rule.RestOfAllosRS.Add(rest);
        });
        return rule;
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}
