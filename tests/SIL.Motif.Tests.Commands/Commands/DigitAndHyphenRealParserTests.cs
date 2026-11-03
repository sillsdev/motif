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

/// <summary>
/// Motif authors what a numeral-bearing orthography needs (digits as phonemes, the ordinal prefix and its hyphenated
/// spelling) and what a hyphenated spelling of an affix process needs (a second process that inserts the hyphen), and the
/// real parser reads the resulting words. Entries are named by form and gloss with <c>lookup:</c>, never by id.
/// </summary>
[Collection(LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "System")]
public sealed class DigitAndHyphenRealParserTests(PristineProjectFixture pristine)
{
    private const string Version = "1.0";

    [RealParserFact]
    public async Task DigitsAsPhonemesLetTheOrdinalPrefixParseBesideANumeralWrittenInDigits()
    {
        var path = Prepare(out var ws, out var analysisWs, "i", "k", "a");
        var words = new[] { "ika-14", "ika14", "14" };
        var before = await Parse(path, words);
        // Digits are not in the phoneme table, so the parser cannot even segment the words.
        Assert.All(before, word => Assert.True(word.InvalidShape));

        NewDraft(path, "digits");
        Compose(path, "digits", "compose-author-phoneme", new { name = "1", representations = new[] { "1" } });
        Compose(path, "digits", "compose-author-phoneme", new { name = "4", representations = new[] { "4" } });
        Compose(path, "digits", "compose-author-phoneme", new { name = "-", representations = new[] { "-" } });
        Compose(path, "digits", "compose-author-lexical-entry", new
        {
            form = "ika", formWs = ws, morphType = MorphType(MoMorphTypeTags.kguidMorphPrefix), gloss = "ordinal",
            analysisWs, msa = new { kind = "unclassified", category = "lookup:category:Numeral" },
        });
        Compose(path, "digits", "compose-author-lexical-entry", new
        {
            form = "14", formWs = ws, morphType = MorphType(MoMorphTypeTags.kguidMorphStem), gloss = "fourteen",
            analysisWs, msa = new { kind = "stem", category = "lookup:category:Numeral" },
        });
        Compose(path, "digits", "compose-author-alternate-form", new
        {
            entry = "lookup:entry:ika-", form = "ika-", formWs = ws, morphType = MorphType(MoMorphTypeTags.kguidMorphPrefix),
        });
        ApplyDraft(path, "digits");

        var after = await Parse(path, words);
        Assert.All(after, word => Assert.False(word.InvalidShape));
        Assert.NotEmpty(after[0].Analyses);
        Assert.Contains(after[0].Analyses, analysis => analysis.Morphs.Count == 2);
        Assert.NotEmpty(after[1].Analyses);
        Assert.NotEmpty(after[2].Analyses);
    }

    [RealParserFact]
    public async Task AHyphenatedSpellingOfAnAffixProcessIsASecondProcessThatInsertsTheHyphen()
    {
        var words = new[] { "ikaab", "ika-ab" };
        var plain = await Parse(await ProcessProject(withHyphenatedProcess: false), words);
        Assert.NotEmpty(plain[0].Analyses);
        Assert.Empty(plain[1].Analyses);

        var hyphenated = await Parse(await ProcessProject(withHyphenatedProcess: true), words);
        Assert.NotEmpty(hyphenated[0].Analyses);
        Assert.NotEmpty(hyphenated[1].Analyses);
        Assert.Equal(2, hyphenated[1].Analyses[0].Morphs.Count);
    }

    private Task<string> ProcessProject(bool withHyphenatedProcess)
    {
        var path = Prepare(out var ws, out var analysisWs, "i", "k", "a", "b");
        NewDraft(path, "process");
        Compose(path, "process", "compose-author-phoneme", new { name = "-", representations = new[] { "-" } });
        Compose(path, "process", "compose-author-lexical-entry", new
        {
            form = "ab", formWs = ws, morphType = MorphType(MoMorphTypeTags.kguidMorphStem), gloss = "root",
            analysisWs, msa = new { kind = "stem", category = "lookup:category:Numeral" },
        });
        Compose(path, "process", "compose-author-affix-process", new
        {
            morphType = MorphType(MoMorphTypeTags.kguidMorphPrefix), gloss = "ika", analysisWs, form = "ika", formWs = ws,
            msa = new { kind = "unclassified", category = "lookup:category:Numeral" },
            input = new object[] { new { name = "rest", remaining = true } },
            output = new object[]
            {
                new { insert = new[] { "lookup:phoneme:i", "lookup:phoneme:k", "lookup:phoneme:a" } },
                new { copy = "rest" },
            },
        });
        if (withHyphenatedProcess)
            Compose(path, "process", "compose-author-affix-process-allomorph", new
            {
                entry = "lookup:entry:ika|ika",
                input = new object[] { new { name = "rest", remaining = true } },
                output = new object[]
                {
                    new { insert = new[] { "lookup:phoneme:i", "lookup:phoneme:k", "lookup:phoneme:a", "lookup:phoneme:-" } },
                    new { copy = "rest" },
                },
            });
        ApplyDraft(path, "process");
        return Task.FromResult(path);
    }

    private string Prepare(out string ws, out string analysisWs, params string[] letters)
    {
        using var cache = pristine.NewScratch();
        ws = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
        analysisWs = cache.ServiceLocator.WritingSystems.DefaultAnalysisWritingSystem.Id;
        RealParserProject.PrepareForParsing(cache, letters);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var pos = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("Numeral", cache.DefaultAnalWs));
        });
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }

    private static string MorphType(Guid guid) => CanonicalId.FromGuid(guid).Value;

    private static void NewDraft(string path, string draft)
    {
        var created = ProposalCommands.New(new(path, Version, draft, draft));
        Assert.True(created.Succeeded, created.Refusal?.Message);
    }

    private static void Compose(string path, string draft, string verb, object intent)
    {
        var composed = ProposalCommands.ComposeIntent(new(path, Version, draft, verb, JsonSerializer.Serialize(intent)));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
    }

    private static void ApplyDraft(string path, string draft)
    {
        DraftRationale.Author(path, draft, draft, "Digits and hyphens are written in the orthography.");
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
    }

    private static async Task<IReadOnlyList<ParseWordEvidence>> Parse(string path, string[] words)
    {
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(path, words, TimeSpan.FromSeconds(5)) { CollectAnalyses = true },
            "test:digits-and-hyphens", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var evidence = ParseMorphEvidence.Read(completed.MorphologyOutput!, words);
        Assert.All(evidence, word =>
        {
            Assert.False(word.Capped || word.TimedOut);
            Assert.Null(word.Refusal);
        });
        return evidence;
    }
}
