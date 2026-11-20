using System.Collections.Immutable;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

internal static class LexiconPages
{
    public static async ValueTask<Page<FormHit>> Forms(this IHandAnalysisSnapshot s, FormQuery q, PageRequest? p = null) =>
        (await s.FormsAsync(q, p ?? new(), default)).Value();

    public static async ValueTask<Page<LexicalFact>> Lexicon(this IHandAnalysisSnapshot s, LexiconQuery q, PageRequest? p = null) =>
        (await s.LexiconAsync(q, p ?? new(), default)).Value();

    public static async ValueTask<OccurrencePage> Occurrences(this IHandAnalysisSnapshot s, OccurrenceQuery q, PageRequest? p = null) =>
        (await s.OccurrencesAsync(q, p ?? new(), default)).Value();

    public static FormQuery Equal(string ws, string text, FormOrder order, params FormTypeMarker[] alternatives) =>
        new(ws, text.Normalize(System.Text.NormalizationForm.FormD), [.. alternatives], FormMatch.Equal, order);
}

/// <summary>A comparer whose version the test chooses; keys are NFD text so lookups behave as before.</summary>
internal sealed class FixedVersionCollation(string version) : IFormCollation
{
    public string Version { get; } = version;
    public string Key(string ws, string nfdText) => nfdText;
}

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisLexiconSeededTests : IDisposable
{
    private static readonly ExternalBasis Basis = new("source", "parser", "settings");
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-hand-lexicon-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "records.sqlite");
    private const int Fillers = 40;
    private const string Soft = "­";

    private sealed record Rich(SeededProject Project, SeededText Text, Guid Rich1, Guid Variant, Guid SenseLess, Guid Prefix, Guid Process,
        Guid SubSense, Guid InflMsa, Guid DerivMsa, Guid Ref,
        Guid[] MsaOrder, Guid[] DuplicateOrder);

    private async Task<Rich> Import()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var seed = SeededProject.Seed(cache);
        var text = SeededProject.SeedText(cache, seed);
        Rich rich = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var s = cache.ServiceLocator;
            var vern = cache.DefaultVernWs; var anal = cache.DefaultAnalWs;
            var types = s.GetInstance<IMoMorphTypeRepository>();
            var first = s.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
            var pos = s.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);
            var infl = s.GetInstance<IMoInflAffMsaFactory>().Create();
            first.MorphoSyntaxAnalysesOC.Add(infl); infl.PartOfSpeechRA = pos;
            var deriv = s.GetInstance<IMoDerivAffMsaFactory>().Create();
            first.MorphoSyntaxAnalysesOC.Add(deriv); deriv.FromPartOfSpeechRA = pos; deriv.ToPartOfSpeechRA = pos;
            var sub = s.GetInstance<ILexSenseFactory>().Create();
            first.SensesOS[0].SensesOS.Add(sub); sub.Gloss.set_String(anal, "sub gloss");

            var rich1 = s.GetInstance<ILexEntryFactory>().Create();
            var lexeme = s.GetInstance<IMoStemAllomorphFactory>().Create();
            rich1.LexemeFormOA = lexeme; lexeme.MorphTypeRA = types.GetObject(MoMorphTypeTags.kguidMorphStem);
            lexeme.Form.set_String(vern, "richstem");
            var prefix = s.GetInstance<IMoAffixAllomorphFactory>().Create();
            rich1.AlternateFormsOS.Add(prefix); prefix.MorphTypeRA = types.GetObject(MoMorphTypeTags.kguidMorphPrefix);
            prefix.Form.set_String(vern, "motifa");
            var process = s.GetInstance<IMoAffixProcessFactory>().Create();
            rich1.AlternateFormsOS.Add(process); process.MorphTypeRA = types.GetObject(MoMorphTypeTags.kguidMorphSuffix);
            process.Form.set_String(vern, "proc");
            var variant = s.GetInstance<ILexEntryFactory>().Create();
            var variantForm = s.GetInstance<IMoStemAllomorphFactory>().Create();
            variant.LexemeFormOA = variantForm; variantForm.MorphTypeRA = types.GetObject(MoMorphTypeTags.kguidMorphStem);
            variantForm.Form.set_String(vern, "motifvariant");
            var reference = s.GetInstance<ILexEntryRefFactory>().Create();
            variant.EntryRefsOS.Add(reference); reference.RefType = LexEntryRefTags.krtVariant;
            reference.ComponentLexemesRS.Add(first); reference.VariantEntryTypesRS.Add(
                s.GetInstance<ILexEntryTypeRepository>().AllInstances().OfType<ILexEntryType>().First());
            var senseless = s.GetInstance<ILexEntryFactory>().Create();
            var senselessForm = s.GetInstance<IMoStemAllomorphFactory>().Create();
            senseless.LexemeFormOA = senselessForm; senselessForm.MorphTypeRA = types.GetObject(MoMorphTypeTags.kguidMorphStem);
            senselessForm.Form.set_String(vern, "senseless");
            for (var i = 0; i < Fillers; i++)
            {
                var filler = s.GetInstance<ILexEntryFactory>().Create();
                var form = s.GetInstance<IMoStemAllomorphFactory>().Create();
                filler.LexemeFormOA = form; form.MorphTypeRA = types.GetObject(MoMorphTypeTags.kguidMorphStem);
                form.Form.set_String(vern, "filler" + i);
                var sense = s.GetInstance<ILexSenseFactory>().Create();
                filler.SensesOS.Add(sense); sense.Gloss.set_String(anal, "filler gloss " + i);
            }
            for (var i = 0; i < 3; i++)
            {
                var duplicate = s.GetInstance<ILexEntryFactory>().Create();
                var duplicateForm = s.GetInstance<IMoStemAllomorphFactory>().Create();
                duplicate.LexemeFormOA = duplicateForm; duplicateForm.MorphTypeRA = types.GetObject(MoMorphTypeTags.kguidMorphStem);
                duplicateForm.Form.set_String(vern, "dup");
            }
            var bulk = s.GetInstance<ITextFactory>().Create();
            bulk.Name.set_String(anal, "bulk");
            var body = s.GetInstance<IStTextFactory>().Create();
            bulk.ContentsOA = body;
            for (var i = 0; i < Fillers; i++)
            {
                var para = s.GetInstance<IStTxtParaFactory>().Create();
                body.ParagraphsOS.Add(para);
                para.Contents = SIL.LCModel.Core.Text.TsStringUtils.MakeString("bulk" + i, vern);
                var segment = para.SegmentsOS.Count > 0 ? para.SegmentsOS[0] : null;
                if (segment is null) { segment = s.GetInstance<ISegmentFactory>().Create(); para.SegmentsOS.Add(segment); }
                segment.AnalysesRS.Add(s.GetInstance<IWfiWordformFactory>().Create(SIL.LCModel.Core.Text.TsStringUtils.MakeString("bulk" + i, vern)));
            }
            rich = new(seed, text, rich1.Guid, variant.Guid, senseless.Guid, prefix.Guid, process.Guid, sub.Guid, infl.Guid, deriv.Guid,
                reference.Guid, first.MorphoSyntaxAnalysesOC.Select(m => m.Guid).ToArray(),
                s.GetInstance<IMoFormRepository>().AllInstances().Where(f => f.Form.get_String(vern).Text == "dup").Select(f => f.Guid).ToArray());
        });
        new FwDataProjectLoader().Save(cache);
        var vernWs = cache.DefaultVernWs;
        rich = rich with
        {
            MsaOrder = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId)
                .MorphoSyntaxAnalysesOC.Select(m => m.Guid).ToArray(),
            DuplicateOrder = cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances()
                .Where(f => f.Form.get_String(vernWs).Text == "dup").Select(f => f.Guid).ToArray()
        };
        await LcmRecordBridge.ImportAsync(cache, cache.ProjectId.Path, Database);
        return rich;
    }

    private async Task<(HandAnalysisHost Host, StoreSnapshot Pin)> Pin(FormCollationFactory? collation = null)
    {
        var host = await HandAnalysisHost.OpenAsync(Database, null, default, collation ?? IcuFormCollation.Factory);
        return (host, await host.PinAsync(Basis, null));
    }

    private static string Vern(StoreSnapshot pin) => pin.Hand.Project.WritingSystems.First(w => w.VernacularOrder == 0).Tag;

    private static FormTypeMarker Stem(StoreSnapshot pin) => Type(pin, "stem");

    private static FormTypeMarker Type(StoreSnapshot pin, string role)
    {
        var type = pin.Hand.Project.MorphTypes.Single(t => t.SemanticRole == role);
        return new(type.Id, type.Markers);
    }

    [Fact]
    public async Task FormsFindCollationEquivalentAllomorphsByTypeAndMarkerPair()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var ws = Vern(pin);
        var stem = Stem(pin);
        var exact = await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter, stem));
        Assert.Equal(rich.Project.FirstLexemeFormId, Assert.Single(exact.Items).Id);
        var hit = exact.Items[0];
        Assert.Equal((rich.Project.FirstEntryId, true, 0, "MoStemAllomorph", stem.TypeId, false),
            (hit.EntryId, hit.IsLexeme, hit.Position, hit.NativeClass, hit.MorphTypeId, hit.IsAbstract));
        Assert.Equal(exact.Items[0].ComparisonKeys[ws], exact.Items[0].SortKey);
        // An ignorable character is collation-equivalent but a different NFD string: that is what the key is for.
        var equivalent = await pin.Forms(LexiconPages.Equal(ws, "moti" + Soft + "fa", FormOrder.Encounter, stem));
        Assert.Equal(rich.Project.FirstLexemeFormId, Assert.Single(equivalent.Items).Id);
        // The same text under the prefix type is a different allomorph of another entry.
        var prefix = Type(pin, "prefix");
        var prefixed = await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter, prefix));
        var prefixHit = Assert.Single(prefixed.Items);
        Assert.Equal((rich.Prefix, rich.Rich1, false, 1), (prefixHit.Id, prefixHit.EntryId, prefixHit.IsLexeme, prefixHit.Position));
        var both = await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter, stem, prefix));
        Assert.Equal(new[] { rich.Project.FirstLexemeFormId, rich.Prefix }.Order(), both.Items.Select(h => h.Id).Order());
        // Markers are ordinal and part of the match; no alternatives and an empty entry list match nothing.
        Assert.Empty((await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter,
            new FormTypeMarker(stem.TypeId, new("x", "y"))))).Items);
        Assert.Empty((await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter))).Items);
        Assert.Empty((await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter, stem) with { EntryIds = [] })).Items);
        Assert.Equal(rich.Rich1, Assert.Single((await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter, stem, prefix)
            with { EntryIds = [rich.Rich1] })).Items).EntryId);
        // Identity stays exact: the wordform read does not collapse the equivalent spelling.
        Assert.Empty((await pin.Words(new WordQuery.Identity(TestWord.Key(ws, "moti" + Soft + "fanalysed")), new())).Items);
    }

    [Fact]
    public async Task PatternModeReturnsPatternBearingFormsUnderTheirDiscriminator()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var suffix = Type(pin, "suffix");
        var page = await pin.Forms(new(Vern(pin), "ignored", [suffix], FormMatch.Pattern, FormOrder.Encounter));
        var hit = Assert.Single(page.Items);
        Assert.Equal(rich.Process, hit.Id);
        Assert.Equal("MoAffixProcess", hit.NativeClass);
        Assert.Equal($"{suffix.TypeId:N}|{suffix.Markers.Before}|{suffix.Markers.After}", hit.Pattern!.Discriminator);
        Assert.Empty((await pin.Forms(new(Vern(pin), "x", [Stem(pin)], FormMatch.Pattern, FormOrder.Encounter))).Items);
    }

    [Fact]
    public async Task FormOrdersAreEncounterUsageAndChooser()
    {
        await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var ws = Vern(pin);
        var stem = Stem(pin);
        var encounter = await pin.Forms(LexiconPages.Equal(ws, "filler1", FormOrder.Encounter, stem));
        Assert.Single(encounter.Items);
        var inUse = await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Usage, stem, Type(pin, "prefix")));
        Assert.Equal(2, inUse.Items.Length);
        Assert.Equal(inUse.Items.OrderByDescending(h => h.DistinctAnalysisUseCount).Select(h => h.Id), inUse.Items.Select(h => h.Id));
        Assert.Equal(1, inUse.Items[0].DistinctAnalysisUseCount);
        Assert.Equal(0, inUse.Items[1].DistinctAnalysisUseCount);
        var chooser = await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Chooser, stem, Type(pin, "prefix")));
        Assert.Equal(chooser.Items.Select(h => h.SortKey).Order(StringComparer.Ordinal), chooser.Items.Select(h => h.SortKey));
        var enc = (await pin.Forms(LexiconPages.Equal(ws, SeededProject.FirstForm, FormOrder.Encounter, stem, Type(pin, "prefix")))).Items;
        Assert.Equal(enc.Select(h => h.EncounterKey).Order(StringComparer.Ordinal), enc.Select(h => h.EncounterKey));
    }

    [Fact]
    public async Task LexiconReturnsEntryClosuresWithOrderedSensesFormsMsasAndReferences()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var page = await pin.Lexicon(new LexiconQuery.ByIds([rich.Project.FirstEntryId, rich.SenseLess, rich.Variant], [], [], []));
        Assert.Equal(3, page.Items.Length);
        var first = page.Items.Single(f => f.EntryId == rich.Project.FirstEntryId);
        Assert.Equal([rich.Project.FirstSenseId, rich.SubSense], first.Senses.Select(s => s.Id));
        Assert.Equal(["1", "1.1"], first.Senses.Select(s => s.Number));
        Assert.Equal((rich.Project.FirstSenseId, "sub gloss"), (first.Senses[1].ParentId!.Value, first.Senses[1].Gloss.Values.Single()));
        Assert.Equal(rich.Project.FirstLexemeFormId, first.LexemeFormId);
        Assert.Contains(first.Msas, m => m.NativeClass == "MoStemMsa" && m.CategoryId == rich.Project.PartOfSpeechId);
        var infl = Assert.Single(first.Msas, m => m.Id == rich.InflMsa);
        Assert.Equal(("MoInflAffMsa", rich.Project.PartOfSpeechId), (infl.NativeClass, infl.CategoryId));
        var deriv = Assert.Single(first.Msas, m => m.Id == rich.DerivMsa);
        Assert.Equal((rich.Project.PartOfSpeechId, rich.Project.PartOfSpeechId, null), (deriv.FromCategoryId, deriv.ToCategoryId, deriv.CategoryId));
        Assert.Equal(3, first.Msas.Select(m => m.SemanticEqualityKey).Distinct().Count());
        var variant = page.Items.Single(f => f.EntryId == rich.Variant);
        Assert.Empty(variant.Senses);
        var reference = Assert.Single(variant.References);
        Assert.Equal(("variant", variant.EntryId, 0, rich.Project.FirstEntryId), (reference.NativeKind, reference.OwnerId, reference.Order,
            reference.Components.Single().EntryId!.Value));
        Assert.Equal(rich.Ref, reference.Id);
        Assert.Empty(page.Items.Single(f => f.EntryId == rich.SenseLess).Senses);
        Assert.Contains(page.Limitations, l => l.Code == "circumfix-partners-unavailable");
        var rich1 = (await pin.Lexicon(new LexiconQuery.ByIds([], [], [], [rich.Ref]))).Items;
        Assert.Equal(rich.Variant, Assert.Single(rich1).EntryId);
        Assert.Equal([rich.Prefix, rich.Process], (await pin.Lexicon(new LexiconQuery.ByIds([rich.Rich1], [], [], []))).Items.Single().AlternateFormIds.ToArray());
        var found = await pin.Lexicon(new LexiconQuery.Search(LexicalField.Headword, LexicalMatch.Prefix, Vern(pin), "motif"));
        Assert.Contains(found.Items, f => f.EntryId == rich.Project.FirstEntryId);
        Assert.Contains(found.Items, f => f.EntryId == rich.Variant);
        Assert.DoesNotContain(found.Items, f => f.EntryId == rich.SenseLess);
        var byGloss = await pin.Lexicon(new LexiconQuery.Search(LexicalField.Sense, LexicalMatch.Equal,
            pin.Hand.Project.WritingSystems.First(w => w.AnalysisOrder == 0).Tag, SeededProject.FirstGloss));
        Assert.Equal(rich.Project.FirstEntryId, Assert.Single(byGloss.Items).EntryId);
        Assert.Contains(((await pin.Lexicon(new LexiconQuery.ByIds([Guid.NewGuid()], [], [], []))).Limitations), l => l.Code == "id-not-found");
    }

    [Fact]
    public async Task OccurrencesCarryAnchorsAttachmentKindsAndAnIndexedMissingSlotPredicate()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var scope = new HandAnalysisBareTextPopulation(rich.Text.TextId);
        var all = await pin.Occurrences(new OccurrenceQuery.Range(scope, null, null, OccurrenceDirection.Forward, []));
        Assert.Equal([AttachmentKind.Analysis, AttachmentKind.Punctuation, AttachmentKind.Wordform],
            all.Rows.Items.Select(o => o.Attachment!.Kind));
        Assert.Equal([OccurrenceState.Analysis, OccurrenceState.Punctuation, OccurrenceState.Bare], all.Rows.Items.Select(o => o.State));
        Assert.Equal(3, all.Rows.Total);
        Assert.Equal((1, 1, 0, 1), (all.Counts.Bare, all.Counts.Analysis, all.Counts.Gloss, all.Counts.Punctuation) is var (b, a, g, p)
            ? (b, a, g, p) : default);
        Assert.Null(all.Counts.Conflict);
        var anchors = all.Rows.Items.Select(o => Assert.IsType<HandAnalysisTextAnchor>(o.Anchor)).ToArray();
        Assert.Equal([(rich.Text.FirstSegmentId, 0, 0), (rich.Text.FirstSegmentId, 1, 1), (rich.Text.SecondSegmentId, 0, 0)],
            anchors.Select(x => (x.SegmentId, x.Index, x.WordPosition)));
        Assert.Equal(rich.Text.ApprovedAnalysisId, all.Rows.Items[0].ResolvedAnalysis is AnalysisRef.Native n ? n.Id : default);
        Assert.Equal(rich.Text.AnalysedWordformId, all.Rows.Items[0].ResolvedWordformId);
        Assert.Equal((true, false), (all.Rows.Items[0].SegmentStart!.Value, all.Rows.Items[0].SegmentEnd!.Value));
        Assert.Equal((false, true), (all.Rows.Items[1].SegmentStart!.Value, all.Rows.Items[1].SegmentEnd!.Value));
        Assert.Contains(all.Rows.Items[0].Limitations, l => l.Code == "occurrence-binding-unavailable");
        var back = await pin.Occurrences(new OccurrenceQuery.Range(scope, null, null, OccurrenceDirection.Backward, []));
        Assert.Equal(all.Rows.Items.Select(o => o.Position).Reverse(), back.Rows.Items.Select(o => o.Position));
        var paged = await Pages.All(c => pin.Occurrences(new OccurrenceQuery.Range(scope, null, null, OccurrenceDirection.Forward, []),
            c is { } r ? r : new()).AsPage(), 2);
        Assert.Equal(3, paged.Count);
        var from = await pin.Occurrences(new OccurrenceQuery.Range(scope, anchors[1], anchors[2], OccurrenceDirection.Forward, []));
        Assert.Equal(2, from.Rows.Items.Length);
        var ws = pin.Hand.Project.WritingSystems.First(w => w.AnalysisOrder == 0).Tag;
        var missing = await pin.Occurrences(new OccurrenceQuery.Range(scope, null, null, OccurrenceDirection.Forward,
            [new("word-gloss", ws)]));
        Assert.Equal([AttachmentKind.Analysis, AttachmentKind.Wordform], missing.Rows.Items.Select(o => o.Attachment!.Kind));
        Assert.Equal([new LineSlot("lex-category", ""), new LineSlot("lex-entry", Vern(pin))],
            all.Rows.Items[0].FilledSlots.Where(s => s.Kind is "lex-category" or "lex-entry" && (s.Ws == "" || s.Ws == Vern(pin))).OrderBy(s => s.Kind));
        var exact = await pin.Occurrences(new OccurrenceQuery.Exact(new(anchors[2])));
        Assert.Equal(rich.Text.UnanalysedWordformId, Assert.Single(exact.Rows.Items).Attachment!.Id);
        var members = await pin.Occurrences(new OccurrenceQuery.Members(new OccurrenceScope.ChapterOrText(scope),
            [new(Vern(pin), SeededProject.UnanalysedWordForm)], []));
        Assert.Equal(rich.Text.UnanalysedWordformId, Assert.Single(members.Rows.Items).Attachment!.Id);
        var corpus = await pin.Occurrences(new OccurrenceQuery.Members(new OccurrenceScope.Corpus(new(pin.Stamp.ProjectId)), [], []));
        Assert.Equal(2 + Fillers, corpus.Rows.Total);
        Assert.Equal(RefusalCodes.HandAnalysisSourceUnavailable, (await pin.OccurrencesAsync(new OccurrenceQuery.Members(
            new OccurrenceScope.Corpus(new(Guid.NewGuid())), [], []), new(), default)).Refusal?.Code);
        var uses = await pin.Occurrences(new OccurrenceQuery.Uses([new(ReferenceKind.Allomorph, rich.Project.FirstLexemeFormId)]));
        Assert.Equal(rich.Text.ApprovedAnalysisId, Assert.IsType<AnalysisRef.Native>(Assert.Single(uses.Rows.Items).ResolvedAnalysis).Id);
    }

    [Fact]
    public async Task ProjectFactsCarryMorphTypesWritingSystemsAndTheCategoryTree()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var facts = Assert.IsType<ProjectRead.Available>(pin.Project).Facts;
        Assert.Equal(pin.Stamp, facts.Stamp);
        Assert.Contains(facts.Limitations, l => l.Code == "settings-unavailable");
        Assert.Equal(pin.Stamp.ComparisonVersion, Assert.IsType<string>(facts.WritingSystems[0].ComparisonVersion));
        var ar = Assert.Single(facts.WritingSystems, w => w.Tag == SeededProject.RightToLeftTag);
        Assert.True(ar.RightToLeft);
        Assert.Equal("Charis SIL", ar.Font);
        Assert.Contains(SeededProject.MissingFont, ar.Ldml);
        Assert.Contains(SeededProject.FontFeatures, ar.Ldml);
        Assert.Equal(RecordStoreAuxiliary("writing-system", "ar"), ar.Id);
        Assert.False(ar.LdmlBytes!.Value.IsEmpty);
        Assert.Equal(System.Text.Encoding.UTF8.GetString([.. ar.LdmlBytes.Value]).TrimStart('﻿'), ar.Ldml);
        var prefix = Assert.Single(facts.MorphTypes, t => t.SemanticRole == "prefix");
        Assert.Equal((MorphFamily.Affix, "", "-"), (prefix.Family, prefix.Markers.Before, prefix.Markers.After) is var (f, bf, af) ? (f, bf, af) : default);
        Assert.Equal(facts.MorphTypes.Select(t => t.Position), Enumerable.Range(0, facts.MorphTypes.Length));
        Assert.Equal(MorphFamily.Stem, Assert.Single(facts.MorphTypes, t => t.SemanticRole == "stem").Family);
        var category = Assert.Single(facts.Categories, c => c.Id == rich.Project.PartOfSpeechId);
        Assert.Equal("SeededNoun", category.Names.Values.Single());
        Assert.Null(category.ParentId);
    }

    private static Guid RecordStoreAuxiliary(string kind, string id) => Store.AuxiliaryId(kind, id);

    [Fact]
    public async Task UseCountsAndRankAreBoundToThePoint()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var ws = Vern(pin);
        var read = await pin.Words(TestWord.Reading(rich.Text.ApprovedAnalysisId), new());
        var reading = Assert.IsType<WordFact.Reading>(Assert.Single(read.Items));
        Assert.Equal(1, reading.OccurrenceUseCount);
        Assert.Equal($"{long.MaxValue - 1:D19}", reading.DefaultRankKey!.Split(':')[0]);
        var ranked = await pin.Words(new WordQuery.Alternatives(TestWord.Key(ws, SeededProject.AnalysedWordForm), WordOrder.DefaultRank), new());
        Assert.Equal("use-count-native-only", Assert.Single(ranked.Limitations).Code);
        Assert.True(ranked.Complete);
        Assert.Equal(rich.Text.ApprovedAnalysisId, TestWord.AnalysisId(Assert.IsType<WordFact.Reading>(ranked.Items[0])));
    }

    /// <summary>The order properties CI can check without the private Indonesian copy.</summary>
    [Fact]
    public async Task SeededOrdersFollowTheNativeOrdersForClosuresEncounterUsageChooserPatternAndEntryFilter()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var ws = Vern(pin);
        var stem = Stem(pin);
        var closure = Assert.Single((await pin.Lexicon(new LexiconQuery.ByIds([rich.Project.FirstEntryId], [], [], []))).Items);
        Assert.Equal(rich.MsaOrder, closure.Msas.Select(m => m.Id).ToArray());
        Assert.Equal(3, rich.DuplicateOrder.Length);
        var encounter = await pin.Forms(LexiconPages.Equal(ws, "dup", FormOrder.Encounter, stem));
        Assert.Equal(rich.DuplicateOrder, encounter.Items.Select(h => h.Id).ToArray());
        var usage = await pin.Forms(LexiconPages.Equal(ws, "dup", FormOrder.Usage, stem));
        Assert.Equal(encounter.Items.Select((h, i) => (h.Id, h.DistinctAnalysisUseCount, i)).OrderByDescending(x => x.DistinctAnalysisUseCount)
            .ThenBy(x => x.i).Select(x => x.Id), usage.Items.Select(h => h.Id));
        var chooser = await pin.Forms(LexiconPages.Equal(ws, "dup", FormOrder.Chooser, stem));
        Assert.Equal(encounter.Items.Select((h, i) => (h.Id, h.SortKey, i)).OrderBy(x => x.SortKey, StringComparer.Ordinal)
            .ThenBy(x => x.i).Select(x => x.Id), chooser.Items.Select(h => h.Id));
        var suffix = Type(pin, "suffix");
        var pattern = await pin.Forms(new FormQuery(ws, "x", [suffix], FormMatch.Pattern, FormOrder.Encounter));
        Assert.Equal(rich.Process, Assert.Single(pattern.Items).Id);
        Assert.Equal(rich.Process, Assert.Single((await pin.Forms(new FormQuery(ws, "x", [suffix], FormMatch.Pattern, FormOrder.Encounter) with
            { EntryIds = [rich.Rich1] })).Items).Id);
        Assert.Empty((await pin.Forms(new FormQuery(ws, "x", [suffix], FormMatch.Pattern, FormOrder.Encounter) with { EntryIds = [rich.Variant] })).Items);
        Assert.Equal(rich.Rich1, Assert.Single((await pin.Forms(LexiconPages.Equal(ws, "richstem", FormOrder.Encounter, stem) with
            { EntryIds = [rich.Rich1, rich.Variant] })).Items).EntryId);
    }

    [Fact]
    public void CountedRowsCannotBeScannedWithoutBeingCounted()
    {
        var counter = new RowCounter();
        var rows = new CountedRows<int>([.. Enumerable.Range(0, 25)], counter);
        for (var i = 0; i < rows.Length; i++) _ = rows[i];
        Assert.Equal(25, counter.Value);
    }

    [Fact]
    public async Task EveryRequiredLookupPathTouchesOnlyItsResultRows()
    {
        var rich = await Import();
        var (host, pin) = await Pin();
        await using var _ = host;
        var ws = Vern(pin);
        var hand = pin.Hand;
        Assert.True(hand.Lexicon.Forms.Length >= Fillers + 4, "the project must be large enough that a scan is visible");
        var scope = new HandAnalysisBareTextPopulation(rich.Text.TextId);
        var analWs = hand.Project.WritingSystems.First(w => w.AnalysisOrder == 0).Tag;
        var stem = Stem(pin);
        async Task Check(string path, long scanCost, int expectedRows, Func<Task<int>> run)
        {
            var before = pin.RowsTouched;
            var rows = await run();
            var cost = pin.RowsTouched - before;
            Assert.True(rows == expectedRows, $"{path} returned {rows} rows, expected {expectedRows}");
            Assert.True(cost <= 4 * rows + 4 && cost < scanCost, $"{path} touched {cost} rows for {rows} results; a scan costs {scanCost}");
        }
        var forms = hand.Lexicon.Forms.Length;
        await Check("forms: collation key + marker pair", forms, 1,
            async () => (await pin.Forms(LexiconPages.Equal(ws, "filler7", FormOrder.Encounter, stem))).Items.Length);
        await Check("forms: pattern discriminator", forms, 1,
            async () => (await pin.Forms(new(ws, "x", [Type(pin, "suffix")], FormMatch.Pattern, FormOrder.Usage))).Items.Length);
        await Check("lexicon: entry closure", hand.Lexicon.Entries.Length, 1,
            async () => (await pin.Lexicon(new LexiconQuery.ByIds([rich.Rich1], [], [], []))).Items.Length);
        await Check("lexicon: sense to entry", hand.Lexicon.Entries.Length, 1,
            async () => (await pin.Lexicon(new LexiconQuery.ByIds([], [rich.Project.SecondSenseId], [], []))).Items.Length);
        await Check("lexicon: headword search", hand.Lexicon.Entries.Length, 1,
            async () => (await pin.Lexicon(new LexiconQuery.Search(LexicalField.Headword, LexicalMatch.Equal, ws, "filler9"))).Items.Length);
        await Check("lexicon: headword prefix", hand.Lexicon.Entries.Length, Fillers,
            async () => (await pin.Lexicon(new LexiconQuery.Search(LexicalField.Headword, LexicalMatch.Prefix, ws, "filler"), new(200))).Items.Length);
        await Check("lexicon: sense gloss search", hand.Lexicon.Entries.Length, 1,
            async () => (await pin.Lexicon(new LexiconQuery.Search(LexicalField.Sense, LexicalMatch.Equal, analWs, "filler gloss 3"))).Items.Length);
        var slots = hand.Occurrences.Slots.Length;
        Assert.Equal(3 + Fillers, slots);
        await Check("occurrences: text, case key and attachment kind", slots, 1,
            async () => (await pin.Occurrences(new OccurrenceQuery.Members(new OccurrenceScope.ChapterOrText(scope),
                [new(ws, SeededProject.UnanalysedWordForm)], [OccurrenceState.Bare]))).Rows.Items.Length);
        await Check("occurrences: missing-slot predicate", slots, 2,
            async () => (await pin.Occurrences(new OccurrenceQuery.Range(scope, null, null, OccurrenceDirection.Forward,
                [new("word-gloss", analWs)]))).Rows.Items.Length);
        await Check("occurrences: usage of a target", slots, 1,
            async () => (await pin.Occurrences(new OccurrenceQuery.Uses([new(ReferenceKind.Sense, rich.Project.FirstSenseId)]))).Rows.Items.Length);
    }

    [Fact]
    public async Task ADifferentComparerVersionChangesTheStampAndRefusesOldCursors()
    {
        await Import();
        var (hostA, a) = await Pin(_ => new FixedVersionCollation("icu-1/test/a"));
        await using var _a = hostA;
        var (hostB, b) = await Pin(_ => new FixedVersionCollation("icu-2/test/a"));
        await using var _b = hostB;
        Assert.Equal(("icu-1/test/a", "icu-2/test/a"), (a.Stamp.ComparisonVersion, b.Stamp.ComparisonVersion));
        Assert.NotEqual(a.Stamp.SnapshotId, b.Stamp.SnapshotId);
        Assert.Equal(a.Stamp.HomePoint, b.Stamp.HomePoint);
        var query = new LexiconQuery.Search(LexicalField.Headword, LexicalMatch.Prefix, Vern(a), "filler");
        var page = await a.Lexicon(query, new(2));
        Assert.NotNull(page.NextCursor);
        Assert.Equal(2, (await a.Lexicon(query, new(2, page.NextCursor))).Items.Length);
        Assert.Equal(RefusalCodes.HandAnalysisInvalidCursor, (await b.LexiconAsync(query, new(2, page.NextCursor), default)).Refusal?.Code);
        Assert.Equal(RefusalCodes.HandAnalysisSnapshotExpired,
            (await hostB.PinAsync(a.Stamp.ProjectId, a.Stamp.SnapshotId, default)).Refusal?.Code);
        // The real ICU comparer names the library, algorithm and definitions in the Stamp.
        var (hostC, c) = await Pin();
        await using var _c = hostC;
        Assert.StartsWith("icu-", c.Stamp.ComparisonVersion);
        Assert.Equal(c.Stamp.ComparisonVersion, c.Hand.Project.WritingSystems[0].ComparisonVersion);
        var (hostD, d) = await Pin(_ => OrdinalFormCollation.Instance);
        await using var _d = hostD;
        Assert.Equal(HandAnalysisHost.ComparisonVersionNone, d.Stamp.ComparisonVersion);
        Assert.NotEqual(c.Stamp.SnapshotId, d.Stamp.SnapshotId);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

internal static class PageAdapter
{
    public static ValueTask<Page<OccurrenceFact>> AsPage(this ValueTask<OccurrencePage> page) => AwaitRows(page);

    private static async ValueTask<Page<OccurrenceFact>> AwaitRows(ValueTask<OccurrencePage> page) => (await page).Rows;
}
