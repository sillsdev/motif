using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class UnderlyingRealizationParserTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task RepresentedUnderlyingDistinctionsSurviveUntilOrderedWrittenRealization()
    {
        using var cache = pristine.NewScratch();
        var inventory = new Inventory(cache);
        var category = AddCategory(cache, "Word");
        var plain = AddStem(cache, "pata", category);
        var hidden = AddStem(cache, "pataʔ", category);
        var longer = AddStem(cache, "pāta", category);
        var accent = AddStem(cache, "pǎta", category);
        var postConsonant = AddStem(cache, "patʔa", category);
        var original = new[] { plain, hidden, longer, accent, postConsonant };
        RealParserProject.PrepareForParsing(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding>" +
                "<NotOnClitics>true</NotOnClitics><DelReapps>0</DelReapps><Strata /></HC></ParserParameters>");
        var rules = inventory.Realization();
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(new[] { plain.Form.ToGuid(), hidden.Form.ToGuid(), longer.Form.ToGuid(), accent.Form.ToGuid() }.Order(),
            (await Readings("pata")).Order());
        Assert.Equal([postConsonant.Form.ToGuid()], await Readings("pat-a"));
        foreach (var word in new[] { "pataʔ", "pāta", "pǎta", "patʔa" }) Assert.Empty(await Readings(word));

        SetRuleDisabled(rules.Quantity, true);
        Assert.DoesNotContain(longer.Form.ToGuid(), await Readings("pata"));
        Assert.Equal([longer.Form.ToGuid()], await Readings("pāta"));
        SetRuleDisabled(rules.Quantity, false);
        SetRuleDisabled(rules.Accent, true);
        Assert.DoesNotContain(accent.Form.ToGuid(), await Readings("pata"));
        Assert.Equal([accent.Form.ToGuid()], await Readings("pǎta"));
        SetRuleDisabled(rules.Accent, false);
        SetRuleDisabled(rules.Erase, true);
        Assert.DoesNotContain(hidden.Form.ToGuid(), await Readings("pata"));
        SetRuleDisabled(rules.Erase, false);
        SoundSystemComposerTests.Execute(cache, OrderPhonologicalRulesComposer.Build(cache,
            new(Rule: rules.Erase, Before: rules.PostConsonant)));
        new FwDataProjectLoader().Save(cache);
        Assert.Empty(await Readings("pat-a"));
        Assert.Contains(postConsonant.Form.ToGuid(), await Readings("pata"));

        SoundSystemComposerTests.Execute(cache, OrderPhonologicalRulesComposer.Build(cache,
            new(Rule: rules.PostConsonant, Before: rules.Erase)));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IMoStemAllomorph>(cache, hidden.Form).IsAbstract = true);
        new FwDataProjectLoader().Save(cache);
        Assert.DoesNotContain(hidden.Form.ToGuid(), await Readings("pata"));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            Get<IMoStemAllomorph>(cache, hidden.Form).IsAbstract = false;
            Get<IPhPhoneme>(cache, inventory.Phones["ʔ"]).CodesOS[0].Representation.set_String(cache.DefaultVernWs, "");
        });
        new FwDataProjectLoader().Save(cache);
        foreach (var word in new[] { "pata", "pat-a" })
        {
            var refused = Assert.IsType<PanGlossTraceOutcome.Declined>(await tracer.TraceAsync(cache.ProjectId.Path,
                word, CancellationToken.None, Wait.Scaled(TimeSpan.FromMinutes(2))));
            Assert.NotNull(refused.ParserRefusal);
            Assert.NotEqual(0, refused.ParserRefusal.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(refused.ParserRefusal.StandardError));
            output.WriteLine($"empty grapheme, {word}: {refused.ParserRefusal.StandardError}");
        }
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IPhPhoneme>(cache, inventory.Phones["ʔ"]).CodesOS[0].Representation.set_String(cache.DefaultVernWs, "ʔ"));
        new FwDataProjectLoader().Save(cache);
        Assert.Contains(hidden.Form.ToGuid(), await Readings("pata"));
        Assert.Equal([postConsonant.Form.ToGuid()], await Readings("pat-a"));

        async Task<Guid[]> Readings(string word)
        {
            var trace = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(cache.ProjectId.Path,
                word, CancellationToken.None, Wait.Scaled(TimeSpan.FromMinutes(2))));
            Assert.True(trace.Details!.SearchCompleted, word);
            Assert.False(trace.Details.Capped || trace.Details.TimedOut || trace.Details.InvalidShape, word);
            var forms = new List<Guid>();
            foreach (var analysis in trace.Document!.Analyses)
            {
                var morph = Assert.Single(analysis.Morphs);
                var form = Guid.Parse(morph.FormId!);
                var expected = Assert.Single(original, item => item.Form.ToGuid() == form);
                Assert.Equal(expected.Msa.ToGuid(), Guid.Parse(morph.MsaId!));
                Assert.Equal(expected.Entry.ToGuid(), Guid.Parse(morph.EntryId!));
                forms.Add(form);
            }
            output.WriteLine($"{word}: completed uncapped search, form IDs {string.Join(", ", forms)}");
            return forms.Distinct().ToArray();
        }

        void SetRuleDisabled(CanonicalId id, bool disabled)
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => Get<IPhRegularRule>(cache, id).Disabled = disabled);
            new FwDataProjectLoader().Save(cache);
        }
    }

    private sealed class Inventory
    {
        private readonly LcmCache _cache;
        private readonly CanonicalId _glottal;
        private readonly CanonicalId _consonants;
        private readonly CanonicalId _vowels;
        private readonly CanonicalId _long;
        private readonly CanonicalId _accented;
        private readonly PhonologicalFeatureValue _short;
        private readonly PhonologicalFeatureValue _unaccented;
        internal Dictionary<string, CanonicalId> Phones { get; } = new();

        internal Inventory(LcmCache cache)
        {
            _cache = cache;
            var basis = Feature("base", "p", "t", "a", "stop", "hyphen");
            var quantity = Feature("quantity", "short", "long");
            var accent = Feature("accent", "default", "exceptional");
            var kind = Feature("kind", "consonant", "vowel", "other");
            _short = new(quantity.Feature, quantity.Values[0]);
            _unaccented = new(accent.Feature, accent.Values[0]);
            foreach (var (symbol, b, q, a, k) in new[] { ("p", 0, 0, 0, 0), ("t", 1, 0, 0, 0),
                         ("a", 2, 0, 0, 1), ("ā", 2, 1, 0, 1), ("ǎ", 2, 0, 1, 1),
                         ("ʔ", 3, 0, 0, 2), ("-", 4, 0, 0, 2) })
            {
                var ops = AuthorPhonemeComposer.Build(cache, new(symbol, [symbol],
                    [new(basis.Feature, basis.Values[b]), new(quantity.Feature, quantity.Values[q]),
                        new(accent.Feature, accent.Values[a]), new(kind.Feature, kind.Values[k])]));
                SoundSystemComposerTests.Execute(cache, ops);
                Phones[symbol] = ops[0].EntityId!.Value;
            }
            _glottal = Class("glottal", "G", new(basis.Feature, basis.Values[3]));
            _consonants = Class("consonants", "C", new(kind.Feature, kind.Values[0]));
            _vowels = Class("vowels", "V", new(kind.Feature, kind.Values[1]));
            _long = Class("long", "L", new(quantity.Feature, quantity.Values[1]));
            _accented = Class("accented", "A", new(accent.Feature, accent.Values[1]));
        }

        internal (CanonicalId PostConsonant, CanonicalId Erase, CanonicalId Quantity, CanonicalId Accent) Realization()
        {
            var post = Rule("write stop after consonant", [new(NaturalClass: _glottal)], [new(Phoneme: Phones["-"])],
                [new(NaturalClass: _consonants)], [new(NaturalClass: _vowels)]);
            var erase = Rule("erase residual stop", [new(NaturalClass: _glottal)], [], [], []);
            var quantity = Rule("neutralize quantity", [new(NaturalClass: _long)], [new(Change: [_short])], [], []);
            var accent = Rule("neutralize accent", [new(NaturalClass: _accented)], [new(Change: [_unaccented])], [], []);
            return (post, erase, quantity, accent);
        }

        private CanonicalId Rule(string name, PhonologicalRuleItem[] input, PhonologicalRuleItem[] output,
            EnvironmentContext[] left, EnvironmentContext[] right)
        {
            var ops = AuthorPhonologicalRuleComposer.Build(_cache,
                new(name, PhonologicalRuleDirection.Simultaneous, input, output, left, right,
                    _cache.LangProject.PhonologicalDataOA.PhonRulesOS.LastOrDefault() is { } last
                        ? new Placement(Id(last), null) : null));
            SoundSystemComposerTests.Execute(_cache, ops);
            return ops[0].EntityId!.Value;
        }

        private (CanonicalId Feature, CanonicalId[] Values) Feature(string name, params string[] values)
        {
            var ops = AuthorPhonologicalFeatureComposer.Build(_cache,
                new(name, values.Select(value => new PhonologicalFeatureOption(value)).ToArray()));
            SoundSystemComposerTests.Execute(_cache, ops);
            return (ops[0].EntityId!.Value, ops.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
                .Select(op => op.EntityId!.Value).ToArray());
        }

        private CanonicalId Class(string name, string abbreviation, PhonologicalFeatureValue feature)
        {
            var ops = AuthorNaturalClassComposer.Build(_cache, new(name, abbreviation, Features: [feature]));
            SoundSystemComposerTests.Execute(_cache, ops);
            return ops[0].EntityId!.Value;
        }
    }
}
