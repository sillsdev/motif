using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.WritingSystems;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class ExplainedWordCardWalkthroughProject : IDisposable
{
    private readonly string _root;

    private ExplainedWordCardWalkthroughProject(string root, string projectPath, Guid textId)
    {
        _root = root;
        FwDataPath = projectPath;
        TextId = textId;
        ManagedRoot = Path.Combine(root, "managed");
        Directory.CreateDirectory(ManagedRoot);
        ParserPath = FakeParser.CopyRecordingInvocations(Path.Combine(ManagedRoot, "fake-pangloss"));
    }

    public string FwDataPath { get; }

    public string ManagedRoot { get; }

    public Guid TextId { get; }

    public string ParserPath { get; }

    public static async Task<ExplainedWordCardWalkthroughProject> CreateAsync(string root)
    {
        WalkthroughTestFiles.DeleteDirectory(root);
        try
        {
            var fixture = BuildOutput.RequirePreparedDirectory(BuildOutput.ExplainedWordCardFixtureDirectory,
                "The Explained Word Card walkthrough fixture");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "fixture.json")));
            var relativeProjectPath = manifest.RootElement.GetProperty("projectPath").GetString()
                ?? throw new InvalidDataException("The prepared walkthrough fixture has no project path.");
            var textId = Guid.Parse(manifest.RootElement.GetProperty("textId").GetString()
                ?? throw new InvalidDataException("The prepared walkthrough fixture has no Text id."));
            var outputPath = Path.Combine(root, "sample-output");
            WalkthroughTestFiles.CopyDirectory(fixture, outputPath);
            var projectPath = Path.GetFullPath(Path.Combine(outputPath, relativeProjectPath));
            if (!File.Exists(projectPath))
                throw new FileNotFoundException("The prepared walkthrough project is missing.", projectPath);
            var project = new ExplainedWordCardWalkthroughProject(root, projectPath, textId);
            project.SetParserBehavior(project.SeedReadings());
            return project;
        }
        catch
        {
            WalkthroughTestFiles.DeleteDirectory(root);
            throw;
        }
    }

    public void Dispose() => WalkthroughTestFiles.DeleteDirectory(_root);

    private IReadOnlyDictionary<string, IReadOnlyList<MorphReference>> SeedReadings()
    {
        using var cache = new FwDataProjectLoader().LoadCache(FwDataPath);
        var services = cache.ServiceLocator;
        services.WritingSystems.CurrentVernacularWritingSystems[0].DefaultFont =
            new FontDefinition(WalkthroughFonts.DejaVuSansFamily);
        services.WritingSystems.CurrentAnalysisWritingSystems[0].DefaultFont =
            new FontDefinition(WalkthroughFonts.DejaVuSansFamily);
        var entries = services.GetInstance<ILexEntryRepository>().AllInstances().ToArray();
        var text = services.GetInstance<ITextRepository>().GetObject(TextId);
        var paragraphs = text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>()
            .ToDictionary(paragraph => paragraph.Contents.Text.Trim(), StringComparer.Ordinal);
        var readings = new Dictionary<string, IReadOnlyList<MorphReference>>(StringComparer.Ordinal)
        {
            ["geldi"] = [Stem(cache, entries, "gel"), Suffix(cache, entries, "past tense", "di")],
            ["evler"] = [Stem(cache, entries, "ev"), Suffix(cache, entries, "plural", "ler")],
            ["kediye"] = [Stem(cache, entries, "kedi"), Suffix(cache, entries, "dative", "ye")],
            ["adamlarında"] = [Stem(cache, entries, "adam"), Suffix(cache, entries, "plural", "lar"),
                Suffix(cache, entries, "third-person singular possession", "ın"),
                Suffix(cache, entries, "locative", "da")],
            ["okullarında"] = [Stem(cache, entries, "okul"), Suffix(cache, entries, "plural", "lar"),
                Suffix(cache, entries, "third-person singular possession", "ın"),
                Suffix(cache, entries, "locative", "da")],
        };

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            StoreAnalysis(cache, paragraphs["geldi"].SegmentsOS.Single(), "geldi", readings["geldi"], Opinions.approves);
            StoreAnalysis(cache, paragraphs["evler"].SegmentsOS.Single(), "evler", readings["evler"], Opinions.noopinion);
            StoreAnalysis(cache, paragraphs["kediye"].SegmentsOS.Single(), "kediye", readings["kediye"], Opinions.disapproves);
            StoreAnalysis(cache, paragraphs["okullarında"].SegmentsOS.Single(), "okullarında", readings["okullarında"], Opinions.approves);
            foreach (var paragraph in paragraphs.Values) paragraph.ParseIsCurrent = true;
        });

        services.WritingSystemManager.Save();
        new FwDataProjectLoader().Save(cache);
        File.SetLastWriteTimeUtc(FwDataPath, new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc));
        return readings;
    }

    private static MorphReference Stem(LcmCache cache, IReadOnlyList<ILexEntry> entries, string form)
    {
        var entry = entries.Single(candidate => FormText(candidate.LexemeFormOA, cache.DefaultVernWs) == form);
        var sense = entry.SensesOS.Single();
        return new MorphReference(entry.LexemeFormOA!, entry.MorphoSyntaxAnalysesOC.Single(), sense);
    }

    private static MorphReference Suffix(
        LcmCache cache, IReadOnlyList<ILexEntry> entries, string gloss, string form)
    {
        var entry = entries.Single(candidate => candidate.SensesOS.Any(sense =>
            sense.Gloss.get_String(cache.DefaultAnalWs)?.Text == gloss));
        var morph = Forms(entry).Single(candidate => FormText(candidate, cache.DefaultVernWs) == form);
        var sense = entry.SensesOS.Single();
        return new MorphReference(morph, sense.MorphoSyntaxAnalysisRA!, sense);
    }

    private static IEnumerable<IMoForm> Forms(ILexEntry entry) =>
        new[] { entry.LexemeFormOA }.OfType<IMoForm>().Concat(entry.AlternateFormsOS);

    private static string? FormText(IMoForm? form, int writingSystem) =>
        form?.Form.get_String(writingSystem)?.Text;

    private static void StoreAnalysis(
        LcmCache cache, ISegment segment, string form, IReadOnlyList<MorphReference> morphs, Opinions opinion)
    {
        var wordform = segment.AnalysesRS.OfType<IWfiWordform>().Single(candidate =>
            candidate.Form.get_String(cache.DefaultVernWs)?.Text == form);
        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
        wordform.AnalysesOC.Add(analysis);
        analysis.CategoryRA = ((IMoStemMsa)morphs[0].Msa).PartOfSpeechRA;
        foreach (var morph in morphs)
        {
            var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = morph.Form;
            bundle.MsaRA = morph.Msa;
            bundle.SenseRA = morph.Sense;
        }

        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, opinion);
        segment.AnalysesRS[segment.AnalysesRS.IndexOf(wordform)] = analysis;
    }

    private void SetParserBehavior(IReadOnlyDictionary<string, IReadOnlyList<MorphReference>> readings)
    {
        object Parsed(string word, string outcome, IReadOnlyList<MorphReference> morphs, string signature) => new
        {
            word,
            outcome,
            signature,
            analyses = new[]
            {
                new
                {
                    morphs = morphs.Select(morph => new
                    {
                        form = morph.Form.Guid.ToString("D"),
                        msa = morph.Msa.Guid.ToString("D"),
                        inflType = (string?)null,
                        guessedString = (string?)null,
                    }).ToArray(),
                },
            },
        };

        FakeParser.BehaveBesideExecutable(ParserPath, new
        {
            subcommands = new
            {
                batch = new
                {
                    words = new object[]
                    {
                        Parsed("geldi", "complete", readings["geldi"], "geldi"),
                        Parsed("evler", "complete", readings["evler"], "evler"),
                        Parsed("kediye", "complete", readings["kediye"], "kediye"),
                        Parsed("adamlarında", "complete", readings["adamlarında"], "adamlarinda"),
                        new { word = "günler", outcome = "no-analysis", signature = "-", analyses = Array.Empty<object>() },
                        Parsed("okullarında", "capped", readings["okullarında"], "okullarinda"),
                    },
                },
            },
        });
    }

    private sealed record MorphReference(IMoForm Form, IMoMorphSynAnalysis Msa, ILexSense Sense);
}
