using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class CreatedAffixRetargetDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatedAlternateCanBeRetargetedAndRetiredInOneDraft(bool includeAdhoc)
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        ILexEntry entry = null!;
        IMoAffixAllomorph retired = null!;
        IMoInflAffMsa msa = null!;
        IWfiWordform wordform = null!;
        IWfiAnalysis analysis = null!;
        IWfiMorphBundle bundle = null!;
        IMoAlloAdhocProhib? rule = null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            entry = services.GetInstance<ILexEntryFactory>().Create();
            var prefix = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix);
            var lexeme = services.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.LexemeFormOA = lexeme;
            lexeme.MorphTypeRA = prefix;
            lexeme.Form.set_String(cache.DefaultVernWs, "ka");
            retired = services.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(retired);
            retired.MorphTypeRA = prefix;
            retired.Form.set_String(cache.DefaultVernWs, "ta");
            msa = services.GetInstance<IMoInflAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            wordform = services.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString("word", cache.DefaultVernWs));
            analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = retired;
            bundle.MsaRA = msa;
            if (!includeAdhoc) return;
            rule = services.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.Disabled = true;
            rule.FirstAllomorphRA = retired;
        });
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        const string draft = "created-retarget";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Replace a form")).Succeeded);
        var authored = ProposalCommands.ComposeAuthorAlternateForm(new(path, Version, draft, JsonSerializer.Serialize(new
        {
            entry = Id(entry.Guid), form = "ta", formWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
            morphType = Id(MoMorphTypeTags.kguidMorphPrefix), placement = new { after = Id(retired.Guid) }
        })));
        Assert.True(authored.Succeeded, authored.Refusal?.Message);
        var create = Assert.Single(authored.Value!.Operations, item => item.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        var createdId = CanonicalId.Parse(create.EntityId!).ToGuid();
        using var staged = new FwDataProjectLoader().LoadScratchCache(path);
        var stagedEntry = staged.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entry.Guid);
        IMoAffixAllomorph created = null!;
        NonUndoableUnitOfWorkHelper.Do(staged.ActionHandlerAccessor, () =>
        {
            created = staged.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create(createdId);
            stagedEntry.AlternateFormsOS.Add(created);
            created.MorphTypeRA = staged.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix);
            created.Form.set_String(staged.DefaultVernWs, "ta");
        });
        var stagedRetired = staged.ServiceLocator.GetInstance<IMoAffixAllomorphRepository>().GetObject(retired.Guid);
        AllomorphIdentity Identity(IMoAffixAllomorph form) => new(Id(form.Guid), Id(entry.Guid), form.ClassName,
            "alternate", "prefix", AllomorphRetirementSemanticDigest.Compute(staged, form), null);
        var survivor = Identity(created);
        var retiredIdentity = Identity(stagedRetired);
        var intent = new RetireAllomorphIntent(Id(entry.Guid), AllomorphRetirementScope.Affix, [retiredIdentity],
            [new(Id(retired.Guid), Id(msa.Guid), null, "whole", survivor)],
            [new(Id(bundle.Guid), Id(analysis.Guid), Id(wordform.Guid), Id(retired.Guid), Id(msa.Guid), null, "whole")],
            includeAdhoc ? [new(Id(rule!.Guid), "FirstAllomorph", null, Id(retired.Guid), survivor)] : []);
        var retirement = ProposalCommands.ComposeIntent(new(path, Version, draft, "retire-allomorph",
            RetireAllomorphCodec.ToJson(new(intent, [survivor]))));
        Assert.True(retirement.Succeeded, retirement.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, draft, "Replace the duplicate form and retain its uses.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var proposal = stored.Value!;
        var createId = CanonicalId.Parse(create.OperationId);
        var retarget = Assert.Single(proposal.Operations, op => op.Kind == AllomorphRetargetOperationKinds.SetBundleMorph);
        Assert.Contains(retarget.DependsOn, dependency => dependency.OperationId == createId);
        if (includeAdhoc)
        {
            var adhoc = Assert.Single(proposal.Operations, op => op.Kind == AllomorphRetargetOperationKinds.RetargetAdhocReferences);
            Assert.Contains(adhoc.DependsOn, dependency => dependency.OperationId == createId);
        }
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "created affix retirement");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");
        Assert.Equal(createdId, bundle.MorphRA!.Guid);
        if (includeAdhoc) Assert.Equal(createdId, rule!.FirstAllomorphRA!.Guid);
        Assert.False(retired.IsValidObject);
    }

    private static string Id(Guid id) => CanonicalId.FromGuid(id).Value;
}
