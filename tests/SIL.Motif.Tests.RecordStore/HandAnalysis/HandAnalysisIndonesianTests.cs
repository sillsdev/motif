using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisIndonesianTests(ImportedProject project) : IClassFixture<ImportedProject>
{
    private static readonly ExternalBasis Basis = new("source-1", "parser-1", "settings-1");

    private async Task<(HandAnalysisHost Host, StoreSnapshot Pin)> PinAsync(string? store = null)
    {
        var host = await HandAnalysisHost.OpenAsync(store ?? project.Store);
        return (host, await host.PinAsync(Basis, null));
    }

    private static ImmutableDictionary<string, string> Alternatives(LcmCache cache, IMultiAccessorBase text) =>
        text.AvailableWritingSystemIds.ToImmutableDictionary(h => cache.ServiceLocator.WritingSystemManager.Get(h).Id,
            h => text.get_String(h).Text ?? "", StringComparer.Ordinal);

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task ProjectionEqualsAnIndependentLcmReadForEveryWordform()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        Assert.Equal(54, pin.AnalysisCount);
        Assert.Equal(123, pin.BundleCount);
        var analysisOrder = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances().Select((a, i) => (a.Guid, i)).ToDictionary(p => p.Guid, p => p.i);
        var wordforms = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().ToArray();
        var facts = await Pages.All(p => pin.Inventory(p), 200);
        Assert.Equal(wordforms.Select(w => w.Guid), facts.Select(h => h.Id));
        var seen = 0;
        foreach (var (word, fact) in wordforms.Zip(facts))
        {
            Assert.Equal(Alternatives(cache, word.Form).OrderBy(p => p.Key), fact.Forms.OrderBy(p => p.Key));
            Assert.Equal(word.AnalysesOC.OrderBy(a => analysisOrder[a.Guid]).Select(a => a.Guid), fact.Analyses);
            foreach (var analysis in word.AnalysesOC)
            {
                var reading = Assert.IsType<WordFact.Reading>(Assert.Single((await pin.Words(TestWord.Reading(analysis.Guid), new PageRequest())).Items));
                seen++;
                Assert.Equal(word.Guid, reading.WordformId);
                Assert.Equal(analysis.CategoryRA?.Guid, reading.CategoryId);
                Assert.Equal(analysis.MorphBundlesOS.Select(b => (Guid?)b.Guid), reading.Bundles.Select(b => b.Id));
                foreach (var (bundle, fact2) in analysis.MorphBundlesOS.Zip(reading.Bundles))
                {
                    Assert.Equal((bundle.MorphRA?.Guid, bundle.SenseRA?.Guid, bundle.MsaRA?.Guid, bundle.InflTypeRA?.Guid),
                        (fact2.MorphId, fact2.SenseId, fact2.MsaId, fact2.InflTypeId));
                    Assert.Equal(Alternatives(cache, bundle.Form).OrderBy(p => p.Key), fact2.Forms.OrderBy(p => p.Key));
                    Assert.Equal(bundle.Guid.ToString("D"), fact2.MemberId);
                }
                Assert.Equal(analysis.MeaningsOC.Select(g => g.Guid).Order(), reading.Glosses.Select(g => g.Id).Order());
                foreach (var gloss in analysis.MeaningsOC)
                    Assert.Equal(Alternatives(cache, gloss.Form).OrderBy(p => p.Key),
                        reading.Glosses.Single(g => g.Id == gloss.Guid).Forms.OrderBy(p => p.Key));
                Assert.Null(reading.OccurrenceUseCount);
                Assert.Equal(analysis.EvaluationsRC.Select(e => (e.Guid, e.Approves, ((ICmAgent)e.Owner).Guid, (bool?)((ICmAgent)e.Owner).Human)).OrderBy(e => e.Item1),
                    reading.Evaluations.Select(e => (e.Id, e.Approves, e.AgentId!.Value, e.Human)).OrderBy(e => e.Id));
            }
        }
        Assert.Equal(54, seen);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task SchemaBindingMatchesTheLcmFieldIds()
    {
        var (host, pin) = await PinAsync();
        await using var _ = host;
        await using var store = new Store(project.Store);
        var records = await store.ReadAsync();
        var schema = new WfiSchema(NativeUsageSchema.Read(records)!);
        Assert.Equal((WfiWordformTags.kflidForm, WfiWordformTags.kflidAnalyses, WfiAnalysisTags.kflidMorphBundles, WfiAnalysisTags.kflidCategory,
                WfiAnalysisTags.kflidMeanings, WfiAnalysisTags.kflidEvaluations, WfiMorphBundleTags.kflidMorph, WfiMorphBundleTags.kflidMsa,
                WfiMorphBundleTags.kflidSense, WfiMorphBundleTags.kflidInflType, WfiMorphBundleTags.kflidForm, WfiGlossTags.kflidForm,
                CmAgentTags.kflidHuman, CmAgentTags.kflidApproves),
            (schema.WordformForm, schema.Analyses, schema.Bundles, schema.Category, schema.Meanings, schema.Evaluations, schema.Morph, schema.Msa,
                schema.Sense, schema.InflType, schema.BundleForm, schema.GlossForm, schema.AgentHuman, schema.AgentApproves));
        Assert.Equal(WfiWordformTags.kflidSpellingStatus, schema.Spelling);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task IdentityFindsWordformsByExactNfdFormAndNeverByCase()
    {
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var headers = await Pages.All(p => pin.Inventory(p), 200);
        foreach (var header in headers)
        foreach (var (ws, text) in header.Forms)
        {
            var found = (await pin.Words(new WordQuery.Identity(TestWord.Key(ws, text)), new PageRequest())).Items.Cast<WordFact.Header>();
            Assert.Contains(header.Id, found.SelectMany(h => h.ExactMatches));
            var composed = new WordIdentity.CaseForm(new(ws, text.Normalize(System.Text.NormalizationForm.FormC)));
            Assert.Contains(header.Id, (await pin.Words(new WordQuery.Identity(composed), new()))
                .Items.Cast<WordFact.Header>().SelectMany(h => h.ExactMatches));
            var native = Assert.IsType<WordFact.Header>(Assert.Single((await pin.Words(
                new WordQuery.Identity(new WordIdentity.Native(header.Id)), new())).Items));
            Assert.Equal(header.Id, Assert.Single(native.ExactMatches));
            Assert.Null(native.Key);
        }
        foreach (var header in headers)
        foreach (var (ws, text) in header.Forms.Where(f => f.Value != f.Value.ToUpperInvariant()))
        {
            var shouted = await pin.Words(new WordQuery.Identity(TestWord.Key(ws, text.ToUpperInvariant())), new PageRequest());
            Assert.DoesNotContain(header.Id, shouted.Items.Cast<WordFact.Header>().SelectMany(h => h.ExactMatches));
        }
        var absent = await pin.Words(new WordQuery.Identity(TestWord.Key("id", "no-such-word-" + Guid.NewGuid())), new PageRequest());
        Assert.Empty(absent.Items);
        Assert.Equal(0, absent.Total);
        Assert.True(absent.Complete);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task AlternativesAndReadingsAreCompleteAndInEncounterOrder()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var order = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances().Select(a => a.Guid).ToList();
        var checkedWords = 0;
        foreach (var word in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().Where(w => w.AnalysesOC.Count > 0))
        {
            var ws = cache.ServiceLocator.WritingSystemManager.Get(word.Form.AvailableWritingSystemIds[0]).Id;
            var key = TestWord.Key(ws, word.Form.get_String(word.Form.AvailableWritingSystemIds[0]).Text);
            var identity = Assert.IsType<WordFact.Header>(Assert.Single((await pin.Words(new WordQuery.Identity(key), new())).Items));
            if (identity.ExactMatches.Length != 1) continue;
            var page = await pin.Words(new WordQuery.Alternatives(key, WordOrder.Encounter), new PageRequest());
            var readings = page.Items.Cast<WordFact.Reading>().ToArray();
            Assert.Equal(word.AnalysesOC.Select(a => a.Guid).OrderBy(order.IndexOf), readings.Select(TestWord.AnalysisId));
            Assert.All(readings, r => Assert.Equal(order.IndexOf(TestWord.AnalysisId(r)), long.Parse(r.EncounterKey.Split(':')[0])));
            Assert.Equal(readings.Length, page.Total);
            var ranked = await pin.Words(new WordQuery.Alternatives(key, WordOrder.DefaultRank), new PageRequest());
            Assert.Equal("rank-unavailable", Assert.Single(ranked.Limitations).Code);
            Assert.Null(ranked.Total);
            Assert.False(ranked.Complete);
            foreach (var reading in readings)
            foreach (var gloss in reading.Glosses)
                Assert.Equal(reading with { SelectedGlossId = gloss.Id },
                    Assert.Single((await pin.Words(new WordQuery.Reading(new ReadingKey.NativeGloss(gloss.Id)), new PageRequest())).Items));
            checkedWords++;
        }
        Assert.True(checkedWords > 0);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task PagesConcatenateToTheUnpagedResultAndCursorsAreBound()
    {
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var whole = await Pages.All(p => pin.Inventory(p), 200);
        Assert.True(whole.Count > 7);
        foreach (var size in new[] { 1, 7, 50, 200 })
            Assert.Equal(whole, await Pages.All(p => pin.Inventory(p), size));
        var first = await pin.Inventory(new(1));
        Assert.Equal(whole.Count, first.Total);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor, (await pin.WordsAsync(
            new WordQuery.Identity(TestWord.Key("x", "y")), new(1, first.NextCursor))).Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor, (await pin.InventoryAsync(new(1, "not a cursor"))).Refusal?.Code);
        await using var other = await host.PinAsync(Basis with { ParserRevision = "parser-2" }, null);
        Assert.NotEqual(pin.Stamp.SnapshotId, other.Stamp.SnapshotId);
        Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor, (await other.InventoryAsync(new(1, first.NextCursor))).Refusal?.Code);
        foreach (var limit in new[] { 0, 201 })
            Assert.Equal(RefusalCodes.HandAnalysisInvalidPageLimit,
                (await pin.WordsAsync(new WordQuery.Identity(new WordIdentity.Native(whole[0].Id)), new(limit))).Refusal?.Code);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task APinSurvivesAWriteAndTheStampsDiffer()
    {
        var store = project.CopyStore();
        var (host, a) = await PinAsync(store);
        await using var _ = host;
        var before = Pages.Json(await Pages.All(p => a.Inventory(p), 200));
        Assert.True(a.IsCurrent);
        Assert.Equal(SnapshotVerification.Full, a.Stamp.Verification);
        Assert.Equal("none", a.Stamp.ComparisonVersion);
        await using var writer = new Store(store);
        var entry = (await writer.ReadAsync()).First(r => r.Kind == "lcm" && r.ClassId == LexEntryTags.kClassId);
        var key = LexEntryTags.kflidHomographNumber.ToString();
        var old = entry.Values[key];
        await writer.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, old, JsonSerializer.SerializeToElement(old.GetInt32() + 1),
            a.Stamp.HomePoint, Guid.NewGuid());
        Assert.False(a.IsCurrent);
        var b = await host.PinAsync(Basis, null);
        Assert.NotEqual(a.Stamp.SnapshotId, b.Stamp.SnapshotId);
        Assert.NotEqual(a.Stamp.HomePoint, b.Stamp.HomePoint);
        Assert.False(a.IsCurrent);
        Assert.True(b.IsCurrent);
        Assert.Equal(before, Pages.Json(await Pages.All(p => a.Inventory(p), 200)));
        Assert.Equal(a.WfiFactsDigest, b.WfiFactsDigest);
        Assert.Equal((54, 123), (b.AnalysisCount, b.BundleCount));
        Assert.Equal(a.Stamp.SnapshotId, (await host.PinAsync(Basis, a.Stamp.SnapshotId)).Stamp.SnapshotId);
        await Assert.ThrowsAsync<SnapshotExpiredException>(() => host.PinAsync(Basis, "unknown"));
        await using var other = await HandAnalysisHost.OpenAsync(project.CopyStore());
        await other.PinAsync(Basis, null);
        await Assert.ThrowsAsync<SnapshotExpiredException>(() => other.PinAsync(Basis, b.Stamp.SnapshotId));
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task ReadsLeaveHistoryStateAndDatabaseBytesUnchanged()
    {
        var store = project.CopyStore();
        string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(store)));
        await using var observer = new Store(store);
        var commits = (await observer.CommitsAsync()).Count;
        var revision = (await observer.ReferencesAsync()).Revision;
        var entry = (await observer.ReadAsync()).First(r => r.ClassId == LexEntryTags.kClassId);
        var before = Hash();
        await using var host = await HandAnalysisHost.OpenAsync(store);
        await using var pin = await host.PinAsync(Basis, null);
        await Pages.All(p => pin.Inventory(p), 7);
        Assert.Equal(pin.Stamp, (await pin.References(new ReferenceQuery.DeleteClosure(new(DeletionKind.Entry, entry.Id)), new PageRequest())).Stamp);
        Assert.Equal(pin.Stamp, (await pin.References(new ReferenceQuery.Uses([new(ReferenceKind.Entry, entry.Id)], true), new PageRequest())).Rows.Stamp);
        Assert.Equal(before, Hash());
        Assert.Equal(commits, (await observer.CommitsAsync()).Count);
        Assert.Equal(revision, (await observer.ReferencesAsync()).Revision);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task DeletionPreviewEqualsAnIndependentLcmCountOverTheOwningClosure()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var bundlesOf = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var bundle in cache.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().AllInstances())
            foreach (var used0 in new ICmObject?[] { bundle.MorphRA, bundle.SenseRA, bundle.MsaRA, bundle.InflTypeRA }.OfType<ICmObject>())
                for (ICmObject? up = used0; up is not null; up = up.Owner)
                {
                    if (!bundlesOf.TryGetValue(up.Guid, out var set)) bundlesOf[up.Guid] = set = [];
                    set.Add(bundle.Guid);
                }
        var targets = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
            .Where(o => o is ILexEntry or ILexSense or IMoForm).ToArray();
        var referenced = 0;
        foreach (var target in targets)
        {
            var bundles = bundlesOf.GetValueOrDefault(target.Guid, []);
            var analyses = bundles.Select(b => ((IWfiMorphBundle)cache.ServiceLocator.GetObject(b)).Owner.Guid).ToHashSet();
            var impact = pin.PreviewDeletion(target.Guid);
            Assert.True(impact.NativeComplete, string.Join("; ", impact.Limitations));
            Assert.Equal(bundles.Order(), impact.Bundles);
            Assert.Equal(analyses.Order(), impact.Analyses);
            Assert.Equal(bundles.Count == 0, impact.Links.IsEmpty);
            Assert.False(impact.DeleteAllowed);
            Assert.Empty(impact.Witnesses);
            Assert.DoesNotContain(impact.Limitations, l => l.Contains("Anchor", StringComparison.Ordinal));
            var kind = target is ILexEntry ? DeletionKind.Entry : target is ILexSense ? DeletionKind.Sense : DeletionKind.Allomorph;
            var rows = await pin.References(new ReferenceQuery.DeleteClosure(new(kind, target.Guid)), new(200));
            Assert.Null(rows.Counts.Bundles);
            Assert.Null(rows.Counts.Analyses);
            Assert.Null(rows.Counts.PlanToken);
            Assert.Null(rows.Rows.Total);
            Assert.Equal(bundles.Order(), rows.Rows.Items.Select(r => r.BundleId).OfType<Guid>().Distinct().Order());
            Assert.Equal(analyses.Order(), rows.Rows.Items.Select(r => r.AnalysisId).OfType<Guid>().Distinct().Order());
            if (bundles.Count > 0) referenced++;
        }
        Assert.True(referenced > 0);
        var used = targets.First(t => bundlesOf.ContainsKey(t.Guid));
        await using var store = new Store(project.Store);
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(used.Guid, pin.Stamp.HomePoint, null));
        var token = pin.PreviewDeletion(used.Guid).PlanToken;
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(used.Guid, pin.Stamp.HomePoint, token));
    }
}
