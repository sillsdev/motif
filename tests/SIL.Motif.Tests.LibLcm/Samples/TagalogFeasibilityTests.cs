using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Samples;

[Collection(LcmCacheTestCollection.Name)]
public sealed class TagalogFeasibilityTests
{
    private readonly ITestOutputHelper _output;

    private const string ParserParametersXml =
        "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";

    public TagalogFeasibilityTests(ITestOutputHelper output) => _output = output;

    [RealParserFact]
    public void SyntheticAffixProcessesParseSevenApprovedTagalogForms()
    {
        var run = RunPanelAssessment(controls: false);
        WritePanelEvidence(run.Response);

        Assert.Equal(
            run.Authored.Words.OrderBy(word => word, StringComparer.Ordinal),
            run.Response.Selection.Words);
        foreach (var (word, expected) in run.Authored.ExpectedMorphs)
        {
            var row = Assert.Single(run.Response.Words, candidate => candidate.Word == word);
            var details = DescribeParserResult(row, run.Response);
            AssertCompletedExpectedReading(row, expected, details);
        }
    }

    [RealParserFact]
    public void SyntheticAffixProcessRejectsCompletedNegativeControlsWithPositiveWitnesses()
    {
        var run = RunPanelAssessment(controls: true);
        WritePanelEvidence(run.Response);

        Assert.Equal(
            run.Authored.NegativeControlWords.Concat(run.Authored.PositiveControlMorphs.Keys)
                .OrderBy(word => word, StringComparer.Ordinal),
            run.Response.Selection.Words);
        foreach (var (word, expected) in run.Authored.PositiveControlMorphs)
        {
            var row = Assert.Single(run.Response.Words, candidate => candidate.Word == word);
            AssertCompletedExpectedReading(row, expected, DescribeParserResult(row, run.Response));
        }

        foreach (var (word, forbidden) in run.Authored.ForbiddenMorphs)
        {
            var row = Assert.Single(run.Response.Words, candidate => candidate.Word == word);
            var details = DescribeParserResult(row, run.Response);
            Assert.False(row.IsIncomplete);
            Assert.Equal("no-analysis", row.Outcome);
            Assert.Equal("Search completed", row.CompletionStatus);
            var morphology = Assert.IsType<ParseWordEvidence>(row.Morphology);
            Assert.Null(morphology.Refusal);
            Assert.Empty(morphology.Unavailable);
            Assert.Empty(morphology.Analyses);
            Assert.False(morphology.InvalidShape);
            Assert.False(morphology.Capped);
            Assert.False(morphology.TimedOut);
            Assert.False(HasExpectedIdentityReading(row, forbidden), details);
        }
    }

    [RealParserFact]
    public void InitialConsonantEnvironmentParsesUmAndInInfixesFromAText()
    {
        var run = RunAssessment(AuthorInfixText, "sulatmbin");

        Assert.Equal(new[] { "bumasa", "sinulat", "sumulat" }, run.Response.Selection.Words);
        AssertExpected(run, "sumulat");
        AssertExpected(run, "sinulat");
        AssertExpected(run, "bumasa");
    }

    [RealParserFact]
    public void NaturalClassCopyFormReportsSupportedAnalysisOrUnavailableEvidence()
    {
        var run = RunAssessment(AuthorCopyPatternText, "sulatm");
        var row = Assert.Single(run.Response.Words, candidate => candidate.Word == "susulat");
        if (HasExpectedReading(row, run.Authored.ExpectedMorphs["susulat"]))
        {
            AssertExpected(run, "susulat");
            return;
        }

        var warnings = run.Response.GrammarWarnings ?? [];
        Assert.Contains(
            "warning: Allomorph '[C^1][V^1]' has a reduplication pattern that cannot be loaded as an affix rule.",
            warnings);
        Assert.Contains(
            "warning: Allomorph '[C^1][V^1]' has a reduplication pattern that cannot be checked against the phoneme inventory.",
            warnings);
        Assert.Equal("no-analysis", row.Outcome);
        Assert.Equal("Some morphology evidence is unavailable.", row.EvidenceStatus);
        Assert.Equal("unavailable", row.Correctness!.Status);
        Assert.Equal(1, row.Correctness.Expected);
        Assert.Equal(0, row.Correctness.Matched);
        Assert.Contains(
            "warning: Allomorph '[C^1][V^1]' has a reduplication pattern that cannot be loaded as an affix rule.",
            row.Morphology!.Unavailable);
        Assert.Contains(
            "warning: Allomorph '[C^1][V^1]' has a reduplication pattern that cannot be checked against the phoneme inventory.",
            row.Morphology.Unavailable);
    }

    [RealParserFact]
    public void ExplicitSegmentalPrefixStillParsesAsTheNearestCopyPatternLesson()
    {
        var run = RunAssessment(AuthorExplicitPrefixText, "sulat");

        AssertExpected(run, "susulat");
    }

    private static AssessmentRun RunAssessment(
        Func<LcmCache, IReadOnlyDictionary<char, IPhPhoneme>, AuthoredText> author,
        string phonemeInventory)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(), "motif-synthetic-philippine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            string projectPath;
            AuthoredText authored = null!;
            using (var cache = NewLangProjFixture.CreateCache(temporaryRoot))
            {
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    cache.LangProject.MorphologicalDataOA.ParserParameters = ParserParametersXml;
                    var phonemes = AddPhonemes(cache, phonemeInventory);
                    authored = author(cache, phonemes);
                });

                new FwDataProjectLoader().Save(cache);
                projectPath = cache.ProjectId.Path;
            }

            var parserPath = PanGlossExecutable.TryLocate();
            Assert.NotNull(parserPath);
            var request = new AssessRequest(
                projectPath,
                new SelectionRequest(false, [authored.TextId], [], false, null),
                PerWordLimitMs: 5000);
            var outcome = AssessCommand.Assess(
                request,
                Path.Combine(temporaryRoot, "motif"),
                parserPath,
                null,
                CancellationToken.None);

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message ?? "The Assessment returned no result.");
            return new AssessmentRun(authored, outcome.Value!);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private PanelAssessmentRun RunPanelAssessment(bool controls)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(), "motif-synthetic-tagalog-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            string projectPath;
            TagalogAffixPanelFixture authored = null!;
            using (var cache = NewLangProjFixture.CreateCache(temporaryRoot))
            {
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    cache.LangProject.MorphologicalDataOA.ParserParameters = ParserParametersXml;
                    authored = TagalogAffixPanelFixture.Author(cache);
                });

                new FwDataProjectLoader().Save(cache);
                projectPath = cache.ProjectId.Path;
            }

            var parserPath = PanGlossExecutable.TryLocate();
            Assert.NotNull(parserPath);
            _output.WriteLine($"PanGloss executable: {parserPath}");
            var request = new AssessRequest(
                projectPath,
                new SelectionRequest(
                    false,
                    [controls ? authored.ControlTextId : authored.TextId],
                    [],
                    false,
                    null),
                PerWordLimitMs: 5000);
            var outcome = AssessCommand.Assess(
                request,
                Path.Combine(temporaryRoot, "motif"),
                parserPath,
                null,
                CancellationToken.None);

            Assert.True(outcome.Succeeded, outcome.Refusal?.Message ?? "The Assessment returned no result.");
            return new PanelAssessmentRun(authored, outcome.Value!);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static IReadOnlyDictionary<char, IPhPhoneme> AddPhonemes(LcmCache cache, string inventory)
    {
        var services = cache.ServiceLocator;
        var phonology = cache.LangProject.PhonologicalDataOA;
        var phonemeSets = phonology.PhonemeSetsOS;
        if (phonemeSets.Count == 0)
            phonemeSets.Add(services.GetInstance<IPhPhonemeSetFactory>().Create());
        var phonemeSet = phonemeSets[0];
        OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, phonemeSet);
        var phonemes = new Dictionary<char, IPhPhoneme>();

        foreach (var symbol in inventory.Distinct())
        {
            var phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
            phonemeSet.PhonemesOC.Add(phoneme);
            phoneme.Name.set_String(cache.DefaultVernWs, symbol.ToString());

            var code = services.GetInstance<IPhCodeFactory>().Create();
            phoneme.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, symbol.ToString());
            phonemes.Add(symbol, phoneme);
        }

        return phonemes;
    }

    private static AuthoredText AuthorInfixText(
        LcmCache cache, IReadOnlyDictionary<char, IPhPhoneme> phonemes)
    {
        AddNaturalClass(cache, phonemes, "C", "Consonants", "sltmbn");
        AddNaturalClass(cache, phonemes, "V", "Vowels", "aui");
        var environment = AddEnvironment(cache, "/ # [C] _");
        var um = MakeEntry(cache, MoMorphTypeTags.kguidMorphInfix, "um", "um");
        var infixIn = MakeEntry(cache, MoMorphTypeTags.kguidMorphInfix, "in", "in");
        SetPosition(um, environment);
        SetPosition(infixIn, environment);

        var sulat = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "sulat", "write");
        var basa = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "basa", "read");
        return MakeApprovedText(cache,
        [
            ("sumulat", [um, sulat]),
            ("sinulat", [infixIn, sulat]),
            ("bumasa", [um, basa]),
        ]);
    }

    private static AuthoredText AuthorCopyPatternText(
        LcmCache cache, IReadOnlyDictionary<char, IPhPhoneme> phonemes)
    {
        AddNaturalClass(cache, phonemes, "C", "Consonants", "sltm");
        AddNaturalClass(cache, phonemes, "V", "Vowels", "ua");
        var copy = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "[C^1][V^1]-", "CV copy");
        var sulat = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "sulat", "write");
        return MakeApprovedText(cache, [("susulat", [copy, sulat])]);
    }

    private static AuthoredText AuthorExplicitPrefixText(
        LcmCache cache, IReadOnlyDictionary<char, IPhPhoneme> phonemes)
    {
        var prefix = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "su-", "su");
        var sulat = MakeEntry(cache, MoMorphTypeTags.kguidMorphStem, "sulat", "write");
        return MakeApprovedText(cache, [("susulat", [prefix, sulat])]);
    }

    private static IPhNCSegments AddNaturalClass(
        LcmCache cache,
        IReadOnlyDictionary<char, IPhPhoneme> phonemes,
        string abbreviation,
        string name,
        string members)
    {
        var naturalClass = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
        cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
        naturalClass.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
        naturalClass.Name.set_String(cache.DefaultAnalWs, name);
        foreach (var member in members)
            naturalClass.SegmentsRC.Add(phonemes[member]);
        return naturalClass;
    }

    private static IPhEnvironment AddEnvironment(LcmCache cache, string representation)
    {
        var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
        environment.StringRepresentation = TsStringUtils.MakeString(representation, cache.DefaultVernWs);
        return environment;
    }

    private static void SetPosition(AuthoredMorph affix, IPhEnvironment environment)
    {
        var allomorph = (IMoAffixAllomorph)affix.Form;
        allomorph.PositionRS.Clear();
        allomorph.PositionRS.Add(environment);
    }

    private static AuthoredMorph MakeEntry(LcmCache cache, Guid morphType, string form, string gloss)
    {
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryFactory>().Create(
            services.GetInstance<IMoMorphTypeRepository>().GetObject(morphType),
            TsStringUtils.MakeString(form, cache.DefaultVernWs),
            gloss,
            new SandboxGenericMSA
            {
                MsaType = morphType == MoMorphTypeTags.kguidMorphStem ? MsaType.kStem : MsaType.kUnclassified,
            });
        return new AuthoredMorph(entry.LexemeFormOA, entry.MorphoSyntaxAnalysesOC.Single());
    }

    private static AuthoredText MakeApprovedText(
        LcmCache cache, IReadOnlyList<(string Word, AuthoredMorph[] Morphs)> words)
    {
        var services = cache.ServiceLocator;
        var text = services.GetInstance<ITextFactory>().Create();
        text.Name.set_String(cache.DefaultAnalWs, "Synthetic Philippine teaching Text");
        var contents = services.GetInstance<IStTextFactory>().Create();
        text.ContentsOA = contents;
        var expected = new Dictionary<string, AuthoredMorph[]>(StringComparer.Ordinal);

        foreach (var (word, morphs) in words)
        {
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString(word, cache.DefaultVernWs);
            var segment = paragraph.SegmentsOS.Single();

            var wordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            foreach (var morph in morphs)
            {
                var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = morph.Form;
                bundle.MsaRA = morph.Msa;
            }
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            segment.AnalysesRS.Add(analysis);
            expected.Add(word, morphs);
        }

        return new AuthoredText(text.Guid, words.Select(item => item.Word).ToArray(), expected);
    }

    private static void AssertExpected(AssessmentRun run, string word)
    {
        var row = Assert.Single(run.Response.Words, candidate => candidate.Word == word);
        var details = DescribeParserResult(row, run.Response);
        Assert.True(row.Correctness!.Status == "covered", details);
        Assert.Equal(1, row.Correctness.Expected);
        Assert.Equal(1, row.Correctness.Matched);
        Assert.False(row.Morphology!.InvalidShape);
        Assert.False(row.Morphology.Capped);
        Assert.False(row.Morphology.TimedOut);
        Assert.Empty(row.Morphology.Unavailable);
        Assert.True(HasExpectedReading(row, run.Authored.ExpectedMorphs[word]),
            details);
    }

    private static void AssertCompletedExpectedReading(
        AssessmentWordResult row,
        IReadOnlyList<TagalogAffixPanelMorph> expected,
        string details)
    {
        Assert.False(row.IsIncomplete, details);
        Assert.Equal("analysed", row.Outcome);
        Assert.Equal("Search completed", row.CompletionStatus);
        var correctness = Assert.IsType<WordCorrectness>(row.Correctness);
        Assert.Equal("covered", correctness.Status);
        Assert.Equal(1, correctness.Expected);
        Assert.Equal(1, correctness.Matched);
        var morphology = Assert.IsType<ParseWordEvidence>(row.Morphology);
        Assert.Null(morphology.Refusal);
        Assert.False(morphology.InvalidShape);
        Assert.False(morphology.Capped);
        Assert.False(morphology.TimedOut);
        Assert.Empty(morphology.Unavailable);
        Assert.True(HasExpectedIdentityReading(row, expected), details);
    }

    private static bool HasExpectedReading(AssessmentWordResult row, IReadOnlyList<AuthoredMorph> expected)
    {
        var expectedForms = expected.Select(morph => morph.Form.Guid.ToString("D")).ToArray();
        return row.Morphology!.Analyses.Any(analysis =>
            analysis.Morphs.Select(morph => morph.Form).SequenceEqual(expectedForms, StringComparer.Ordinal));
    }

    private static bool HasExpectedIdentityReading(
        AssessmentWordResult row, IReadOnlyList<TagalogAffixPanelMorph> expected)
    {
        return row.Morphology!.Analyses.Any(analysis =>
            analysis.Morphs.Count == expected.Count && analysis.Morphs.Zip(expected).All(pair =>
                pair.First.Form == pair.Second.Form.ToString("D") &&
                pair.First.Msa == pair.Second.Msa.ToString("D")));
    }

    private void WritePanelEvidence(AssessCommandResponse response)
    {
        _output.WriteLine("HC per-word timings:");
        foreach (var word in response.Words)
            _output.WriteLine(
                $"  {word.Word}: {word.ElapsedMs?.ToString() ?? "not recorded"} ms; " +
                $"completion: {word.CompletionStatus}; incomplete: {word.IsIncomplete}");

        var warnings = response.GrammarWarnings ?? [];
        _output.WriteLine("Grammar warnings:");
        if (warnings.Count == 0)
            _output.WriteLine("  none");
        foreach (var warning in warnings)
            _output.WriteLine($"  {warning}");
    }

    private static string DescribeParserResult(AssessmentWordResult row, AssessCommandResponse response)
    {
        var readings = row.Morphology?.Analyses.Select(analysis =>
            string.Join("+", analysis.Morphs.Select(morph => morph.Form))) ?? [];
        var warnings = response.GrammarWarnings ?? [];
        return $"Outcome: {row.Outcome}; {row.EvidenceStatus}; readings: {string.Join("; ", readings)}; " +
               $"grammar warnings: {string.Join(" | ", warnings)}";
    }

    private sealed record AuthoredMorph(IMoForm Form, IMoMorphSynAnalysis Msa);

    private sealed record AuthoredText(
        Guid TextId,
        IReadOnlyList<string> Words,
        IReadOnlyDictionary<string, AuthoredMorph[]> ExpectedMorphs);

    private sealed record AssessmentRun(AuthoredText Authored, AssessCommandResponse Response);

    private sealed record PanelAssessmentRun(TagalogAffixPanelFixture Authored, AssessCommandResponse Response);
}
