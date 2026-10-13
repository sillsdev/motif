using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorLexicalEntryParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AuthoredStemInflectionDerivationAndUnclassifiedAffixReachThePinnedParser()
    {
        string projectPath;
        var entries = new Dictionary<string, CanonicalId>();
        using (var cache = pristine.NewScratch())
        {
            foreach (var symbol in "amotifbsn")
                Apply(cache, AuthorPhonemeComposer.Build(cache, new AuthorPhonemeIntent(symbol.ToString(),
                    [symbol.ToString()])));
            var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
            var vern = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
            var category = Created(Apply(cache, AuthorPartOfSpeechComposer.Build(cache,
                new AuthorPartOfSpeechIntent("Synthetic noun", ws))), CmPossibilityListPossibilitiesOperationKinds.Create);
            var derived = Created(Apply(cache, AuthorPartOfSpeechComposer.Build(cache,
                new AuthorPartOfSpeechIntent("Synthetic derived", ws))), CmPossibilityListPossibilitiesOperationKinds.Create);
            var slot = Created(Apply(cache, AuthorAffixSlotComposer.Build(cache,
                new AuthorAffixSlotIntent(category, "Synthetic plural", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
            Apply(cache, AuthorAffixTemplateComposer.Build(cache,
                new AuthorAffixTemplateIntent(category, "Synthetic noun template", ws, [], [slot], true)));
            new FwDataProjectLoader().Save(cache);
            using (var baselineInvoker = new PanGlossInvoker())
            {
                var before = await new PanGlossTracer(baselineInvoker).TraceAsync(cache.ProjectId.Path, "mama",
                    CancellationToken.None);
                Assert.True(before is PanGlossTraceOutcome.Completed, before.ToString());
                Assert.Equal("-", ((PanGlossTraceOutcome.Completed)before).Summary.Signature);
            }
            Add("mama", MoMorphTypeTags.kguidMorphStem, "MAMA", new(LexicalEntryMsaKind.Stem, category));
            Add("s", MoMorphTypeTags.kguidMorphSuffix, "PL", new(LexicalEntryMsaKind.Inflectional, category, Slots: [slot]));
            Add("n", MoMorphTypeTags.kguidMorphPrefix, "DERIV", new(LexicalEntryMsaKind.Derivational, category, derived));
            Add("t", MoMorphTypeTags.kguidMorphPrefix, "UNC", new(LexicalEntryMsaKind.Unclassified, category));
            new FwDataProjectLoader().Save(cache);
            projectPath = cache.ProjectId.Path;

            void Add(string form, Guid morphType, string gloss, LexicalEntryMsa msa)
            {
                var proposal = Apply(cache, AuthorLexicalEntryComposer.Build(cache,
                    new AuthorLexicalEntryIntent(form, vern, CanonicalId.FromGuid(morphType), gloss, ws, msa)));
                entries.Add(gloss, Created(proposal, LexDbEntriesOperationKinds.Create));
            }
        }
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var importedPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "authored-entry-snapshot.json");
        var imported = await invoker.RunAsync(new PanGlossRequest.Import(projectPath, importedPath),
            "synthetic-entry-import", CancellationToken.None);
        Assert.True(imported is PanGlossOutcome.Completed, imported.Message);
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(importedPath));
        foreach (var (gloss, id) in entries)
        {
            var entry = Assert.Single(snapshot.RootElement.GetProperty("lexicon").GetProperty("entries")
                .EnumerateArray(), item => item.GetProperty("guid").GetString() == id.ToGuid().ToString());
            var sense = Assert.Single(entry.GetProperty("senses").EnumerateArray());
            Assert.Equal(gloss, Assert.Single(sense.GetProperty("gloss").EnumerateArray()).GetProperty("form").GetString());
            Assert.Equal(Assert.Single(entry.GetProperty("msas").EnumerateArray()).GetProperty("guid").GetString(),
                sense.GetProperty("msa").GetString());
        }
        foreach (var (word, signature) in new[]
            { ("mama", "|mama"), ("mamas", "+|mama+?s"), ("nmama", "+|n+?mama"), ("tmama", "+|t+?mama") })
        {
            var outcome = await tracer.TraceAsync(projectPath, word, CancellationToken.None);
            Assert.True(outcome is PanGlossTraceOutcome.Completed, outcome.ToString());
            Assert.Equal(signature, ((PanGlossTraceOutcome.Completed)outcome).Summary.Signature);
        }
        var negative = await tracer.TraceAsync(projectPath, "mamab", CancellationToken.None);
        Assert.True(negative is PanGlossTraceOutcome.Completed, negative.ToString());
        Assert.Equal("-", ((PanGlossTraceOutcome.Completed)negative).Summary.Signature);
    }

    private static Proposal Apply(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string>
            { ["lists"] = "1.0", ["grammar"] = "1.0", ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        ProposalApplier.Apply(cache, proposal, ScratchDryRun.Of(cache, proposal).Anchor, "synthetic-entry-parser-tests");
        return proposal;
    }

    private static CanonicalId Created(Proposal proposal, string kind) =>
        Assert.Single(proposal.Operations, operation => operation.Kind == kind).EntityId!.Value;
}
