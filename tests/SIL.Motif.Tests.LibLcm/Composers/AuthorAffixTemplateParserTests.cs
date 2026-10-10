using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class AuthorAffixTemplateParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AlternativeTemplateAdmitsItsBranchAndKeepsMixedBranchNegativeRejected()
    {
        using var cache = pristine.NewScratch();
        var grammar = CreateBranchedGrammar(cache);
        RealParserProject.PrepareForParsing(cache, "m", "k", "a", "b", "d", "t");
        var words = new[] { "mkat", "bkad", "bkat" };
        var before = await Parse(cache.ProjectId.Path, words);

        Assert.Equal(WordOutcome.Analysed, before.Words[0].Outcome);
        Assert.Equal(WordOutcome.NoAnalysis, before.Words[1].Outcome);
        Assert.Equal(WordOutcome.NoAnalysis, before.Words[2].Outcome);

        var writingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var operations = AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(Id(grammar.Category), "New affix branch", writingSystem,
                [Id(grammar.NewPrefixSlot)], [Id(grammar.NewSuffixSlot)], Final: true));
        var templateId = Assert.Single(operations,
            operation => operation.Kind == PartOfSpeechAffixTemplatesOperationKinds.Create).EntityId ??
            throw new InvalidOperationException("The composer did not assign the template id.");
        var proposal = new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" },
            CanonicalId.Mint(), null, operations.Reverse().ToArray());
        Assert.Empty(ChangeFitPreflight.Check(cache, proposal));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.PartOfSpeechAffixTemplates);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "author-affix-template-parser-tests");
        new FwDataProjectLoader().Save(cache);

        var after = await Parse(cache.ProjectId.Path, words);
        Assert.Equal(WordOutcome.Analysed, after.Words[0].Outcome);
        Assert.Equal(WordOutcome.Analysed, after.Words[1].Outcome);
        Assert.Equal(WordOutcome.NoAnalysis, after.Words[2].Outcome);
        var alternativeEvidence = Assert.Single(ParseMorphEvidence.Read(after.MorphologyOutput!, words)
            .Single(word => word.Word == words[1]).Analyses);
        Assert.Contains(alternativeEvidence.Morphs, morph =>
            morph.Form == grammar.NewPrefix.Guid.ToString("D"));
        Assert.Contains(alternativeEvidence.Morphs, morph =>
            morph.Form == grammar.NewSuffix.Guid.ToString("D"));

        Assert.Equal(grammar.OldTemplatePrefixOrder, grammar.OldTemplate.PrefixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal(grammar.OldTemplateSuffixOrder, grammar.OldTemplate.SuffixSlotsRS.Select(slot => slot.Guid));
        var created = Assert.IsAssignableFrom<IMoInflAffixTemplate>(
            cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(templateId.ToGuid()));
        Assert.Equal([grammar.NewPrefixSlot.Guid], created.PrefixSlotsRS.Select(slot => slot.Guid));
        Assert.Equal([grammar.NewSuffixSlot.Guid], created.SuffixSlotsRS.Select(slot => slot.Guid));
    }

    private static async Task<BatchRun> Parse(string projectPath, IReadOnlyList<string> words)
    {
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(projectPath, words,
            TimeSpan.FromSeconds(5)) { CollectAnalyses = true }, "alternative affix template", cancellation.Token,
            wallClockCap: TimeSpan.FromSeconds(45));
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return new BatchRun(BatchTsvParser.Parse(completed.Output), completed.MorphologyOutput!);
    }

    private static BranchedGrammar CreateBranchedGrammar(LcmCache cache)
    {
        IPartOfSpeech category = null!;
        IMoInflAffixSlot oldPrefixSlot = null!;
        IMoInflAffixSlot oldSuffixSlot = null!;
        IMoInflAffixSlot newPrefixSlot = null!;
        IMoInflAffixSlot newSuffixSlot = null!;
        IMoInflAffixTemplate oldTemplate = null!;
        ILexEntry newPrefix = null!;
        ILexEntry newSuffix = null!;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            category = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, "Template branch test");

            oldPrefixSlot = CreateSlot(cache, category, "Old prefix");
            oldSuffixSlot = CreateSlot(cache, category, "Old suffix");
            newPrefixSlot = CreateSlot(cache, category, "New prefix");
            newSuffixSlot = CreateSlot(cache, category, "New suffix");

            oldTemplate = CreateTemplate(cache, category, "Existing branch", [oldPrefixSlot], [oldSuffixSlot]);

            services.GetInstance<ILexEntryFactory>().Create(
                services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("ka", cache.DefaultVernWs), "root",
                SandboxGenericMSA.Create(MsaType.kStem, category));

            CreateAffix(cache, category, oldPrefixSlot, MoMorphTypeTags.kguidMorphPrefix, "m-", "old prefix");
            CreateAffix(cache, category, oldSuffixSlot, MoMorphTypeTags.kguidMorphSuffix, "-t", "old suffix");
            newPrefix = CreateAffix(cache, category, newPrefixSlot, MoMorphTypeTags.kguidMorphPrefix, "b-", "new prefix");
            newSuffix = CreateAffix(cache, category, newSuffixSlot, MoMorphTypeTags.kguidMorphSuffix, "-d", "new suffix");
        });

        return new BranchedGrammar(category, oldTemplate, newPrefixSlot, newSuffixSlot,
            newPrefix.LexemeFormOA, newSuffix.LexemeFormOA,
            oldTemplate.PrefixSlotsRS.Select(slot => slot.Guid).ToArray(),
            oldTemplate.SuffixSlotsRS.Select(slot => slot.Guid).ToArray());
    }

    private static IMoInflAffixSlot CreateSlot(LcmCache cache, IPartOfSpeech category, string name)
    {
        var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
        category.AffixSlotsOC.Add(slot);
        slot.Name.set_String(cache.DefaultAnalWs, name);
        return slot;
    }

    private static IMoInflAffixTemplate CreateTemplate(LcmCache cache, IPartOfSpeech category, string name,
        IReadOnlyList<IMoInflAffixSlot> prefixes, IReadOnlyList<IMoInflAffixSlot> suffixes)
    {
        var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
        category.AffixTemplatesOS.Add(template);
        template.Name.set_String(cache.DefaultAnalWs, name);
        template.Final = true;
        foreach (var prefix in prefixes) template.PrefixSlotsRS.Add(prefix);
        foreach (var suffix in suffixes) template.SuffixSlotsRS.Add(suffix);
        return template;
    }

    private static ILexEntry CreateAffix(LcmCache cache, IPartOfSpeech category, IMoInflAffixSlot slot,
        Guid morphType, string form, string gloss)
    {
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryFactory>().Create(
            services.GetInstance<IMoMorphTypeRepository>().GetObject(morphType),
            TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
            SandboxGenericMSA.Create(MsaType.kInfl, category));
        var msa = Assert.IsAssignableFrom<IMoInflAffMsa>(entry.MorphoSyntaxAnalysesOC.Single());
        msa.SlotsRC.Add(slot);
        return entry;
    }

    private sealed record BranchedGrammar(
        IPartOfSpeech Category,
        IMoInflAffixTemplate OldTemplate,
        IMoInflAffixSlot NewPrefixSlot,
        IMoInflAffixSlot NewSuffixSlot,
        IMoForm NewPrefix,
        IMoForm NewSuffix,
        Guid[] OldTemplatePrefixOrder,
        Guid[] OldTemplateSuffixOrder);

    private sealed record BatchRun(IReadOnlyList<WordAnalysis> Words, string MorphologyOutput);

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}
