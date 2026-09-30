using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class ExplainedWordCardWalkthroughProject : IDisposable
{
    private const string TextIdValue = "explained-word-card";
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
        Directory.CreateDirectory(root);
        try
        {
            var repository = RepositoryRoot();
            var sourcePath = Path.Combine(repository, "samples", "synthetic-turkic", "sample.json");
            var sample = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))!.AsObject();
            sample["stems"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "explained-ev",
                ["form"] = "ev",
                ["partOfSpeech"] = "noun",
                ["gloss"] = "house",
            });
            sample["texts"] = new JsonArray(new JsonObject
            {
                ["id"] = TextIdValue,
                ["title"] = "Round 3 word examples",
                ["sentences"] = new JsonArray(
                    JsonValue.Create("geldi"), JsonValue.Create("evler"), JsonValue.Create("kediye"),
                    JsonValue.Create("adamlarında"), JsonValue.Create("günler"), JsonValue.Create("okullarında")),
            });

            var specPath = Path.Combine(root, "sample.json");
            var outputPath = Path.Combine(root, "sample-output");
            await File.WriteAllTextAsync(specPath, sample.ToJsonString());
            var build = await BuildProjectAsync(specPath, outputPath);
            var project = new ExplainedWordCardWalkthroughProject(root, build.ProjectPath, build.TextId);
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

    private static async Task<(string ProjectPath, Guid TextId)> BuildProjectAsync(string specPath, string outputPath)
    {
        var builderPath = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var start = new ProcessStartInfo(builderPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(specPath);
        start.ArgumentList.Add(outputPath);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The synthetic word-card project builder did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var standardOutput = await output;
        var standardError = await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The synthetic word-card project did not build: {standardError}");

        using var result = JsonDocument.Parse(standardOutput);
        var projectPath = result.RootElement.GetProperty("projectPath").GetString()!;
        var textId = Guid.Parse(result.RootElement.GetProperty("texts")[0].GetProperty("guid").GetString()!);
        return (projectPath, textId);
    }

    private IReadOnlyDictionary<string, IReadOnlyList<MorphReference>> SeedReadings()
    {
        using var cache = new FwDataProjectLoader().LoadCache(FwDataPath);
        var services = cache.ServiceLocator;
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }

    private sealed record MorphReference(IMoForm Form, IMoMorphSynAnalysis Msa, ILexSense Sense);
}
