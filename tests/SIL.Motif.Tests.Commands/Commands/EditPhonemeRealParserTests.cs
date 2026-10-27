using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "System")]
public sealed class EditPhonemeRealParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AppliedDigraphChangesRealParserReadingsWhileStagingAloneDoesNot()
    {
        const string version = "1.0";
        const string draft = "digraph-parser";
        string path;
        Guid phonemeId;
        Guid codeId;
        Guid firstMsaId;
        Guid secondMsaId;
        int phonemeCount;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
            firstMsaId = entries.GetObject(pristine.Seed.FirstEntryId).MorphoSyntaxAnalysesOC.Single().Guid;
            secondMsaId = entries.GetObject(pristine.Seed.SecondEntryId).MorphoSyntaxAnalysesOC.Single().Guid;
            var form = (IMoStemAllomorph)cache.ServiceLocator.GetInstance<IMoFormRepository>()
                .GetObject(pristine.Seed.FirstLexemeFormId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                form.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("na", cache.DefaultVernWs)));
            RealParserProject.PrepareForParsing(cache, "n", "g", "a", "m", "o", "t", "i", "f", "b");
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
            var phoneme = set.PhonemesOC.Single(p => p.CodesOS.Any(c =>
                c.Representation.get_String(cache.DefaultVernWs)?.Text == "n"));
            phonemeId = phoneme.Guid;
            codeId = phoneme.CodesOS.Single().Guid;
            phonemeCount = set.PhonemesOC.Count;
        }

        var words = new[] { "nga", "na", SeededProject.SecondForm };
        var before = await Parse(path, words);
        Assert.Empty(before[0].Analyses);
        AssertHasForm(before[1], pristine.Seed.FirstLexemeFormId, firstMsaId);
        AssertHasForm(before[2], pristine.Seed.SecondLexemeFormId, secondMsaId);

        var created = ProposalCommands.New(new(path, version, draft, "Add a digraph"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var intent = JsonSerializer.Serialize(new
        {
            target = CanonicalId.FromGuid(phonemeId).Value,
            add = new[] { new { representation = "ng" } },
        });
        var staged = ProposalCommands.ComposeIntent(new(path, version, draft, "compose-edit-phoneme", intent));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var stagedReadings = await Parse(path, words);
        Assert.Empty(stagedReadings[0].Analyses);
        AssertHasForm(stagedReadings[1], pristine.Seed.FirstLexemeFormId, firstMsaId);
        AssertHasForm(stagedReadings[2], pristine.Seed.SecondLexemeFormId, secondMsaId);

        DraftRationale.Author(path, draft, "Add a digraph", "The same sound is also written with two letters.");
        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        using (var reloaded = new FwDataProjectLoader().LoadCache(path))
        {
            var set = reloaded.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
            Assert.Equal(phonemeCount, set.PhonemesOC.Count);
            var phoneme = Assert.Single(set.PhonemesOC, p => p.Guid == phonemeId);
            Assert.Contains(phoneme.CodesOS, code => code.Guid == codeId &&
                code.Representation.get_String(reloaded.DefaultVernWs)?.Text == "n");
            Assert.Single(phoneme.CodesOS, code => code.Representation.get_String(reloaded.DefaultVernWs)?.Text == "ng");
            Assert.Single(set.PhonemesOC.SelectMany(p => p.CodesOS), code =>
                code.Representation.get_String(reloaded.DefaultVernWs)?.Text == "ng");
        }

        var after = await Parse(path, words);
        AssertHasForm(after[0], pristine.Seed.FirstLexemeFormId, firstMsaId);
        AssertHasForm(after[1], pristine.Seed.FirstLexemeFormId, firstMsaId);
        AssertHasForm(after[2], pristine.Seed.SecondLexemeFormId, secondMsaId);
    }

    private static void AssertHasForm(ParseWordEvidence word, Guid formId, Guid msaId) =>
        Assert.Contains(word.Analyses, analysis => analysis.Morphs.Any(morph =>
            morph.Form == formId.ToString("D") && morph.Msa == msaId.ToString("D")));

    private static async Task<IReadOnlyList<ParseWordEvidence>> Parse(string path, string[] words)
    {
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(path, words, TimeSpan.FromSeconds(5)) { CollectAnalyses = true },
            "test:edit-phoneme", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var evidence = ParseMorphEvidence.Read(completed.MorphologyOutput!, words);
        Assert.All(evidence, word =>
        {
            Assert.False(word.Capped || word.TimedOut || word.InvalidShape);
            Assert.Null(word.Refusal);
            Assert.Empty(word.Unavailable);
        });
        return evidence;
    }
}
