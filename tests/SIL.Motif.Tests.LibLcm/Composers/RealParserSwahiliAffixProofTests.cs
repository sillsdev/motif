using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserSwahiliAffixProofTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task SameEntryMAndMwFormsRespectEnvironmentAndInflectionClassThroughPublicApply()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "t", "u", "w", "a", "l", "i");

        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var vernacularWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var categoryProposal = Apply(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Synthetic Swahili noun", analysisWs, "SN")));
        var category = Created(categoryProposal, CmPossibilityListPossibilitiesOperationKinds.Create);

        var class12Operations = AuthorInflectionClassComposer.Build(cache,
            new AuthorInflectionClassIntent(category, "G1/2", analysisWs, "G1/2"));
        var class34Operations = AuthorInflectionClassComposer.Build(cache,
            new AuthorInflectionClassIntent(category, "G3/4", analysisWs, "G3/4"));
        var setupProposal = Apply(cache, [.. class12Operations, .. class34Operations]);
        var classes = setupProposal.Operations
            .Where(operation => operation.Kind == PartOfSpeechInflectionClassesOperationKinds.Create)
            .Select(operation => operation.EntityId!.Value).ToArray();
        var class12 = classes[0];
        var class34 = classes[1];

        var phonemes = Phonemes(cache);
        var consonantClassProposal = Apply(cache, AuthorNaturalClassComposer.Build(cache,
            new AuthorNaturalClassIntent("Synthetic consonants", "C", [phonemes["m"], phonemes["t"], phonemes["w"], phonemes["l"]])));
        var consonants = Created(consonantClassProposal, PhPhonDataNaturalClassesOperationKinds.Create);
        var vowelClassProposal = Apply(cache, AuthorNaturalClassComposer.Build(cache,
            new AuthorNaturalClassIntent("Synthetic vowels", "V", [phonemes["a"], phonemes["u"], phonemes["i"]])));
        var vowels = Created(vowelClassProposal, PhPhonDataNaturalClassesOperationKinds.Create);

        var consonantEnvironmentProposal = Apply(cache, AuthorEnvironmentComposer.Build(cache,
            new AuthorEnvironmentIntent("Before consonant", [], [new EnvironmentContext(NaturalClass: consonants)])));
        var beforeConsonant = Created(consonantEnvironmentProposal, PhPhonDataEnvironmentsOperationKinds.Create);
        var vowelEnvironmentProposal = Apply(cache, AuthorEnvironmentComposer.Build(cache,
            new AuthorEnvironmentIntent("Before vowel", [], [new EnvironmentContext(NaturalClass: vowels)])));
        var beforeVowel = Created(vowelEnvironmentProposal, PhPhonDataEnvironmentsOperationKinds.Create);

        var slotProposal = Apply(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(category, "Synthetic noun prefix", analysisWs, false)));
        var slot = Created(slotProposal, PartOfSpeechAffixSlotsOperationKinds.Create);
        Apply(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(category, "Synthetic noun template", analysisWs, [slot], [], true)));

        var foreignPrefix = AddPrefix(cache, "m", "G3/4 m-", category, class34, slot, beforeConsonant);
        var mtuStem = AddStem(cache, "tu", "person", category, class12);
        var mwalimuStem = AddStem(cache, "alimu", "teacher", category, class12);
        _ = AddStem(cache, "utu", "personhood control", category, class12);
        var mtiStem = AddStem(cache, "ti", "tree class control", category, class34);

        var originalDigest = BaselineSemanticDigest.Compute(cache);
        var projectPath = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();

        const string productVersion = "0.1.0";
        const string draftName = "swahili-m-and-mw";
        Assert.True(ProposalCommands.New(new NewDraftRequest(projectPath, productVersion, draftName,
            "Add conditioned Swahili noun prefixes.")).Succeeded);
        var entryComposition = ProposalCommands.ComposeAuthorLexicalEntry(new ComposeAuthorLexicalEntryRequest(
            projectPath, productVersion, draftName, JsonSerializer.Serialize(new
            {
                form = "m",
                formWs = vernacularWs,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
                gloss = "G1/2 m-",
                analysisWs,
                msa = new
                {
                    kind = "inflectional",
                    category = category.Value,
                    slots = new[] { slot.Value },
                },
                inflectionClasses = new[] { class12.Value },
                environments = new[] { beforeConsonant.Value },
            })));
        Assert.True(entryComposition.Succeeded, entryComposition.Refusal?.Message);
        var entryOperations = entryComposition.Value!.Operations;
        var entryCreate = Assert.Single(entryOperations, operation => operation.Kind == LexDbEntriesOperationKinds.Create);
        var mPrefix = (Entry: CanonicalId.Parse(entryCreate.EntityId!),
            Form: CanonicalId.Parse(Assert.Single(entryOperations,
                operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm).EntityId!),
            Msa: CanonicalId.Parse(Assert.Single(entryOperations,
                operation => operation.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create).EntityId!));

        var alternateComposition = ProposalCommands.ComposeAuthorAlternateForm(new ComposeAuthorAlternateFormRequest(
            projectPath, productVersion, draftName, JsonSerializer.Serialize(new
            {
                entry = mPrefix.Entry.Value,
                form = "mw",
                formWs = vernacularWs,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
                environments = new[] { beforeVowel.Value },
                inflectionClasses = new[] { class12.Value },
            })));
        Assert.True(alternateComposition.Succeeded, alternateComposition.Refusal?.Message);
        var alternateOperations = alternateComposition.Value!.Operations;
        var alternateForm = CanonicalId.Parse(Assert.Single(alternateOperations,
            operation => operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms).EntityId!);
        Assert.True(ProposalCommands.Label(new LabelRequest(projectPath, productVersion, draftName,
            "Add Swahili m- and mw- forms.")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(projectPath, productVersion, draftName,
            "The forms share one G1/2 entry and use complementary environments.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, productVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        var loaded = ProjectStoreCommand.Run<Proposal>(projectPath, productVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(proposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var finalProposal = loaded.Value!;
        var storedAlternateCreate = Assert.Single(finalProposal.Operations,
            operation => operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        Assert.Contains(CanonicalId.Parse(entryCreate.OperationId), storedAlternateCreate.DependsOn
            .Select(dependency => dependency.OperationId));

        var beforeDryRunDigest = originalDigest;
        var dryRun = DryRunJobRunner.Run(projectPath, productVersion, proposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        using (var dryRunReadback = new FwDataProjectLoader().LoadCache(projectPath))
            Assert.Equal(beforeDryRunDigest, BaselineSemanticDigest.Compute(dryRunReadback));
        var applied = ProposalCommands.Apply(new ApplyRequest(projectPath, productVersion,
            proposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.NotEmpty(applied.Value!.Effects);

        using (var reloaded = new FwDataProjectLoader().LoadCache(projectPath))
        {
            var reloadedPrefix = Get<ILexEntry>(reloaded, mPrefix.Entry);
            var reloadedBaseForm = Assert.IsAssignableFrom<IMoAffixAllomorph>(reloadedPrefix.LexemeFormOA);
            var reloadedAlternate = Assert.IsAssignableFrom<IMoAffixAllomorph>(Assert.Single(reloadedPrefix.AlternateFormsOS));
            Assert.Equal(mPrefix.Form.ToGuid(), reloadedBaseForm.Guid);
            Assert.Equal(class12.ToGuid(), Assert.Single(reloadedBaseForm.InflectionClassesRC).Guid);
            Assert.Equal(beforeConsonant.ToGuid(), Assert.Single(reloadedBaseForm.PhoneEnvRC).Guid);
            Assert.Equal(class12.ToGuid(), Assert.Single(reloadedAlternate.InflectionClassesRC).Guid);
            Assert.Equal(beforeVowel.ToGuid(), Assert.Single(reloadedAlternate.PhoneEnvRC).Guid);
            Assert.Equal(class34.ToGuid(), Assert.Single(Assert.IsAssignableFrom<IMoAffixAllomorph>(
                Get<ILexEntry>(reloaded, foreignPrefix.Entry).LexemeFormOA).InflectionClassesRC).Guid);
            Assert.Equal(class34.ToGuid(), Assert.IsAssignableFrom<IMoStemMsa>(
                Assert.Single(Get<ILexEntry>(reloaded, mtiStem.Entry).MorphoSyntaxAnalysesOC)).InflectionClassRA!.Guid);
        }

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var mtu = await CompletedTrace(tracer, projectPath, "mtu");
        WriteWitness(output, "mtu", AssertReading(mtu, mPrefix.Form, mPrefix.Msa, mtuStem.Form, mtuStem.Msa));
        var mwalimu = await CompletedTrace(tracer, projectPath, "mwalimu");
        WriteWitness(output, "mwalimu", AssertReading(mwalimu, alternateForm, mPrefix.Msa,
            mwalimuStem.Form, mwalimuStem.Msa));
        var mutu = await CompletedTrace(tracer, projectPath, "mutu");
        AssertNoAnalyses(mutu);
        output.WriteLine("mutu: completed uncapped search; no analyses");

        var mtiBeforeClassRemoval = await CompletedTrace(tracer, projectPath, "mti");
        WriteWitness(output, "mti before class removal",
            AssertReading(mtiBeforeClassRemoval, foreignPrefix.Form, foreignPrefix.Msa, mtiStem.Form, mtiStem.Msa));
        Assert.DoesNotContain(mtiBeforeClassRemoval.Document!.Analyses.SelectMany(item => item.Morphs),
            morph => ParserId(morph.FormId) == mPrefix.Form.ToGuid());

        var removeOnlyClassRestriction = NewProposal([
            new OperationEnvelope(CanonicalId.Mint(), MoAffixFormInflectionClassesOperationKinds.RemoveRefInflectionClasses,
                target: mPrefix.Form,
                after: JsonSerializer.SerializeToElement(new { member = class12.Value })),
        ]);
        using (var classControlProject = new FwDataProjectLoader().LoadCache(projectPath))
        {
            var beforeControlDigest = BaselineSemanticDigest.Compute(classControlProject);
            var classControlDryRun = DryRun(classControlProject, removeOnlyClassRestriction);
            Assert.Equal(beforeControlDigest, BaselineSemanticDigest.Compute(classControlProject));
            ProposalApplier.Apply(classControlProject, removeOnlyClassRestriction, classControlDryRun.Anchor,
                "swahili-class-control");
            Assert.Empty(Assert.IsAssignableFrom<IMoAffixAllomorph>(
                Get<ILexEntry>(classControlProject, mPrefix.Entry).LexemeFormOA).InflectionClassesRC);
            Assert.Equal(beforeConsonant.ToGuid(), Assert.Single(Assert.IsAssignableFrom<IMoAffixAllomorph>(
                Get<ILexEntry>(classControlProject, mPrefix.Entry).LexemeFormOA).PhoneEnvRC).Guid);
            new FwDataProjectLoader().Save(classControlProject);
            Assert.NotEqual(beforeControlDigest, BaselineSemanticDigest.Compute(classControlProject));
        }

        var classControl = await CompletedTrace(tracer, projectPath, "mti");
        bool IsG1Reading(PanGlossTraceAnalysis analysis) =>
            analysis.Morphs.Any(morph => ParserId(morph.FormId) == mPrefix.Form.ToGuid() &&
                                         ParserId(morph.MsaId) == mPrefix.Msa.ToGuid()) &&
            analysis.Morphs.Any(morph => ParserId(morph.FormId) == mtiStem.Form.ToGuid() &&
                                         ParserId(morph.MsaId) == mtiStem.Msa.ToGuid());
        Assert.Contains(classControl.Document!.Analyses, IsG1Reading);
        WriteWitness(output, "mti after class removal", classControl.Document.Analyses.First(IsG1Reading));

    }

    private static (CanonicalId Entry, CanonicalId Form, CanonicalId Msa) AddPrefix(LcmCache cache,
        string form, string gloss, CanonicalId category, CanonicalId inflectionClass, CanonicalId slot,
        CanonicalId environment)
    {
        var operations = AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix), gloss, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category, Slots: [slot]), null,
            [inflectionClass], [environment]));
        var proposal = Apply(cache, operations);
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create));
    }

    private static (CanonicalId Entry, CanonicalId Form, CanonicalId Msa) AddStem(LcmCache cache,
        string form, string gloss, CanonicalId category, CanonicalId inflectionClass)
    {
        var operations = AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), gloss, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category, InflectionClass: inflectionClass)));
        var proposal = Apply(cache, operations);
        var stem = (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create));
        return stem;
    }

    private static PanGlossTraceAnalysis AssertReading(PanGlossTraceOutcome.Completed trace,
        CanonicalId prefixForm, CanonicalId prefixMsa, CanonicalId rootForm, CanonicalId rootMsa)
    {
        var analysis = Assert.Single(trace.Document!.Analyses);
        Assert.Equal([prefixForm.ToGuid(), rootForm.ToGuid()],
            analysis.Morphs.Select(morph => ParserId(morph.FormId)));
        Assert.Equal([prefixMsa.ToGuid(), rootMsa.ToGuid()],
            analysis.Morphs.Select(morph => ParserId(morph.MsaId)));
        return analysis;
    }

    private static Guid ParserId(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Guid.TryParse(value, out var parsed) ? parsed : CanonicalId.Parse(value).ToGuid();
    }

    private static void WriteWitness(ITestOutputHelper output, string word, PanGlossTraceAnalysis analysis)
    {
        var morphs = analysis.Morphs.Select(morph =>
            $"{CanonicalId.FromGuid(ParserId(morph.FormId)).Value}/{CanonicalId.FromGuid(ParserId(morph.MsaId)).Value}");
        output.WriteLine($"{word}: completed uncapped search; form/MSA IDs {string.Join(" + ", morphs)}");
    }

    private static void AssertNoAnalyses(PanGlossTraceOutcome.Completed trace)
    {
        Assert.Equal("-", trace.Summary.Signature);
        Assert.Empty(trace.Document!.Analyses);
    }

    private static async Task<PanGlossTraceOutcome.Completed> CompletedTrace(PanGlossTracer tracer,
        string projectPath, string word)
    {
        var outcome = await tracer.TraceAsync(projectPath, word, CancellationToken.None);
        var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome);
        Assert.True(completed.Summary.Completed);
        Assert.NotNull(completed.Details);
        Assert.True(completed.Details!.SearchCompleted, $"{word}: {completed.Details}");
        Assert.False(completed.Details.Capped, word);
        Assert.False(completed.Details.TimedOut, word);
        Assert.False(completed.Details.InvalidShape, word);
        Assert.NotNull(completed.Document);
        return completed;
    }

    private static Dictionary<string, CanonicalId> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);

    private static T Get<T>(LcmCache cache, CanonicalId id) where T : class, ICmObject =>
        (T)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());

    private static string Vern(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);

    private static string Anal(LcmCache cache) => cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);

    private static CanonicalId Created(Proposal proposal, string kind) =>
        Assert.Single(proposal.Operations, operation => operation.Kind == kind).EntityId!.Value;

    private static Proposal Apply(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = NewProposal(operations);
        var digest = BaselineSemanticDigest.Compute(cache);
        var dryRun = DryRun(cache, proposal);
        Assert.Equal(digest, BaselineSemanticDigest.Compute(cache));
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "swahili-proof-fixture");
        return proposal;
    }

    private static Proposal NewProposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lists"] = "1.0", ["lexical"] = "1.0", ["grammar"] = "1.0" },
        CanonicalId.Mint(), null, operations);

    private static SIL.Motif.Model.DryRun.DryRun DryRun(LcmCache live, Proposal proposal)
    {
        var projectPath = live.ProjectId.Path;
        new FwDataProjectLoader().Save(live);
        var scratchRoot = Path.Combine(Path.GetTempPath(), "motif-swahili-proof-" + Guid.NewGuid().ToString("N"));
        using var scratch = DryRunScratch.Adopt(new ScratchCacheFactory().CreateFromFileCopy(projectPath, scratchRoot),
            "file copy of the synthetic Swahili proof project", () =>
            {
                try { Directory.Delete(scratchRoot, recursive: true); }
                catch { }
            });
        return ProposalDryRunner.Run(scratch, proposal);
    }
}
