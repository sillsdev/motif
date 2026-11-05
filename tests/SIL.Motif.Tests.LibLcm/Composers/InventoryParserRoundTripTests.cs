using System.Xml;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class InventoryParserRoundTripTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task ExplicitCaseAndLiteralIdentitiesKeepOnlyTheIntendedDefaultEngineAnalyses()
    {
        using var cache = pristine.NewScratch();
        var representations = new[] { "n", "b", "i", "p", "c", "k", "l", "s", "d", "a", "á", "ng", "(", ")", "/" };
        var definition = AuthorPhonologicalFeatureComposer.Build(cache,
            new("segment identity", representations.Select((_, index) => new PhonologicalFeatureOption($"segment {index}")).ToArray()));
        SoundSystemComposerTests.Execute(cache, definition);
        var feature = definition[0].EntityId!.Value;
        var values = definition.Where(operation => operation.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(operation => operation.EntityId!.Value).ToArray();
        var phonemes = new Dictionary<string, CanonicalId>();
        for (var index = 0; index < representations.Length; index++)
        {
            var text = representations[index];
            var operations = AuthorPhonemeComposer.Build(cache, new(text, [text], [new(feature, values[index])]));
            SoundSystemComposerTests.Execute(cache, operations);
            phonemes.Add(text, operations.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value);
        }
        var forms = new Dictionary<string, Guid>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><AcceptUnspecifiedGraphemes>true</AcceptUnspecifiedGraphemes><Strata /></HC></ParserParameters>";
            var ws = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
            var valid = ValidCharacters.Load(ws);
            valid.AddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(character => character.ToString()));
            valid.SaveTo(ws);
            cache.ServiceLocator.WritingSystemManager.Save();
            var store = Path.Combine(Path.GetDirectoryName(cache.ProjectId.Path)!, "WritingSystemStore");
            Directory.CreateDirectory(store);
            using (var writer = XmlWriter.Create(Path.Combine(store, ws.LanguageTag + ".ldml"))) ws.WriteLdml(writer);
            foreach (var text in new[] { "nbi", "npc", "kkk", "ca", "la", "nga", "a", "á", "(a", ")a", "/a", "LSDa" })
            {
                var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
                    cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                    TsStringUtils.MakeString(text, cache.DefaultVernWs), "inventory witness",
                    new SandboxGenericMSA { MsaType = MsaType.kStem });
                forms.Add(text, entry.LexemeFormOA.Guid);
            }
        });
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        var before = await Parse(cache.ProjectId.Path, ["LSD", "La", "la"]);
        Assert.Empty(before[0].Analyses);
        Assert.Empty(before[1].Analyses);
        Assert.Contains(before[2].Analyses.SelectMany(analysis => analysis.Morphs),
            morph => morph.Form == forms["la"].ToString("D"));

        var casing = new CaseFunctions(cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem);
        foreach (var (text, phoneme) in phonemes)
        {
            var aliases = new[] { casing.ToTitle(text), casing.ToUpper(text) }.Where(alias => alias != text)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (aliases.Length > 0)
                SoundSystemComposerTests.Execute(cache, EditPhonemeComposer.Build(cache, new(phoneme, aliases, [])));
        }
        loader.Save(cache);
        var words = new[] { "LSD", "nbi", "NBI", "npc", "NPC", "kkk", "KKK", "ca", "Ca", "CA", "la", "La", "LA",
            "nga", "Nga", "NGa", "NGA", "a", "A", "á", "Á", "(a", ")a", "/a" };
        var after = await Parse(cache.ProjectId.Path, words);
        Assert.Empty(after[0].Analyses);
        var expected = new[] { "nbi", "nbi", "npc", "npc", "kkk", "kkk", "ca", "ca", "ca", "la", "la", "la",
            "nga", "nga", "nga", "nga", "a", "a", "á", "á", "(a", ")a", "/a" };
        for (var index = 1; index < words.Length; index++)
        {
            var selected = after[index].Analyses.SelectMany(analysis => analysis.Morphs).Select(morph => morph.Form).ToArray();
            Assert.Contains(forms[expected[index - 1]].ToString("D"), selected);
            Assert.DoesNotContain(selected, form => forms.Where(pair => pair.Key != expected[index - 1])
                .Any(pair => form == pair.Value.ToString("D")));
        }
    }

    private static async Task<IReadOnlyList<ParseWordEvidence>> Parse(string path, string[] words)
    {
        using var invoker = new PanGlossInvoker();
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(path, words, null) { CollectAnalyses = true },
            "test:explicit-inventory", CancellationToken.None);
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
