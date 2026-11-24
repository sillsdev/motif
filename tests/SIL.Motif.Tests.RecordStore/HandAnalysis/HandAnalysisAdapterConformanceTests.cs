using System.Collections.Immutable;
using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.RecordStore.Lcm;
using SIL.WritingSystems;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

/// <summary>
/// The adapter comparison on Indonesian. motif-cli's adapter conformance suite and FwDataHandAnalysisIndexBuilder are not on
/// main, so the same query families run against this index and against an independent read of the LCM cache that supplied it.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisAdapterConformanceTests(ImportedProject project) : IClassFixture<ImportedProject>
{
    private static readonly ExternalBasis Basis = new("source-1", "parser-1", "settings-1");

    private async Task<(HandAnalysisHost Host, StoreSnapshot Pin)> PinAsync()
    {
        var host = await HandAnalysisHost.OpenAsync(project.Store, null, default, IcuFormCollation.Factory);
        return (host, await host.PinAsync(Basis, null));
    }

    private static ImmutableDictionary<string, string> Alt(LcmCache cache, IMultiAccessorBase text) =>
        text.AvailableWritingSystemIds.ToImmutableDictionary(h => cache.ServiceLocator.WritingSystemManager.Get(h).Id,
            h => text.get_String(h).Text ?? "", StringComparer.Ordinal).Where(p => p.Value.Length > 0)
            .ToImmutableDictionary(StringComparer.Ordinal);

    private static IEnumerable<KeyValuePair<string, string>> Sorted(IEnumerable<KeyValuePair<string, string>> map) =>
        map.Where(p => p.Value.Length > 0).OrderBy(p => p.Key, StringComparer.Ordinal);

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task EveryEntryClosureEqualsAnIndependentLcmRead()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToArray();
        Assert.NotEmpty(entries);
        var facts = new Dictionary<Guid, LexicalFact>();
        foreach (var chunk in entries.Chunk(150))
            foreach (var fact in (await pin.Lexicon(new LexiconQuery.ByIds([.. chunk.Select(e => e.Guid)], [], [], []), new(200))).Items)
                facts.Add(fact.EntryId, fact);
        Assert.Equal(entries.Length, facts.Count);
        var msaClasses = new HashSet<string>();
        var refs = 0;
        foreach (var entry in entries)
        {
            var fact = facts[entry.Guid];
            var expectedHeadword = Alt(cache, entry.LexemeFormOA?.Form ?? entry.CitationForm).ToBuilder();
            foreach (var (ws, text) in Alt(cache, entry.CitationForm)) expectedHeadword[ws] = text;
            Assert.Equal(Sorted(expectedHeadword), Sorted(fact.Headword));
            Assert.Equal(Sorted(Alt(cache, entry.CitationForm)), Sorted(fact.Citation));
            Assert.Equal(entry.HomographNumber, fact.Homograph);
            Assert.Equal(entry.LexemeFormOA?.Guid, fact.LexemeFormId);
            Assert.Equal(entry.AlternateFormsOS.Select(f => f.Guid), fact.AlternateFormIds.AsEnumerable());
            var senses = new List<(Guid, Guid?, string, ILexSense)>();
            void Walk(IEnumerable<ILexSense> list, Guid? parent, string prefix)
            {
                var n = 0;
                foreach (var sense in list)
                {
                    var number = prefix.Length == 0 ? $"{++n}" : $"{prefix}.{++n}";
                    senses.Add((sense.Guid, parent, number, sense));
                    Walk(sense.SensesOS, sense.Guid, number);
                }
            }
            Walk(entry.SensesOS, null, "");
            Assert.Equal(senses.Select(s => (s.Item1, s.Item2, s.Item3)), fact.Senses.Select(s => (s.Id, s.ParentId, s.Number)));
            foreach (var (sense, mine) in senses.Zip(fact.Senses))
            {
                Assert.Equal(Sorted(Alt(cache, sense.Item4.Gloss)), Sorted(mine.Gloss));
                Assert.Equal(Sorted(Alt(cache, sense.Item4.Definition)), Sorted(mine.Definition));
                Assert.Equal(sense.Item4.MorphoSyntaxAnalysisRA?.Guid, mine.MsaId);
            }
            Assert.Equal(entry.MorphoSyntaxAnalysesOC.Select(m => m.Guid), fact.Msas.Select(m => m.Id));
            foreach (var msa in entry.MorphoSyntaxAnalysesOC)
            {
                var mine = fact.Msas.Single(m => m.Id == msa.Guid);
                Assert.Equal(msa.ClassName, mine.NativeClass);
                msaClasses.Add(msa.ClassName);
                var (category, from, to) = msa switch
                {
                    IMoStemMsa s => (s.PartOfSpeechRA?.Guid, (Guid?)null, (Guid?)null),
                    IMoInflAffMsa i => (i.PartOfSpeechRA?.Guid, null, null),
                    IMoUnclassifiedAffixMsa u => (u.PartOfSpeechRA?.Guid, null, null),
                    IMoDerivAffMsa d => (null, d.FromPartOfSpeechRA?.Guid, d.ToPartOfSpeechRA?.Guid),
                    _ => (mine.CategoryId, mine.FromCategoryId, mine.ToCategoryId)
                };
                Assert.Equal((category, from, to), (mine.CategoryId, mine.FromCategoryId, mine.ToCategoryId));
            }
            Assert.Equal(entry.MorphoSyntaxAnalysesOC.Count, fact.Msas.Select(m => m.Id).Distinct().Count());
            Assert.Equal(entry.EntryRefsOS.Select(r => r.Guid), fact.References.Select(r => r.Id));
            foreach (var (reference, mine) in entry.EntryRefsOS.Zip(fact.References))
            {
                refs++;
                Assert.Equal(reference.RefType == LexEntryRefTags.krtVariant ? "variant" : "complex-form", mine.NativeKind);
                Assert.Equal(entry.Guid, mine.OwnerId);
                Assert.Equal(reference.ComponentLexemesRS.Select(c => c is ILexSense s ? (s.Entry.Guid, (Guid?)s.Guid) : (c.Guid, null)),
                    mine.Components.Select(c => (c.EntryId!.Value, c.SenseId)));
                Assert.Equal((reference.RefType == LexEntryRefTags.krtVariant ? reference.VariantEntryTypesRS : reference.ComplexEntryTypesRS)
                    .Select(t => t.Guid), mine.TypeIds.AsEnumerable());
            }
            Assert.Equal(entry.EntryRefsOS.SelectMany(r => r.VariantEntryTypesRS.OfType<ILexEntryInflType>()).Select(t => t.Guid).Distinct().Order(),
                fact.InflectionalTypes.Select(t => t.Id).Order());
        }
        Assert.True(refs > 0 || entries.Length > 0);
        Assert.Contains("MoStemMsa", msaClasses);
    }

    private static async Task<ImmutableArray<FormHit>> AllForms(StoreSnapshot pin, FormQuery query)
    {
        var hits = ImmutableArray.CreateBuilder<FormHit>();
        string? cursor = null;
        do
        {
            var page = await pin.Forms(query, new(200, cursor));
            hits.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return hits.ToImmutable();
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task FormLookupEqualsAnIndependentCollationComparisonForEveryFormAndWritingSystem()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var forms = cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances().Where(f => f.MorphTypeRA is not null).ToArray();
        var repositoryOrder = forms.Select((f, i) => (f.Guid, i)).ToDictionary(p => p.Guid, p => p.i);
        var collators = new Dictionary<string, ICollator?>();
        ICollator? Collator(string ws) => collators.TryGetValue(ws, out var c) ? c :
            collators[ws] = cache.ServiceLocator.WritingSystemManager.Get(ws).DefaultCollation?.Collator;
        int Compare(string ws, string a, string b) => Collator(ws) is { } collator ? collator.Compare(a, b) : string.CompareOrdinal(
            a.Normalize(System.Text.NormalizationForm.FormD), b.Normalize(System.Text.NormalizationForm.FormD));

        // Independent use counts: distinct analyses with a bundle on the form.
        var uses = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var analysis in cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances())
            foreach (var bundle in analysis.MorphBundlesOS.Where(b => b.MorphRA is not null))
            {
                if (!uses.TryGetValue(bundle.MorphRA.Guid, out var set)) uses[bundle.MorphRA.Guid] = set = [];
                set.Add(analysis.Guid);
            }

        // Equivalence classes: sort each (ws, type) group by the LCM collator, cut where Compare is not 0.
        var texts = forms.ToDictionary(f => f.Guid, f => Alt(cache, f.Form));
        var classes = new List<(string Ws, Guid Type, FormTypeMarker Marker, Guid[] Members, string Text)>();
        foreach (var group in forms.SelectMany(f => texts[f.Guid].Select(t => (Form: f, Ws: t.Key, Text: t.Value)))
                     .GroupBy(x => (x.Ws, x.Form.MorphTypeRA!.Guid)))
        {
            var sorted = group.OrderBy(x => x.Text, Comparer<string>.Create((a, b) => Compare(group.Key.Ws, a, b)))
                .ThenBy(x => repositoryOrder[x.Form.Guid]).ToArray();
            for (var start = 0; start < sorted.Length;)
            {
                var end = start + 1;
                while (end < sorted.Length && Compare(group.Key.Ws, sorted[start].Text, sorted[end].Text) == 0) end++;
                var type = sorted[start].Form.MorphTypeRA!;
                classes.Add((group.Key.Ws, type.Guid, new(type.Guid, new(type.Prefix ?? "", type.Postfix ?? "")),
                    sorted[start..end].Select(x => x.Form.Guid).OrderBy(g => repositoryOrder[g]).ToArray(), sorted[start].Text));
                start = end;
            }
        }
        Assert.True(classes.Count > 50);
        var checkedHits = new HashSet<Guid>();
        foreach (var (ws, _, marker, members, text) in classes)
        {
            var page = await AllForms(pin, LexiconPages.Equal(ws, text, FormOrder.Encounter, marker));
            Assert.Equal(members, page.Select(h => h.Id).ToArray());
            var usage = await AllForms(pin, LexiconPages.Equal(ws, text, FormOrder.Usage, marker));
            Assert.Equal(members.Select((g, i) => (g, n: uses.GetValueOrDefault(g)?.Count ?? 0, i)).OrderByDescending(x => x.n)
                .ThenBy(x => x.i).Select(x => x.g), usage.Select(h => h.Id));
            Assert.All(usage, h => Assert.Equal(uses.GetValueOrDefault(h.Id)?.Count ?? 0, h.DistinctAnalysisUseCount));
            // Equivalent forms share one comparison key, so the chooser order falls back to the encounter order.
            var chooser = await AllForms(pin, LexiconPages.Equal(ws, text, FormOrder.Chooser, marker));
            Assert.Equal(members, chooser.Select(h => h.Id).ToArray());
            foreach (var hit in page.Where(h => checkedHits.Add(h.Id)))
            {
                var native = forms.Single(g => g.Guid == hit.Id);
                var owner = native.Owner is ILexEntry entry ? entry : (ILexEntry)native.Owner;
                Assert.Equal((owner.Guid, owner.LexemeFormOA == native, native.ClassName),
                    (hit.EntryId, hit.IsLexeme, hit.NativeClass));
                Assert.Equal(Sorted(Alt(cache, native.Form)), Sorted(hit.Forms));
                Assert.Equal(native.IsAbstract, hit.IsAbstract);
                Assert.Equal(native.MorphTypeRA!.Guid, hit.MorphTypeId);
            }
        }
        Assert.Equal(forms.Count(f => !texts[f.Guid].IsEmpty), checkedHits.Count);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task PatternModeAndTheEntryFilterEqualAnIndependentLcmRead()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var ws = pin.Hand.Project.WritingSystems.First(w => w.VernacularOrder == 0).Tag;
        var forms = cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances().Where(f => f.MorphTypeRA is not null).ToArray();
        var processes = forms.OfType<IMoAffixProcess>().ToArray();
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToArray();
        var subset = entries.Where((_, i) => i % 7 == 0).Select(e => e.Guid).ToHashSet();
        var typed = 0;
        foreach (var type in forms.Select(f => f.MorphTypeRA!).DistinctBy(t => t.Guid))
        {
            var marker = new FormTypeMarker(type.Guid, new(type.Prefix ?? "", type.Postfix ?? ""));
            var expected = processes.Where(f => f.MorphTypeRA!.Guid == type.Guid).ToArray();
            var page = await AllForms(pin, new FormQuery(ws, "", [marker], FormMatch.Pattern, FormOrder.Encounter));
            Assert.Equal(expected.Select(f => f.Guid), page.Select(h => h.Id));
            Assert.All(page, h => Assert.NotNull(h.Pattern));
            var filtered = await AllForms(pin, new FormQuery(ws, "", [marker], FormMatch.Pattern, FormOrder.Encounter) with { EntryIds = [.. subset] });
            Assert.Equal(expected.Where(f => subset.Contains(((ILexEntry)f.Owner).Guid)).Select(f => f.Guid), filtered.Select(h => h.Id));
            typed += expected.Length;
        }
        Assert.True(typed > 0 || processes.Length == 0);
        // The entry filter on an equal lookup keeps exactly the forms of those entries, in encounter order.
        var probe = forms.Where(f => Alt(cache, f.Form).ContainsKey(ws)).Take(400).ToArray();
        foreach (var form in probe)
        {
            var type = form.MorphTypeRA!;
            var marker = new FormTypeMarker(type.Guid, new(type.Prefix ?? "", type.Postfix ?? ""));
            var owner = ((ILexEntry)(form.Owner is ILexEntry e ? e : form.Owner)).Guid;
            var page = await AllForms(pin, LexiconPages.Equal(ws, Alt(cache, form.Form)[ws], FormOrder.Encounter, marker) with { EntryIds = [owner] });
            Assert.Contains(form.Guid, page.Select(h => h.Id));
            Assert.All(page, h => Assert.Equal(owner, h.EntryId));
        }
    }

    private sealed record Slot(Guid Text, Guid Paragraph, Guid Segment, int Index, IAnalysis Analysis);

    private static IEnumerable<Slot> Slots(LcmCache cache) => cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances()
        .SelectMany(t => t.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? [], (t, p) => (t, p))
        .SelectMany(x => x.p.SegmentsOS, (x, s) => (x.t, x.p, s))
        .SelectMany(x => x.s.AnalysesRS.Select((a, i) => new Slot(x.t.Guid, x.p.Guid, x.s.Guid, i, a)));

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task OccurrencesAndUseCountsEqualAnIndependentLcmWalk()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var all = Slots(cache).ToArray();
        Assert.NotEmpty(all);
        var analWs = cache.ServiceLocator.WritingSystemManager.Get(cache.DefaultAnalWs).Id;
        var byText = all.GroupBy(s => s.Text).ToArray();
        long total = 0;
        foreach (var group in byText)
        {
            var scope = new HandAnalysisBareTextPopulation(group.Key);
            var rows = await Pages.All(c => pin.Occurrences(new OccurrenceQuery.Range(scope, null, null, OccurrenceDirection.Forward, []), c)
                .AsPage(), 200);
            var expected = group.ToArray();
            Assert.Equal(expected.Length, rows.Count);
            Assert.Equal(Enumerable.Range((int)total, expected.Length).Select(i => (long)i), rows.Select(r => r.Position));
            total += expected.Length;
            foreach (var (slot, row) in expected.Zip(rows))
            {
                var anchor = Assert.IsType<HandAnalysisTextAnchor>(row.Anchor);
                Assert.Equal((slot.Text, slot.Paragraph, slot.Segment, slot.Index), (anchor.TextId, anchor.ParagraphId, anchor.SegmentId, anchor.Index));
                var (kind, state) = slot.Analysis switch
                {
                    IWfiWordform => (AttachmentKind.Wordform, OccurrenceState.Bare),
                    IWfiAnalysis => (AttachmentKind.Analysis, OccurrenceState.Analysis),
                    IWfiGloss => (AttachmentKind.Gloss, OccurrenceState.Gloss),
                    _ => (AttachmentKind.Punctuation, OccurrenceState.Punctuation)
                };
                Assert.Equal((kind, slot.Analysis.Guid, state), (row.Attachment!.Kind, row.Attachment.Id, row.State));
                Assert.Equal(slot.Index == 0, row.SegmentStart);
            }
            // Missing-slot predicate vs the walk: no word gloss unless attached to a gloss with text.
            var missing = await Pages.All(c => pin.Occurrences(new OccurrenceQuery.Range(scope, null, null, OccurrenceDirection.Forward,
                [new("word-gloss", analWs)]), c).AsPage(), 200);
            var expectedMissing = expected.Where(s => s.Analysis is not IPunctuationForm &&
                !(s.Analysis is IWfiGloss g && !string.IsNullOrEmpty(g.Form.get_String(cache.DefaultAnalWs).Text))).Select(s => (s.Segment, s.Index));
            Assert.Equal(expectedMissing, missing.Select(r => ((HandAnalysisTextAnchor)r.Anchor) is var a ? (a.SegmentId, a.Index) : default));
        }
        Assert.Equal(all.Length, pin.Hand.Occurrences.Slots.Length);
        // Use counts: slots attached to the analysis or one of its glosses.
        var uses = all.Where(s => s.Analysis is IWfiAnalysis or IWfiGloss).ToLookup(s => s.Analysis is IWfiGloss g ? ((IWfiAnalysis)g.Owner).Guid : s.Analysis.Guid);
        foreach (var analysis in cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances())
        {
            var reading = Assert.IsType<WordFact.Reading>(Assert.Single((await pin.Words(TestWord.Reading(analysis.Guid), new())).Items));
            Assert.Equal(uses[analysis.Guid].LongCount(), reading.OccurrenceUseCount);
            var found = (await Pages.All(c => pin.Occurrences(new OccurrenceQuery.Uses([new(ReferenceKind.Analysis, analysis.Guid)]), c).AsPage(), 200)).Count;
            Assert.Equal(uses[analysis.Guid].Count(), found);
        }
        foreach (var word in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().Where(w => w.AnalysesOC.Count > 1).Take(30))
        {
            var ws = cache.ServiceLocator.WritingSystemManager.Get(word.Form.AvailableWritingSystemIds[0]).Id;
            var key = TestWord.Key(ws, word.Form.get_String(word.Form.AvailableWritingSystemIds[0]).Text);
            var ranked = (await pin.Words(new WordQuery.Alternatives(key, WordOrder.DefaultRank), new())).Items.Cast<WordFact.Reading>().ToArray();
            Assert.Equal(ranked.Select(r => r.OccurrenceUseCount!.Value).OrderByDescending(c => c), ranked.Select(r => r.OccurrenceUseCount!.Value));
        }
        var members = await pin.Occurrences(new OccurrenceQuery.Members(new OccurrenceScope.Corpus(new(pin.Stamp.ProjectId)), [], []), new(200));
        Assert.Equal(all.LongCount(s => s.Analysis is not IPunctuationForm), members.Rows.Total);
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task ProjectFactsEqualAnIndependentLcmRead()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var facts = Assert.IsType<ProjectRead.Available>(pin.Project).Facts;
        var types = cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.OfType<IMoMorphType>().ToArray();
        Assert.Equal(types.Select(t => (t.Guid, t.Prefix ?? "", t.Postfix ?? "")), facts.MorphTypes.Select(t => (t.Id, t.Markers.Before, t.Markers.After)));
        Assert.Equal(Enumerable.Range(0, types.Length), facts.MorphTypes.Select(t => t.Position));
        Assert.All(types, t => Assert.Equal(t.IsAffixType, facts.MorphTypes.Single(f => f.Id == t.Guid).Family == MorphFamily.Affix));
        Assert.DoesNotContain(facts.MorphTypes, t => t.SemanticRole == "custom");
        var tags = cache.ServiceLocator.WritingSystems.AllWritingSystems.Select(w => w.Id).Order(StringComparer.Ordinal).ToArray();
        // The store carries every definition in the project's WritingSystemStore, not only those the cache loaded.
        Assert.Empty(tags.Except(facts.WritingSystems.Select(w => w.Tag)));
        Assert.Equal(cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems.Select(w => w.Id),
            facts.WritingSystems.Where(w => w.VernacularOrder is not null).OrderBy(w => w.VernacularOrder).Select(w => w.Tag));
        Assert.Equal(cache.ServiceLocator.WritingSystems.CurrentAnalysisWritingSystems.Select(w => w.Id),
            facts.WritingSystems.Where(w => w.AnalysisOrder is not null).OrderBy(w => w.AnalysisOrder).Select(w => w.Tag));
        Assert.All(facts.WritingSystems, w =>
        {
            Assert.Equal(cache.ServiceLocator.WritingSystemManager.Get(w.Tag).RightToLeftScript, w.RightToLeft);
            Assert.False(w.LdmlBytes!.Value.IsEmpty);
            Assert.Equal(pin.Stamp.ComparisonVersion, w.ComparisonVersion);
        });
        var categories = new List<(Guid, Guid?, int, string)>();
        void Walk(IEnumerable<ICmPossibility> list, Guid? parent)
        {
            var order = 0;
            foreach (var p in list)
            {
                categories.Add((p.Guid, parent, order++, string.Join("|", Sorted(Alt(cache, p.Name)).Select(x => x.Key + "=" + x.Value))));
                Walk(p.SubPossibilitiesOS, p.Guid);
            }
        }
        Walk(cache.LangProject.PartsOfSpeechOA.PossibilitiesOS, null);
        Assert.Equal(categories, facts.Categories.Select(c => (c.Id, c.ParentId, c.Order,
            string.Join("|", Sorted(c.Names).Select(x => x.Key + "=" + x.Value)))));
    }

    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task MorphTypeRolesAndFamiliesMatchTheLcmWellKnownTypes()
    {
        var cache = project.Cache!;
        var (host, pin) = await PinAsync();
        await using var _ = host;
        var expected = new Dictionary<Guid, string>
        {
            [MoMorphTypeTags.kguidMorphBoundRoot] = "bound-root", [MoMorphTypeTags.kguidMorphBoundStem] = "bound-stem",
            [MoMorphTypeTags.kguidMorphCircumfix] = "circumfix", [MoMorphTypeTags.kguidMorphClitic] = "clitic",
            [MoMorphTypeTags.kguidMorphEnclitic] = "enclitic", [MoMorphTypeTags.kguidMorphInfix] = "infix",
            [MoMorphTypeTags.kguidMorphParticle] = "particle", [MoMorphTypeTags.kguidMorphPhrase] = "phrase",
            [MoMorphTypeTags.kguidMorphPrefix] = "prefix", [MoMorphTypeTags.kguidMorphProclitic] = "proclitic",
            [MoMorphTypeTags.kguidMorphRoot] = "root", [MoMorphTypeTags.kguidMorphSimulfix] = "simulfix",
            [MoMorphTypeTags.kguidMorphStem] = "stem", [MoMorphTypeTags.kguidMorphSuffix] = "suffix",
            [MoMorphTypeTags.kguidMorphSuprafix] = "suprafix", [MoMorphTypeTags.kguidMorphDiscontiguousPhrase] = "discontiguous-phrase",
            [MoMorphTypeTags.kguidMorphPrefixingInterfix] = "prefixing-interfix", [MoMorphTypeTags.kguidMorphInfixingInterfix] = "infixing-interfix",
            [MoMorphTypeTags.kguidMorphSuffixingInterfix] = "suffixing-interfix"
        };
        var facts = Assert.IsType<ProjectRead.Available>(pin.Project).Facts;
        foreach (var (id, role) in expected)
            Assert.Equal(role, facts.MorphTypes.Single(t => t.Id == id).SemanticRole);
    }
}

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisCebuanoLexicalTimingTests(ImportedCebuano project, ITestOutputHelper output) : IClassFixture<ImportedCebuano>
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_CEBUANO")]
    public async Task FormsLookupAndEntryClosureMeetOneHundredMillisecondsWarmAtP95()
    {
        var cache = project.Cache!;
        var forms = cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances()
            .Where(f => f.MorphTypeRA is not null && f.Form.AvailableWritingSystemIds.Length > 0).Take(40000).ToArray();
        var random = new Random(23);
        var sample = forms.OrderBy(_ => random.Next()).Take(400).Select(f =>
        {
            var h = f.Form.AvailableWritingSystemIds.First(h => !string.IsNullOrEmpty(f.Form.get_String(h).Text));
            return (Ws: cache.ServiceLocator.WritingSystemManager.Get(h).Id, Text: f.Form.get_String(h).Text, Type: f.MorphTypeRA!,
                Entry: ((f.Owner as ILexEntry) ?? (ILexEntry)f.Owner).Guid);
        }).ToArray();
        var entryCount = cache.ServiceLocator.GetInstance<ILexEntryRepository>().Count;
        project.ReleaseCache();
        var log = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_LOG");
        void Say(string line)
        {
            line = "HAND Cebuano lexical " + line;
            output.WriteLine(line);
            if (log is not null) File.AppendAllText($"{log}.{Environment.ProcessId}", line + Environment.NewLine);
        }
        await using var host = await HandAnalysisHost.OpenAsync(project.Store, null, default, IcuFormCollation.Factory);
        var clock = Stopwatch.StartNew();
        await using var pin = await host.PinAsync(new ExternalBasis("s", "p", "t"), null);
        Say($"first pin with the lexical, occurrence and project projection={clock.Elapsed.TotalMilliseconds:F0} ms; entries={entryCount}; forms={pin.Hand.Lexicon.Forms.Length}; comparer={pin.Stamp.ComparisonVersion}");
        var load = File.ReadAllText("/proc/loadavg").Split(' ').Take(3);
        Say($"machine load (1/5/15 min) before timing={string.Join(' ', load)}; cores={Environment.ProcessorCount}");
        double[] Time(int n, Func<int, Task> run)
        {
            var times = new double[n];
            for (var i = 0; i < n; i++)
            {
                var t = Stopwatch.GetTimestamp();
                run(i).GetAwaiter().GetResult();
                times[i] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            }
            Array.Sort(times);
            return times;
        }
        // Warm: one untimed pass so JIT and first-use ICU collators are paid before the measured pass.
        foreach (var s in sample.Take(20)) await pin.Forms(LexiconPages.Equal(s.Ws, s.Text, FormOrder.Usage,
            new FormTypeMarker(s.Type.Guid, new(s.Type.Prefix ?? "", s.Type.Postfix ?? ""))));
        var formTimes = Time(sample.Length, async i =>
        {
            var s = sample[i];
            var page = await pin.Forms(LexiconPages.Equal(s.Ws, s.Text, FormOrder.Usage,
                new FormTypeMarker(s.Type.Guid, new(s.Type.Prefix ?? "", s.Type.Postfix ?? ""))), new(200));
            Assert.Contains(s.Entry, page.Items.Select(h => h.EntryId));
        });
        var entryTimes = Time(sample.Length, async i =>
        {
            var page = await pin.Lexicon(new LexiconQuery.ByIds([sample[i].Entry], [], [], []));
            Assert.Single(page.Items);
        });
        double P(double[] t, double q) => t[Math.Min(t.Length - 1, (int)(t.Length * q))];
        Say($"warm forms lookup over {formTimes.Length}: p50={P(formTimes, .5):F3} ms p95={P(formTimes, .95):F3} ms max={formTimes[^1]:F3} ms");
        Say($"warm entry closure over {entryTimes.Length}: p50={P(entryTimes, .5):F3} ms p95={P(entryTimes, .95):F3} ms max={entryTimes[^1]:F3} ms");
        Say($"machine load after timing={string.Join(' ', File.ReadAllText("/proc/loadavg").Split(' ').Take(3))}");
        Assert.True(P(formTimes, .95) < 100, $"forms lookup p95 {P(formTimes, .95):F1} ms");
        Assert.True(P(entryTimes, .95) < 100, $"entry closure p95 {P(entryTimes, .95):F1} ms");
    }
}
