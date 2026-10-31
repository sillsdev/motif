using System.Text.Json;
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
public sealed class MergePhonemeRealParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AWordSpelledWithTheMovedCodeParsesAsTheTargetAfterTheMergeIsApplied()
    {
        const string version = "1.0";
        const string draft = "merge-parser";
        string path;
        Guid targetId;
        Guid sourceId;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            MergePhonemeDraftTests.Phonemes(cache, out targetId, out sourceId);
        }

        // The seeded entry is spelled motifb, with o; mutifb writes the same word with u.
        var words = new[] { SeededProject.SecondForm, "mutifb" };
        var before = await Parse(path, words);
        AssertHasForm(before[0], pristine.Seed.SecondLexemeFormId);
        Assert.Empty(before[1].Analyses);

        Assert.True(ProposalCommands.New(new(path, version, draft, "Merge o into u")).Succeeded);
        var intent = JsonSerializer.Serialize(new
        {
            source = CanonicalId.FromGuid(sourceId).Value,
            target = CanonicalId.FromGuid(targetId).Value,
        });
        var staged = ProposalCommands.ComposeIntent(new(path, version, draft, "compose-merge-phoneme", intent));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var stagedReadings = await Parse(path, words);
        Assert.Empty(stagedReadings[1].Analyses);

        DraftRationale.Author(path, draft, "Merge o into u", "The language writes one vowel two ways.");
        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        var after = await Parse(path, words);
        AssertHasForm(after[0], pristine.Seed.SecondLexemeFormId);
        AssertHasForm(after[1], pristine.Seed.SecondLexemeFormId);
    }

    [RealParserFact]
    public async Task AppliedMergeKeepsTheSpellingOnTheTargetAndRemovesTheSourceAndASplitCanThenBeStaged()
    {
        string path;
        Guid targetId;
        Guid sourceId;
        int phonemeCount;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            MergePhonemeDraftTests.Phonemes(cache, out targetId, out sourceId);
            phonemeCount = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.Count;
        }

        await Parse(path, [SeededProject.SecondForm]);
        ApplyIntent(path, "merge-apply", "compose-merge-phoneme",
            Json(new { source = CanonicalId.FromGuid(sourceId).Value, target = CanonicalId.FromGuid(targetId).Value }));

        using (var reloaded = new FwDataProjectLoader().LoadCache(path))
        {
            var set = reloaded.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
            Assert.Equal(phonemeCount - 1, set.PhonemesOC.Count);
            Assert.DoesNotContain(set.PhonemesOC, p => p.Guid == sourceId);
            var target = Assert.Single(set.PhonemesOC, p => p.Guid == targetId);
            Assert.Equal(["u", "o"], target.CodesOS.Select(c => c.Representation.get_String(reloaded.DefaultVernWs).Text));
        }

        // A second Apply needs a fresh Baseline, so the split is staged against the applied state rather than applied.
        Assert.True(ProposalCommands.New(new(path, Version, "split", "Split o out again")).Succeeded);
        var split = ProposalCommands.ComposeIntent(new(path, Version, "split", "compose-split-phoneme",
            Json(new { source = CanonicalId.FromGuid(targetId).Value, representation = "o", name = "o" })));
        Assert.True(split.Succeeded, split.Refusal?.Message);
        Assert.Equal(["grammar/phTerminalUnit/deleteCodes", "grammar/phPhonemeSet/createPhonemes",
                "grammar/phTerminalUnit/setName", "grammar/phTerminalUnit/createCodes", "grammar/phCode/setRepresentation"],
            split.Value!.Operations.Select(item => item.Kind));
    }

    private static void ApplyIntent(string path, string draft, string verb, string intent)
    {
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Change a phoneme")).Succeeded);
        var staged = ProposalCommands.ComposeIntent(new(path, Version, draft, verb, intent));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        DraftRationale.Author(path, draft, "Change a phoneme", "One sound is written two ways.");
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
    }
    private static string Json(object value) => JsonSerializer.Serialize(value);

    private const string Version = "1.0";

    private static void AssertHasForm(ParseWordEvidence word, Guid formId) =>
        Assert.Contains(word.Analyses, analysis => analysis.Morphs.Any(morph => morph.Form == formId.ToString("D")));

    private static async Task<IReadOnlyList<ParseWordEvidence>> Parse(string path, string[] words)
    {
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(path, words, TimeSpan.FromSeconds(5)) { CollectAnalyses = true },
            "test:merge-phoneme", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
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
