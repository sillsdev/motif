using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.Assess;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using SIL.Motif.Tests.Samples;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class RealParserBatchTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task SharedMorphologyRetainsSameOwnerAllomorphsAndMatchesAllApprovedReadings()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        IMoStemAllomorph alternate = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            alternate = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = entry.LexemeFormOA.MorphTypeRA;
            alternate.Form.set_String(cache.DefaultVernWs, SeededProject.FirstForm);
            var word = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString(SeededProject.FirstForm, cache.DefaultVernWs));
            foreach (var form in new IMoForm[] { entry.LexemeFormOA, alternate })
            {
                var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                word.AnalysesOC.Add(analysis);
                var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = form;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                bundle.SenseRA = entry.SensesOS[0];
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            }
        });
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        var root = Path.Combine(Path.GetTempPath(), "motif-morphology-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            using var invoker = new PanGlossInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);
            var produced = await assessor.ProduceAsync(new AssessmentScope.Words([SeededProject.FirstForm, SeededProject.FirstForm, "mofita"],
                [AssessmentKind.ParseTime, AssessmentKind.Correctness], TimeSpan.FromSeconds(5)),
                Path.GetDirectoryName(cache.ProjectId.Path)!, CancellationToken.None);
            var batch = Assert.IsType<AssessmentRaw.Batch>(produced.Single(item => item.Kind == AssessmentKind.Correctness).Raw).Analysis;
            var first = batch.Words[0];
            Assert.Equal("covered", first.Correctness!.Status);
            Assert.Equal(2, first.Correctness.Expected);
            Assert.Equal(2, first.Correctness.Matched);
            Assert.Empty(first.Morphology!.Unavailable);
            var forms = first.Morphology.Analyses.SelectMany(analysis => analysis.Morphs).Select(morph => morph.Form).ToHashSet();
            Assert.Contains(entry.LexemeFormOA.Guid.ToString("D"), forms);
            Assert.Contains(alternate.Guid.ToString("D"), forms);
            Assert.All(first.Morphology.Analyses.SelectMany(analysis => analysis.Morphs), morph =>
                Assert.Equal(entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D"), morph.Msa));
            Assert.Equal(1, batch.Words[1].Morphology!.Index);
            Assert.Equal("covered", batch.Words[1].Correctness!.Status);
            Assert.Equal(WordOutcome.NoAnalysis, batch.Words[2].Outcome);
            Assert.Equal("no-expectations", batch.Words[2].Correctness!.Status);
            Assert.Equal(produced[0].Invocation, produced[1].Invocation);
            Assert.Equal(BatchInvocationEvidence.DigestFile(produced[0].Invocation!.AnalysesPath!),
                produced[0].Invocation!.AnalysesSha256);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [RealParserFact]
    public async Task InflectionalVariantRetainsVariantFormBaseMsaAndInflTypeGuids()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        ILexEntry variant = null!;
        ILexEntryInflType inflType = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var lexDb = cache.LangProject.LexDbOA;
            if (lexDb.VariantEntryTypesOA is null)
                lexDb.VariantEntryTypesOA = services.GetInstance<ICmPossibilityListFactory>().Create();
            inflType = services.GetInstance<ILexEntryInflTypeFactory>().Create();
            lexDb.VariantEntryTypesOA.PossibilitiesOS.Add(inflType);
            inflType.Name.set_String(cache.DefaultAnalWs, "Inflectional variant");
            inflType.Abbreviation.set_String(cache.DefaultAnalWs, "infl.var.");
            var variantRef = entry.CreateVariantEntryAndBackRef(
                inflType, TsStringUtils.MakeString("motifv", cache.DefaultVernWs));
            variant = variantRef.OwnerOfClass<ILexEntry>();

            var word = services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("motifv", cache.DefaultVernWs));
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            word.AnalysesOC.Add(analysis);
            var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = variant.LexemeFormOA;
            bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
            bundle.InflTypeRA = inflType;
            bundle.SenseRA = entry.SensesOS[0];
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b", "v");
        var root = Path.Combine(Path.GetTempPath(), "motif-variant-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            using var invoker = new PanGlossInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);
            var produced = await assessor.ProduceAsync(
                new AssessmentScope.Words(["motifv"], [AssessmentKind.Correctness], TimeSpan.FromSeconds(5)),
                Path.GetDirectoryName(cache.ProjectId.Path)!, CancellationToken.None);
            var batch = Assert.IsType<AssessmentRaw.Batch>(produced.Single().Raw).Analysis;
            var word = Assert.Single(batch.Words);
            Assert.Equal("covered", word.Correctness!.Status);
            Assert.Equal(1, word.Correctness.Expected);
            Assert.Equal(1, word.Correctness.Matched);
            Assert.Empty(word.Morphology!.Unavailable);
            var morph = Assert.Single(Assert.Single(word.Morphology.Analyses).Morphs);
            Assert.Equal(variant.LexemeFormOA.Guid.ToString("D"), morph.Form);
            Assert.Equal(entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D"), morph.Msa);
            Assert.Equal(inflType.Guid.ToString("D"), morph.InflType);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [RealParserFact]
    public async Task ExhaustedPerWordBudgetReturnsPerWordCapsAndContinuesTheBatch()
    {
        var fixture = RuntimeSampleProjectFixture.Create("runtime-batch-cap");
        var tokens = fixture.Tokens;
        fixture.Sample.Stems = [fixture.Sample.Stems[0]];
        fixture.Sample.PartsOfSpeech = [fixture.Sample.PartsOfSpeech[0]];
        fixture.Sample.AffixTemplates = [fixture.Sample.AffixTemplates[0]];
        var slots = Enumerable.Range(0, 8).Select(index => tokens.Id + "-suffix-slot-" + index).ToArray();
        fixture.Sample.AffixSlots.AddRange(slots.Select((slot, index) =>
            new RuntimeAffixSlotSpec(slot, tokens.Id + " suffix slot " + index, tokens.NounId, Optional: false)));
        var nounTemplateIndex = fixture.Sample.AffixTemplates.FindIndex(template =>
            template.Id == tokens.NounTemplateId);
        fixture.Sample.AffixTemplates[nounTemplateIndex] = fixture.Sample.AffixTemplates[nounTemplateIndex] with
            { SuffixSlots = slots };
        // Four alternatives make the eight-slot search reach the two-step work ceiling.
        var suffixAllomorphs = Enumerable.Range(0, slots.Length / 2)
            .Select(index => new RuntimeAllomorphSpec(
                tokens.Id + "-suffix-form-" + index, tokens.SuffixForm))
            .ToArray();
        fixture.Sample.Affixes.Add(new(tokens.Id + "-suffix", tokens.NounId, slots,
            tokens.Id + " repeated suffix", suffixAllomorphs));
        var cappedWord = tokens.NounForm + string.Concat(Enumerable.Repeat(tokens.SuffixForm, slots.Length));
        var nonParseWord = tokens.PrefixForm;
        Assert.Contains(nonParseWord, fixture.Sample.Phonemes);
        var root = Path.Combine(BuildOutput.ProductDirectory, "RealParserBatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var specPath = Path.Combine(root, "sample.json");
        await RuntimeSampleProjectFixture.WriteSampleAsync(specPath, fixture.Sample);
        var builder = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var build = new ProcessStartInfo(builder);
        build.ArgumentList.Add("build");
        build.ArgumentList.Add(specPath);
        build.ArgumentList.Add(Path.Combine(root, "output"));
        var buildResult = ToolProcess.Run(build);
        Assert.True(buildResult.ExitCode == 0, buildResult.Error);
        using var builtProject = JsonDocument.Parse(buildResult.Output);
        var projectPath = builtProject.RootElement.GetProperty("projectPath").GetString()!;
        string[] words = [cappedWord, nonParseWord];
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var outcome = await invoker.RunAsync(
                new PanGlossRequest.Batch(projectPath, words, TimeSpan.FromSeconds(1), PerWordStepLimit: 2)
                    { CollectAnalyses = true },
                "test:per-word-cap", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
            Assert.True(outcome is PanGlossOutcome.Completed, outcome.Message);
            var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
            var results = BatchTsvParser.Parse(completed.Output);
            Assert.Equal(words, results.Select(result => result.Word));
            Assert.Equal(WordOutcome.Capped, results[0].Outcome);
            Assert.Equal(WordOutcome.NoAnalysis, results[1].Outcome);
            var evidence = ParseMorphEvidence.Read(completed.MorphologyOutput!, words);
            Assert.True(evidence[0].Capped);
            Assert.False(evidence[1].Capped);
            Assert.Contains("CAP", completed.StandardError);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [RealParserFact]
    public async Task RuntimeSeededStemsParseAndASegmentableUnknownWordHasNoAnalysis()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");

        string[] words = [SeededProject.FirstForm, SeededProject.SecondForm, "mofita"];
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(cache.ProjectId.Path, words, TimeSpan.FromSeconds(1)),
            "test:runtime-seeded-stems", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));

        Assert.True(outcome is PanGlossOutcome.Completed, $"Expected a completed batch, received {outcome}.");
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var results = BatchTsvParser.Parse(completed.Output);

        Assert.Equal(words, results.Select(result => result.Word));
        Assert.Collection(results,
            first => Assert.Equal(WordOutcome.Analysed, first.Outcome),
            second => Assert.Equal(WordOutcome.Analysed, second.Outcome),
            unknown => Assert.Equal(WordOutcome.NoAnalysis, unknown.Outcome));
    }

    [RealParserFact]
    public async Task BatchStatisticsRecreatesThePreviousCacheAndReadsDeterministicWork()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        var root = Path.Combine(Path.GetTempPath(), "motif-pangloss-stats-v9-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cachePath = Path.Combine(root, "stats.sqlite");
        var word = SeededProject.FirstForm;
        using var invoker = new PanGlossInvoker();

        try
        {
            var first = await invoker.RunAsync(
                new PanGlossRequest.Batch(cache.ProjectId.Path, [word], TimeSpan.FromSeconds(5), cachePath)
                    { ThreadCount = 1 },
                "test:stats-v9-prime", CancellationToken.None);
            Assert.IsType<PanGlossOutcome.Completed>(first);
            SetCacheSchemaVersion(cachePath, PanGlossInterfaceVersions.StatsCacheVersion - 1);

            var second = await invoker.RunAsync(
                new PanGlossRequest.Batch(cache.ProjectId.Path, [word], TimeSpan.FromSeconds(5), cachePath)
                    { ThreadCount = 1 },
                "test:stats-v9-recreate", CancellationToken.None);

            Assert.IsType<PanGlossOutcome.Completed>(second);
            Assert.Equal(PanGlossInterfaceVersions.StatsCacheVersion, ReadInt64(cachePath,
                "SELECT schema_version FROM cache_identity WHERE cache_id = 1;"));
            var objectKeys = ReadStrings(cachePath, "SELECT key FROM object ORDER BY object_id;");
            Assert.Contains(objectKeys, key => key.StartsWith("lex_entry:", StringComparison.Ordinal));
            var allomorphKeys = ReadStrings(cachePath,
                "SELECT key FROM allomorph WHERE allomorph_id > 0 ORDER BY allomorph_id;");
            Assert.Contains(allomorphKeys, key => key.StartsWith("lex_entry:", StringComparison.Ordinal) &&
                key.Contains("#allo", StringComparison.Ordinal));

            var statistics = PanGlossBatchStatisticsReader.Read(cachePath, [word]);
            var counters = statistics.Words[word];
            Assert.NotNull(counters.WorkSteps);
            Assert.Equal(ReadInt64(cachePath, "SELECT work FROM word LIMIT 1;"), counters.WorkSteps);
            Assert.True(counters.WorkSteps >= counters.Attempts);
            Assert.Contains(statistics.ObjectTimings,
                row => row.Key.StartsWith("lex_entry:", StringComparison.Ordinal));

            var query = await invoker.RunAsync(new PanGlossRequest.Stats(cache.ProjectId.Path, cachePath,
                    ["--group", "word", "--format", "jsonl"]),
                "test:stats-v9-query", CancellationToken.None);
            var statsOutput = Assert.IsType<PanGlossOutcome.Completed>(query).Output;
            var rows = statsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, rows.Length);
            using var metadata = JsonDocument.Parse(rows[0]);
            Assert.Equal("word", metadata.RootElement.GetProperty("orientation").GetString());
            using var wordStatistics = JsonDocument.Parse(rows[1]);
            Assert.Equal(word, wordStatistics.RootElement.GetProperty("form").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SetCacheSchemaVersion(string path, int version)
    {
        using var connection = OpenCache(path);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE cache_identity SET schema_version = $version WHERE cache_id = 1;";
        command.Parameters.AddWithValue("$version", version);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static long ReadInt64(string path, string query)
    {
        using var connection = OpenCache(path);
        using var command = connection.CreateCommand();
        command.CommandText = query;
        return (long)command.ExecuteScalar()!;
    }

    private static string[] ReadStrings(string path, string query)
    {
        using var connection = OpenCache(path);
        using var command = connection.CreateCommand();
        command.CommandText = query;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return [.. values];
    }

    private static SqliteConnection OpenCache(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    [RealParserFact]
    public async Task ParallelBatchPreservesSingleThreadWordOrderAndAnalysisContent()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");

        string[] words = [SeededProject.FirstForm, SeededProject.SecondForm, "mofita"];
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var singleThreaded = await invoker.RunAsync(
            new PanGlossRequest.Batch(cache.ProjectId.Path, words, TimeSpan.FromSeconds(1))
            {
                CollectAnalyses = true,
                ThreadCount = 1,
            },
            "test:single-thread-batch", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));
        var parallel = await invoker.RunAsync(
            new PanGlossRequest.Batch(cache.ProjectId.Path, words, TimeSpan.FromSeconds(1))
            {
                CollectAnalyses = true,
                ThreadCount = 2,
            },
            "test:parallel-batch", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));

        var singleOutput = Assert.IsType<PanGlossOutcome.Completed>(singleThreaded);
        var parallelOutput = Assert.IsType<PanGlossOutcome.Completed>(parallel);
        var singleRows = BatchTsvParser.Parse(singleOutput.Output);
        var parallelRows = BatchTsvParser.Parse(parallelOutput.Output);
        var stableRows = singleRows.Select(row => (row.Index, row.Word, row.Outcome, row.Signature));
        var parallelStableRows = parallelRows.Select(row => (row.Index, row.Word, row.Outcome, row.Signature));

        Assert.Equal(words, singleRows.Select(row => row.Word));
        Assert.Equal(words, parallelRows.Select(row => row.Word));
        Assert.Equal(stableRows, parallelStableRows);

        var singleEvidence = ParseMorphEvidence.Read(singleOutput.MorphologyOutput!, words)
            .Select(row => JsonSerializer.Serialize(row with { ElapsedMs = 0 }, ParseMorphEvidence.JsonOptions));
        var parallelEvidence = ParseMorphEvidence.Read(parallelOutput.MorphologyOutput!, words)
            .Select(row => JsonSerializer.Serialize(row with { ElapsedMs = 0 }, ParseMorphEvidence.JsonOptions));
        Assert.Equal(singleEvidence, parallelEvidence);
    }

    // Measured with PanGloss 0.6.2 and 0.7.0: 16 parser threads reserve more address space than the ceiling.
    [RealParserFact]
    public async Task SixteenThreadBatchStartsUnderTheDefaultMemoryCeiling()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");

        string[] words = [SeededProject.FirstForm, SeededProject.SecondForm, "mofita"];
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(cache.ProjectId.Path, words, TimeSpan.FromSeconds(1)) { ThreadCount = 16 },
            "test:sixteen-thread-batch", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));

        Assert.True(outcome is PanGlossOutcome.Completed, outcome.Message);
        var completed = (PanGlossOutcome.Completed)outcome;
        Assert.Equal(words, BatchTsvParser.Parse(completed.Output).Select(row => row.Word));
    }
}
