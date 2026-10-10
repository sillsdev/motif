using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Projection.Grammar;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>A SYNTHETIC EXAMPLE feature distinguishes category defaults from phoneme and lexical evidence.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class FeatureOwnerWarningTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData("default", false)]
    [InlineData("default", true)]
    [InlineData("inherited", false)]
    [InlineData("inherited", true)]
    [InlineData("phoneme", false)]
    [InlineData("phoneme", true)]
    public void AFeatureWithOnlyAnUnattributedOwnerKeepsItsReasonThroughTheWordJoinAndCli(
        string owner, bool withEvidence)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var (feature, _, _) = AuthorFeature(cache, owner, represented: false);
        var finding = Finding(feature, cache);
        if (withEvidence) finding = WithWords(finding, Word("tu"));

        var phoneme = owner == "phoneme";
        var path = phoneme ? WarningWordsPath.ProjectWide : WarningWordsPath.UnresolvedIdentity;
        var reason = phoneme ? WarningAttributionReason.NoWordAttribution : WarningAttributionReason.UnsupportedKind;
        Assert.Equal(path, finding.Subject[0].Reach!.Path);
        Assert.Equal(reason, finding.Subject[0].Reach!.Reason);
        Assert.Equal(phoneme ? WarningAttributionState.ProjectWide : WarningAttributionState.UnresolvedIdentity,
            finding.AttributionState);
        Assert.Equal(reason, finding.AttributionReason);
        Assert.Contains(phoneme ? "Your words: project-wide; no word attribution" :
            "Your words: unresolved identity; the named class has no supported route to words", Render(finding));
        Assert.DoesNotContain("none in the Selection", Render(finding));
        Assert.DoesNotContain("evidence unavailable", Render(finding));
    }

    [Theory]
    [InlineData("default", false)]
    [InlineData("default", true)]
    [InlineData("phoneme", false)]
    [InlineData("phoneme", true)]
    public void AFeatureWithALexicalRouteAlsoKeepsItsUnattributedOwnerLimit(string owner, bool withEvidence)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var (feature, value, _) = AuthorFeature(cache, owner, represented: false);
        var msa = (IMoStemMsa)cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId).MorphoSyntaxAnalysesOC.First();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            SetStructure(cache, feature, value, structure => msa.MsFeaturesOA = structure);
        });
        var finding = Finding(feature, cache);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        finding = JsonSerializer.Deserialize<GrammarWarning>(JsonSerializer.Serialize(finding, options), options)!;
        if (withEvidence) finding = WithWords(finding, Word("tu", msa));

        Assert.Equal(withEvidence ? WarningAttributionState.ExactUses : WarningAttributionState.EvidenceUnavailable,
            finding.AttributionState);
        Assert.Contains(owner == "phoneme" ? "no_word_attribution" : "unsupported_kind",
            JsonSerializer.SerializeToElement(finding, options)
            .GetProperty("attributionLimits").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(owner == "phoneme" ? "Attribution limits: an owner has no word attribution" :
            "Attribution limits: an owner's class has no supported route to words", Render(finding));
        if (withEvidence)
        {
            Assert.Equal("tu", Assert.Single(finding.YourWords!.Words).Row.Word);
            Assert.Equal(1, WarningWordsQuery.Touched([finding])!.Words);
        }
    }

    [Fact]
    public void APhonologicalFeatureSystemRetainsItsPhonemesSpellingCandidatesOutsideIdentityCounts()
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(pristine.CopyProjectFile());
        var (feature, _, phoneme) = AuthorFeature(cache, "phoneme", represented: true);
        var direct = WithWords(Finding(feature, cache), Word("tu"));
        var system = WithWords(Finding(cache.LangProject.PhFeatureSystemOA, cache), Word("tu"));

        Assert.Equal(WarningAttributionState.SpellingCandidates, direct.AttributionState);
        Assert.Equal(WarningAttributionState.SpellingCandidates, system.AttributionState);
        Assert.Equal(WarningWordsMatch.Membership, system.YourWords!.Match);
        Assert.Empty(system.YourWords.Words);
        Assert.Empty(system.YourWords.MembershipCandidates);
        Assert.Equal("tu", Assert.Single(system.YourWords.SpellingCandidates).Row.Word);
        var touched = WarningWordsQuery.Touched([system])!;
        Assert.Equal((0, 0, 1), (touched.Words, touched.ByMembershipOnly, touched.BySpellingOnly));
        Assert.Contains("Spelling candidates; not confirmed uses of the phoneme: tu", Render(system));

        var set = Finding(phoneme!.Owner, cache);
        Assert.Empty(set.Subject[0].Reach!.Spellings);
        Assert.Equal(WarningAttributionState.ProjectWide, set.AttributionState);
    }

    private static (IFsClosedFeature Feature, IFsSymFeatVal Value, IPhPhoneme? Phoneme) AuthorFeature(
        LcmCache cache, string owner, bool represented)
    {
        IFsClosedFeature feature = null!;
        IFsSymFeatVal value = null!;
        IPhPhoneme? phoneme = null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var system = owner == "phoneme" ? cache.LangProject.PhFeatureSystemOA : cache.LangProject.MsFeatureSystemOA;
            feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
            system.FeaturesOC.Add(feature);
            value = services.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            if (owner == "phoneme")
            {
                var set = services.GetInstance<IPhPhonemeSetFactory>().Create();
                cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
                OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, set);
                phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                set.PhonemesOC.Add(phoneme);
                SetStructure(cache, feature, value, structure => phoneme.FeaturesOA = structure);
                phoneme.CodesOS.Clear();
                if (represented)
                {
                    var code = services.GetInstance<IPhCodeFactory>().Create();
                    phoneme.CodesOS.Add(code);
                    code.Representation.set_String(cache.DefaultVernWs, "u");
                }
            }
            else
            {
                var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
                SetStructure(cache, feature, value, structure =>
                {
                    if (owner == "default") pos.DefaultFeaturesOA = structure;
                    else pos.InherFeatValOA = structure;
                });
            }
        });
        return (feature, value, phoneme);
    }

    private static void SetStructure(LcmCache cache, IFsClosedFeature feature, IFsSymFeatVal value,
        Action<IFsFeatStruc> attach)
    {
        var structure = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
        attach(structure);
        var spec = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
        structure.FeatureSpecsOC.Add(spec);
        spec.FeatureRA = feature;
        spec.ValueRA = value;
    }

    private static GrammarWarning Finding(ICmObject item, LcmCache cache)
    {
        var part = new GrammarWarningPart(item.ClassName, GrammarWarningPartRole.Object,
            item.Guid.ToString("D"), item.ClassName) { SubjectGuid = item.Guid.ToString("D") };
        return new(GrammarDiagnosticLevel.Warning, "feature", [part with { Reach = WarningReachReader.Reach(part, () => cache) }],
            [], "feature");
    }

    private static AssessmentWordResult Word(string word, IMoMorphSynAnalysis? msa = null) =>
        new(word, "no-analysis", false, "Search completed", 1, null)
        {
            StoredAnalyses = msa is null ? [] :
            [new ParserReading([new ParserReadingMorph("synthetic", "feature", "noun", null, false, null)
                { GrammaticalInfoId = msa.Guid.ToString("D") }]) { StoredAnalysisOpinion = ReadingGrade.Approved }],
        };

    private static GrammarWarning WithWords(GrammarWarning finding, AssessmentWordResult word) =>
        finding with { YourWords = WarningWordsQuery.YourWordsOf(finding, [word], []) };

    private static string Render(GrammarWarning finding) => CommandTextRenderer.Render(
        CommandOutcome<WarningsResponse>.Success(new(true, true, [finding], [], 1, 0)), asJson: false).Output;
}
