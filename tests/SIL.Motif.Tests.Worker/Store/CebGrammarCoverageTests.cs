using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Corpus;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class CebGrammarCoverageTests
{
    internal const string SettingsVariable = "MOTIF_CEB_CORPUS_DRIVER";
    internal const string FreshMetricsVariable = "MOTIF_CEB_CORPUS_FRESH_METRICS";

    [Fact]
    public void WholeInventoryWitnessDetectsASwapThatPreservesCountsAndMembership()
    {
        var weights = new Dictionary<string, long>(StringComparer.Ordinal) { ["alpha"] = 2, ["beta"] = 7 };
        var provenance = new FrequencyProvenance("project", "source", "source-digest", "scope-digest", "weights",
            "tokenizer", "tokenizer-digest", "profile", false, "exact", "NFD", "en", new Dictionary<string, long>(), true);
        var swapped = GrammarCoverageInventory.FromManifest(new(provenance,
            [new(WordformId("en", "alpha"), "en", "alpha", "alpha", 7),
             new(WordformId("en", "beta"), "en", "beta", "beta", 2)]), "test-comparison", CancellationToken.None);

        Assert.Throws<Xunit.Sdk.EqualException>(() =>
            AssertPublishedWeights("en", weights, swapped.Rows.Sum(row => row.Frequency), swapped.Rows.Length, swapped.WeightDigest));
    }

    [CebGrammarCoverageFact]
    public void CebInventoryMatchesTheSourceAndUnboundLegacyAssessmentIsUnavailable()
    {
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Environment.GetEnvironmentVariable(SettingsVariable)!))!;
        var project = new ProjectLocator(settings.Fwdata, settings.StoreProjectIdentity);
        var originalFiles = HashSource(settings.Paratext);
        var assessmentStoreDigest = BatchInvocationEvidence.DigestFile(settings.AssessmentStore);
        Assert.Equal(settings.GrammarId, BatchInvocationEvidence.DigestFile(settings.Fwdata));
        var root = Directory.CreateTempSubdirectory("motif-ceb-grammar-coverage-").FullName;
        try
        {
            using var local = AmbientEnvironment.Push(new Dictionary<string, string?>
            {
                [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(root, "scripture-local.json"),
            });
            var pairedCopy = Path.Combine(root, Path.GetFileName(settings.AssessmentStore));
            var upgrade = MotifStoreUpgrade.Copy(settings.AssessmentStore, pairedCopy);
            Assert.True(upgrade.Carried["AssessedWords"] > 0);
            using var paired = MotifDatabase.OpenOwned(pairedCopy, project, MotifSchema.CurrentSchema, new Version(1, 0));
            using var profile = BaselineReadCache.Open(settings.Fwdata);
            var tokeniser = SourceApprovals.Tokeniser(profile.Cache);
            var writingSystem = profile.Cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
            var identity = ProgressSourceIdentity.Capture(settings.Fwdata)!;
            var entry = new SourceListEntry("CEBOCB", "CEB", SourceKind.Paratext, "GEN-REV", "English", null, null);
            ScriptureLocalStore.ForInstallation().SetSourcePath(ProjectWorkspaceKey.Compute(project), entry.Id, settings.Paratext);

            var watch = Stopwatch.StartNew();
            Assert.True(SourceIndexRefresh.Build(paired, entry, settings.Paratext, writingSystem.Id, identity, tokeniser));
            var indexSeconds = watch.Elapsed.TotalSeconds;
            var state = new SourceIndexRepository(paired).ReadState(entry.Id)!;
            var store = new GrammarCoverageStore(paired.FullPath, project);
            watch.Restart();
            var manifest = GrammarCoverageInventory.FromParatext(paired, settings.ProjectId, entry.Id, state.Generation,
                identity.SidecarDigest!, writingSystem, CancellationToken.None);
            var segmentation = store.Prepare(paired, manifest);
            var prepareSeconds = watch.Elapsed.TotalSeconds;

            var stream = StreamWeights(settings.Paratext, tokeniser);
            Assert.Equal(stream.Count, manifest.Rows.Count);
            Assert.True(manifest.Rows.All(row => row.WritingSystemId == writingSystem.Id && row.CasedForm is { } form &&
                row.Frequency == stream.GetValueOrDefault(form)), "CEB inventory membership or weights differ.");
            Assert.Equal(stream.Values.Sum(), manifest.Rows.Sum(row => row.Frequency));

            var assessment = new AssessmentRepository(paired).Get(settings.AssessmentId);
            var baseline = new BaselineRepository(paired).GetLatestCaptured(ProjectWorkspaceKey.Compute(project))!.Token;
            Assert.Equal(settings.Revision, baseline.SemanticSnapshotDigest);
            Assert.Equal(settings.BaselineId, baseline.BundleDigest);
            Assert.Null(assessment.Invocation!.InputProfile);
            var basis = new GrammarCoverageBasis(settings.ProjectId, settings.Revision, baseline, settings.GrammarId, settings.ParserId,
                GrammarCoverageAssessment.LimitsId(80000, new ParserWorkLimit(20000000), null),
                segmentation.Id, segmentation.MappingDigest);
            var refusal = Assert.Throws<GrammarCoverageReadException>(() => store.Build(paired, settings.AssessmentId, basis));
            Assert.Equal(GrammarCoverageReadFailure.Unavailable, refusal.Failure);

            Assert.True(originalFiles.SequenceEqual(HashSource(settings.Paratext)), "The Paratext Source changed.");
            Assert.Equal(settings.GrammarId, BatchInvocationEvidence.DigestFile(settings.Fwdata));
            Assert.Equal(assessmentStoreDigest, BatchInvocationEvidence.DigestFile(settings.AssessmentStore));
            File.WriteAllText(settings.Metrics, JsonSerializer.Serialize(new
            {
                indexSeconds, prepareSeconds, assessment = settings.AssessmentId, coverage = "unavailable",
                Occurrences = stream.Values.Sum(), Wordforms = stream.Count, SourceFiles = originalFiles.Count,
                state.Generation, segmentation.MappingDigest, segmentation.WeightDigest,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [FreshCebGrammarCoverageFact]
    public void FreshProfileBoundCebAssessmentPublishesCoverageAgainstTheInPlaceSource()
    {
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Environment.GetEnvironmentVariable(SettingsVariable)!))!;
        var sourceHashes = HashSource(settings.Paratext);
        var profileHash = WritingSystemProfileDigest.Read(settings.Fwdata);
        Assert.Equal(settings.GrammarId, BatchInvocationEvidence.DigestFile(settings.Fwdata));
        var root = Directory.CreateTempSubdirectory("motif-ceb-fresh-coverage-").FullName;
        try
        {
            using var local = AmbientEnvironment.Push(new Dictionary<string, string?>
            {
                [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(root, "scripture-local.json"),
            });
            var sourceProject = Path.GetDirectoryName(settings.Fwdata)!;
            var path = Path.Combine(root, Path.GetFileName(settings.Fwdata));
            File.Copy(settings.Fwdata, path);
            Directory.CreateDirectory(Path.Combine(root, "WritingSystemStore"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(sourceProject, "WritingSystemStore"), "*.ldml"))
                File.Copy(file, Path.Combine(root, "WritingSystemStore", Path.GetFileName(file)));
            Assert.Equal(profileHash, WritingSystemProfileDigest.Read(path));

            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            var entry = new SourceListEntry("CEBOCB", "CEB", SourceKind.Paratext, "GEN-REV", "English", null, null);
            ScriptureLocalStore.ForInstallation().SetSourcePath(ProjectWorkspaceKey.Compute(project), entry.Id, settings.Paratext);
            var watch = Stopwatch.StartNew();
            Dictionary<string, long> weights;
            using (var profile = BaselineReadCache.Open(path))
                weights = StreamWeights(settings.Paratext, SourceApprovals.Tokeniser(profile.Cache));
            var streamSeconds = watch.Elapsed.TotalSeconds;

            var parser = Path.Combine(BuildOutput.ProductDirectory, PanGlossExecutable.ExecutableFileName);
            using var release = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildOutput.ProductDirectory, "..", "..", "pangloss-release.json")));
            var releaseRoot = release.RootElement;
            var parserDigest = "sha256:" + releaseRoot.GetProperty("assets").GetProperty(RuntimeInformation.RuntimeIdentifier)
                .GetProperty("sha256").GetString();
            Assert.Equal(parserDigest, BatchInvocationEvidence.DigestFile(parser));
            var parserId = releaseRoot.GetProperty("version").GetString() + "/" + parserDigest;
            var request = new AssessRequest(path, new(false, [], weights.Keys.Order(StringComparer.Ordinal).ToArray(), false, null),
                PerWordLimitMs: 80000, PerWordParserWorkLimit: new ParserWorkLimit(20000000), Collect: ["ParseTime"]);
            watch.Restart();
            var run = AssessCommand.Assess(request, Path.Combine(root, "worker"), parser, null, CancellationToken.None);
            var assessSeconds = watch.Elapsed.TotalSeconds;
            Assert.True(run.Succeeded, run.Refusal?.Message);
            var assessmentId = Assert.Single(run.Value!.AssessmentIds);

            var store = new GrammarCoverageStore(project);
            using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
            var baseline = new BaselineRepository(database).GetLatestCaptured(ProjectWorkspaceKey.Compute(project))!;
            var assessment = new AssessmentRepository(database).Get(assessmentId);
            var invocation = assessment.Invocation!;
            Assert.Equal(parserId, GrammarCoverageAssessment.ParserId(invocation));
            Assert.Equal(settings.GrammarId, invocation.SourceBytesSha256);
            Assert.Equal(settings.ProjectId, baseline.Token.ProjectIdentity);
            var selection = Assert.IsType<AssessmentSubject.Words>(assessment.Subject).Selection;
            Assert.Equal(weights.Count, selection.Words.Count);
            Assert.True(selection.Words.All(weights.ContainsKey), "The Assessment Selection differs from the CEB Wordform stream.");

            using var capturedProfile = BaselineReadCache.Open(baseline.FwDataPath);
            var writingSystem = capturedProfile.Cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
            Assert.Equal(new BatchInputProfile(writingSystem.Id, profileHash, BatchInputProfile.WholeWordLowercase), invocation.InputProfile);
            var identity = ProgressSourceIdentity.Capture(path)!;
            watch.Restart();
            Assert.True(SourceIndexRefresh.Build(database, entry, settings.Paratext, writingSystem.Id, identity,
                SourceApprovals.Tokeniser(capturedProfile.Cache)));
            var indexSeconds = watch.Elapsed.TotalSeconds;
            var state = new SourceIndexRepository(database).ReadState(entry.Id)!;
            watch.Restart();
            var segmentation = store.PrepareParatext(database, baseline.Token.ProjectIdentity, entry.Id, state.Generation,
                profileHash, writingSystem);
            var prepareSeconds = watch.Elapsed.TotalSeconds;
            var basis = new GrammarCoverageBasis(baseline.Token.ProjectIdentity, baseline.Token.SemanticSnapshotDigest, baseline.Token,
                settings.GrammarId, parserId, GrammarCoverageAssessment.LimitsId(invocation), segmentation.Id, segmentation.MappingDigest,
                invocation.ImportedSnapshotSha256, segmentation.SourceGeneration);
            watch.Restart();
            var published = store.Build(database, assessmentId, basis);
            var aggregateSeconds = watch.Elapsed.TotalSeconds;

            watch.Restart();
            var summary = store.ReadSummary(published.Stamp.Id);
            var firstSummaryMs = watch.Elapsed.TotalMilliseconds;
            var driver = new GrammarCoverageReadDriver(project);
            watch.Restart();
            var first = driver.ReadNoAnalysis(summary.Stamp.Id);
            var firstPageMs = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(state.Generation, summary.Stamp.SourceGeneration);
            AssertPublishedWeights(writingSystem.Id, weights, summary.Aggregate.Occurrences,
                summary.Aggregate.DistinctWordforms, summary.WeightDigest);
            Assert.Equal(published.Stamp.Id, summary.Stamp.Id);
            var casing = LowercasePass.For(writingSystem);
            var checks = assessment.Words!.ToDictionary(word => word.Word.Normalize(NormalizationForm.FormD), StringComparer.Ordinal);
            var firstPassLimited = invocation.FirstPassGrammarWarningLines.Any(BatchInvocationEvidence.IsUnsupportedGrammarWarning);
            var limitedRetries = invocation.StoppedWordRetry is { Failure: null } retry &&
                (retry.RetryStandardErrorSha256 is null || retry.GrammarWarningLines.Any(BatchInvocationEvidence.IsUnsupportedGrammarWarning))
                    ? retry.ReplacedWords().Select(word => word.Normalize(NormalizationForm.FormD)).ToHashSet(StringComparer.Ordinal) : [];
            var results = weights.Select(weight => (Form: weight.Key, Weight: weight.Value,
                Result: IndependentResult(weight.Key, casing, checks, firstPassLimited, limitedRetries))).ToArray();
            Assert.Equal(results.Where(row => row.Result.HasReading == true).Sum(row => row.Weight), summary.ProvedOccurrences.Analysed);
            Assert.Equal(results.Where(row => row.Result.Competing == true).Sum(row => row.Weight), summary.ProvedOccurrences.Ambiguous);
            Assert.Equal(results.Where(row => row.Result.HasReading == false).Sum(row => row.Weight), summary.ProvedOccurrences.NoAnalysis);
            Assert.Equal(results.Where(row => row.Result.HasReading is null).Sum(row => row.Weight), summary.UnknownOccurrences.NoAnalysis);
            Assert.Equal(results.Where(row => row.Result.Competing is null).Sum(row => row.Weight), summary.UnknownOccurrences.Ambiguous);
            Assert.Equal(results.LongCount(row => row.Result.HasReading == true), summary.ProvedWordforms.Analysed);
            Assert.Equal(results.LongCount(row => row.Result.Competing == true), summary.ProvedWordforms.Ambiguous);
            Assert.Equal(results.LongCount(row => row.Result.HasReading == false), summary.ProvedWordforms.NoAnalysis);
            Assert.Equal(results.LongCount(row => row.Result.HasReading is null), summary.UnknownWordforms.NoAnalysis);
            Assert.Equal(results.LongCount(row => row.Result.Competing is null), summary.UnknownWordforms.Ambiguous);
            var expectedRank = results.Where(row => row.Result.HasReading == false)
                .Select(row => (Id: WordformId(writingSystem.Id, row.Form),
                    row.Form, row.Weight))
                .OrderByDescending(row => row.Weight).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedRank.LongLength, summary.RankedWordforms);
            var firstPage = ReadPage(first);
            var actualRank = new List<(string Id, string Form, long Weight)>();
            var page = firstPage;
            while (true)
            {
                Assert.Equal(summary.Stamp.Id, page.Stamp.SnapshotId);
                foreach (var row in page.Items)
                {
                    var word = row.GetProperty("wordform").Deserialize<WordformKey>(MotifJson.CreateHandAnalysisReadOptions())!;
                    Assert.Equal(writingSystem.Id, word.Ws);
                    actualRank.Add((row.GetProperty("wordformId").GetString()!, word.NfdCaseForm, row.GetProperty("frequency").GetInt64()));
                    Assert.True(actualRank.Count <= expectedRank.Length, "Ranked pages contain repeated or unexpected rows.");
                }
                if (page.NextCursor is not { } next) break;
                page = ReadPage(driver.ReadNoAnalysis(summary.Stamp.Id, cursor: next));
            }
            Assert.True(expectedRank.SequenceEqual(actualRank), "Ranked misses differ from the independent CEB stream tally.");
            var warmSummary = TimeReads(() => store.ReadSummary(summary.Stamp.Id));
            var warmFirst = TimeReads(() => driver.ReadNoAnalysis(summary.Stamp.Id));
            var warmSeek = firstPage.NextCursor is { } cursor
                ? TimeReads(() => driver.ReadNoAnalysis(summary.Stamp.Id, cursor: cursor)) : null;

            Assert.True(sourceHashes.SequenceEqual(HashSource(settings.Paratext)), "The Paratext Source changed.");
            Assert.Equal(settings.GrammarId, BatchInvocationEvidence.DigestFile(settings.Fwdata));
            Assert.Equal(profileHash, WritingSystemProfileDigest.Read(settings.Fwdata));
            using var process = Process.GetCurrentProcess();
            var wal = new FileInfo(database.FullPath + "-wal");
            File.WriteAllText(Environment.GetEnvironmentVariable(FreshMetricsVariable)!, JsonSerializer.Serialize(new
            {
                assessSeconds, streamSeconds, indexSeconds, prepareSeconds, aggregateSeconds, firstSummaryMs, firstPageMs,
                warmSummary, warmFirst, warmSeek, SourceFiles = sourceHashes.Count, assessmentId, parserId,
                SourceBytes = Directory.EnumerateFiles(settings.Paratext).Sum(file => new FileInfo(file).Length),
                StoreBytes = new FileInfo(database.FullPath).Length, WalBytes = wal.Exists ? wal.Length : 0,
                TestHostPeakRssBytes = process.PeakWorkingSet64,
                summary.Stamp, summary.AssessmentInputs, summary.MappingDigest, summary.WeightDigest, summary.Aggregate,
                summary.ProvedOccurrences, summary.ProvedWordforms, summary.UnknownOccurrences, summary.UnknownWordforms,
                summary.RankedWordforms, summary.Completion, summary.Limitations,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Page<JsonElement> ReadPage(JsonElement response) =>
        response.GetProperty("page").Deserialize<Page<JsonElement>>(MotifJson.CreateHandAnalysisReadOptions())!;

    private static (bool? HasReading, bool? Competing) IndependentResult(string form, LowercasePass casing,
        IReadOnlyDictionary<string, AssessedWord> checks, bool firstPassLimited, HashSet<string> limitedRetries)
    {
        var exact = Check(form);
        var lower = casing.LowercaseOf(form)?.Normalize(NormalizationForm.FormD);
        if (exact.HasReading == true || lower is null || lower == form) return exact;
        var fallback = Check(lower);
        if (fallback.HasReading == true)
            return (true, fallback.Competing == true || exact.HasReading == false ? fallback.Competing : null);
        return exact.HasReading == false && fallback.HasReading == false ? (false, false) : (null, null);

        (bool? HasReading, bool? Competing) Check(string key)
        {
            if (!checks.TryGetValue(key, out var word) || word.Morphology is not { } evidence ||
                evidence.InvalidShape || word.Outcome == "skipped")
                return (null, null);
            var native = evidence.Analyses.Where(reading => reading.Morphs.Count > 0 &&
                reading.Morphs.All(morph => morph.GuessedString is null)).ToArray();
            var complete = !firstPassLimited && !limitedRetries.Contains(key) &&
                !word.IsIncomplete && !evidence.Capped && !evidence.TimedOut &&
                evidence.Refusal is null && evidence.Unavailable.Count == 0;
            if (native.Length == 0)
                return complete && word.Outcome == "no-analysis" && evidence.Analyses.Count == 0 ? (false, false) : (null, null);
            var mapped = native.Where(reading => reading.Morphs.All(morph =>
                    Guid.TryParse(morph.Form, out _) && Guid.TryParse(morph.Msa, out _) &&
                    (morph.InflType is null || Guid.TryParse(morph.InflType, out _))))
                .Select(reading => string.Join("|", reading.Morphs.Select(morph => $"{morph.Form}/{morph.Msa}/{morph.InflType}")))
                .Distinct(StringComparer.Ordinal).ToArray();
            return (true, mapped.Length > 1 ? true : complete && word.Outcome == "analysed" &&
                native.Length == 1 && mapped.Length == 1 ? false : null);
        }
    }

    private static Dictionary<string, long> StreamWeights(string folder, IWordTokeniser tokeniser)
    {
        var weights = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var chapter in ParatextPassages.Read(folder))
        foreach (var verse in chapter.Verses)
        foreach (var token in tokeniser.Tokenise(verse.Text))
        {
            var form = token.Normalize(NormalizationForm.FormD);
            weights[form] = weights.GetValueOrDefault(form) + 1;
        }
        return weights;
    }

    private static ReadLatency TimeReads(Action read)
    {
        const int samples = 100;
        var times = new double[samples];
        for (var i = 0; i < times.Length; i++)
        {
            var watch = Stopwatch.StartNew();
            read();
            times[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        return new(samples, times[49], times[94]);
    }

    private sealed record ReadLatency(int Samples, double P50Ms, double P95Ms);

    private static void AssertPublishedWeights(string writingSystem, Dictionary<string, long> weights,
        long occurrences, long wordforms, string weightDigest)
    {
        Assert.Equal(weights.Values.Sum(), occurrences);
        Assert.Equal(weights.Count, wordforms);
        var expectedWeights = weights.Select(pair => new { WordformId = WordformId(writingSystem, pair.Key), Frequency = pair.Value })
            .OrderBy(row => row.WordformId, StringComparer.Ordinal);
        Assert.Equal(AssessmentMaterial.Digest(JsonSerializer.Serialize(expectedWeights)), weightDigest);
    }

    private static string WordformId(string writingSystem, string form) =>
        AssessmentMaterial.Digest(JsonSerializer.Serialize(new { writingSystem, form }));

    private static List<string> HashSource(string folder) => Directory.EnumerateFiles(folder)
        .Order(StringComparer.Ordinal).Select(file => Path.GetFileName(file) + ":" + BatchInvocationEvidence.DigestFile(file)).ToList();

    private sealed record Settings(string Paratext, string Fwdata, string AssessmentStore, string AssessmentId, string ProjectId,
        string Revision, string GrammarId, string ParserId, string Metrics, string StoreProjectIdentity, string BaselineId);
}

internal sealed class FreshCebGrammarCoverageFactAttribute : FactAttribute
{
    public FreshCebGrammarCoverageFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CebGrammarCoverageTests.SettingsVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CebGrammarCoverageTests.FreshMetricsVariable)))
            Skip = "Set MOTIF_CEB_CORPUS_DRIVER and MOTIF_CEB_CORPUS_FRESH_METRICS to measure a disposable copy of the CEB project.";
    }
}

internal sealed class CebGrammarCoverageFactAttribute : FactAttribute
{
    public CebGrammarCoverageFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CebGrammarCoverageTests.SettingsVariable)))
            Skip = "Set MOTIF_CEB_CORPUS_DRIVER to a configuration with the read-only CEB Source and saved Assessment.";
    }
}
