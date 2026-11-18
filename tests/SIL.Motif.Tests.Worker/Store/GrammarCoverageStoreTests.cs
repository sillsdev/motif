using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Sources;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Corpus;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class GrammarCoverageStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-grammar-coverage-").FullName;

    [Fact]
    public void WeightedAggregateMatchesAnIndependentStreamAndRanksTiesById()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest();
        var segmentation = store.Prepare(database, manifest);
        var basis = Save(database, segmentation, "before", "parsed");
        var summary = store.Build(database, "before", basis);

        var occurrenceOutcomes = manifest.Rows.SelectMany(row => Enumerable.Repeat(row.WordformId, (int)row.Frequency));
        var outcomes = occurrenceOutcomes.ToLookup(id => id switch
        {
            "01" or "02" => "miss",
            "03" => "success",
            "04" => "ambiguous",
            "08" => "ambiguous-stopped",
            "05" => "stopped",
            "06" => "capped",
            _ => "missing",
        });

        Assert.Equal(outcomes.Sum(group => group.Count()), summary.Aggregate.Occurrences);
        Assert.Equal(outcomes["success"].LongCount() + outcomes["ambiguous"].LongCount() +
            outcomes["ambiguous-stopped"].LongCount(), summary.ProvedOccurrences.Analysed);
        Assert.Equal(outcomes["ambiguous"].LongCount() + outcomes["ambiguous-stopped"].LongCount(),
            summary.ProvedOccurrences.Ambiguous);
        Assert.Equal(outcomes["miss"].LongCount(), summary.ProvedOccurrences.NoAnalysis);
        Assert.Equal(2, summary.ProvedWordforms.NoAnalysis);
        Assert.Equal(2, summary.ProvedWordforms.Ambiguous);
        Assert.Equal(9, summary.Aggregate.ExcludedOccurrences);
        Assert.Equal(outcomes["stopped"].LongCount() + outcomes["capped"].LongCount() +
            outcomes["missing"].LongCount() + outcomes["ambiguous-stopped"].LongCount(),
            summary.Aggregate.OccurrenceBuckets.Unmeasured);
        Assert.Null(summary.Aggregate.OccurrenceBuckets.NoAnalysis);
        Assert.Null(summary.Aggregate.OccurrenceBuckets.Misspelled);
        Assert.Null(summary.Aggregate.OccurrenceBuckets.Disposition);
        Assert.Null(summary.Aggregate.Open);
        Assert.Null(summary.Aggregate.ApprovedLost);
        Assert.Equal(9, summary.UnknownOccurrences.Analysed);
        Assert.Equal(9, summary.UnknownOccurrences.NoAnalysis);
        Assert.Equal(9, summary.UnknownOccurrences.Ambiguous);
        Assert.Equal(4, summary.UnknownOccurrences.InvalidShape);
        Assert.Equal(summary.Aggregate.Occurrences, summary.UnknownOccurrences.ApprovedLost);
        Assert.Contains(summary.Limitations, item => item is { Code: "capped-check", Count: 2 });
        Assert.Contains(summary.Limitations, item => item is { Code: "stopped-check", Count: 4 });
        Assert.Contains(summary.Limitations, item => item is { Code: "missing-check", Count: 4 });

        var first = Store().ReadNoAnalysis(summary.Stamp.Id, new(1));
        Assert.Equal("01", Assert.Single(first.Page.Items).WordformId);
        Assert.Equal("zulu", first.Page.Items[0].Wordform.NfdCaseForm);
        var second = Store().ReadNoAnalysis(summary.Stamp.Id, new(1, first.Page.NextCursor));
        Assert.Equal("02", Assert.Single(second.Page.Items).WordformId);
        Assert.Null(second.Page.NextCursor);
    }

    [Fact]
    public void ANewAssessmentReusesTheFrequencyInventoryAndKeepsTheOldPin()
    {
        using var database = Open();
        var manifest = Manifest();
        var store = Store();
        var segmentation = store.Prepare(database, manifest);
        var before = store.Build(database, "before", Save(database, segmentation, "before", "parsed"));
        var after = Store().Build(database, "after", Save(database, segmentation, "after", "zulu"));

        Assert.Equal(before.Stamp.SegmentationId, after.Stamp.SegmentationId);
        Assert.Equal(before.WeightDigest, after.WeightDigest);
        Assert.Equal(before.MappingDigest, after.MappingDigest);
        Assert.Equal(before.Aggregate.Occurrences, after.Aggregate.Occurrences);
        Assert.Equal(before.Aggregate.DistinctWordforms, after.Aggregate.DistinctWordforms);
        Assert.NotEqual(before.Stamp.Id, after.Stamp.Id);
        Assert.Equal(before.ProvedOccurrences.Analysed + 5, after.ProvedOccurrences.Analysed);
        Assert.Equal(before.ProvedOccurrences.NoAnalysis - 5, after.ProvedOccurrences.NoAnalysis);
        Assert.Equal(new[] { "01", "02" }, Store().ReadNoAnalysis(before.Stamp.Id, new()).Page.Items.Select(row => row.WordformId));
        Assert.Equal("02", Assert.Single(Store().ReadNoAnalysis(after.Stamp.Id, new()).Page.Items).WordformId);
        var cursor = Store().ReadNoAnalysis(before.Stamp.Id, new(1)).Page.NextCursor;
        Assert.Equal(GrammarCoverageReadFailure.InvalidCursor,
            Assert.Throws<GrammarCoverageReadException>(() => Store().ReadNoAnalysis(after.Stamp.Id, new(1, cursor))).Failure);
    }

    [Fact]
    public void IncompatibleBasisAndMappingAreRefused()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest();
        var basis = Save(database, store.Prepare(database, manifest), "before", "parsed");
        Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with { GrammarId = "other" }));
        Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with { MappingDigest = "other" }));
        Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with { ParserId = "other" }));
        Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with { LimitsId = "other" }));
        Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with { ImportedSnapshotDigest = "other" }));
        var anotherBaseline = new BaselineToken(ProjectId, AssessmentMaterial.Digest("other"), "1",
            "2020-01-02T00:00:00Z", AssessmentMaterial.Digest("other bundle"));
        Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with
        {
            Baseline = anotherBaseline, Revision = anotherBaseline.SemanticSnapshotDigest,
        }));

        var other = store.Prepare(database, manifest with
        {
            Rows = manifest.Rows.Select(row => row.WordformId == "03" ? row with { FallbackParserKey = "unexpected" } : row).ToArray(),
        });
        Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with
        {
            SegmentationId = other.Id, MappingDigest = other.MappingDigest,
        }));
    }

    // Share-read-write open: Windows blocks an exclusive read while a live connection holds the store.
    private static byte[] ReadSharedBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void ReadsKeepDatabaseAndWalBytesAndRefuseMissingStorage()
    {
        var store = Store();
        var driver = new GrammarCoverageReadDriver(Project);
        Assert.Equal("corpus.index-unavailable", driver.ReadSummary("missing").GetProperty("code").GetString());
        Assert.Equal("corpus.index-unavailable", driver.ReadNoAnalysis("missing").GetProperty("code").GetString());
        Assert.False(File.Exists(CoveragePath));

        using var database = Open();
        var segmentation = store.Prepare(database, Manifest());
        using var holdWal = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = CoveragePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        holdWal.Open();
        var basis = Save(database, segmentation, "before", "parsed");
        var summary = store.Build(database, "before", basis);
        using (var settle = holdWal.CreateCommand())
        {
            // Backfill every frame now, so no later checkpoint by these writer connections can move the header counter.
            settle.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
            settle.ExecuteNonQuery();
        }

        var main = ReadSharedBytes(CoveragePath);
        var wal = ReadSharedBytes(CoveragePath + "-wal");
        Assert.NotEmpty(wal);

        using var writerOwnership = MotifSqliteStore.AcquireOwnershipForTesting(CoveragePath, TimeSpan.Zero);
        var wire = driver.ReadSummary(summary.Stamp.Id);
        Assert.Equal(summary.Stamp.Id, wire.GetProperty("stamp").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("stamp").GetProperty("judgementRevision").ValueKind);
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("aggregate").GetProperty("approvedLost").ValueKind);
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("aggregate").GetProperty("open").ValueKind);
        var response = driver.ReadNoAnalysis(summary.Stamp.Id, limit: 1);
        Assert.Equal(2, response.GetProperty("rankedWordforms").GetInt64());
        Assert.Equal(summary.Stamp.Id, response.GetProperty("stamp").GetProperty("id").GetString());
        Assert.Equal(basis.Baseline.BundleDigest, response.GetProperty("stamp").GetProperty("baseline").GetString());
        var page = response.GetProperty("page");
        var shared = page.Deserialize<Page<JsonElement>>(MotifJson.CreateHandAnalysisReadOptions())!;
        Assert.Equal(summary.Stamp.Id, shared.Stamp.SnapshotId);
        Assert.Equal(Guid.Parse(basis.ProjectId), shared.Stamp.ProjectId);
        Assert.Equal(basis.Revision, shared.Stamp.HomePoint);
        Assert.Null(shared.Total);
        Assert.False(shared.Complete);
        Assert.Contains(shared.Limitations, item => item.Code == "missing-check" && item.AffectedIds.IsEmpty &&
            !string.IsNullOrWhiteSpace(item.Reason));
        Assert.Equal(4, response.GetProperty("limitationWeights").GetProperty("missing-check").GetInt64());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("total").ValueKind);
        Assert.Equal("corpus.revision-unavailable", driver.ReadNoAnalysis("missing").GetProperty("code").GetString());
        var row = Assert.Single(shared.Items);
        Assert.Equal(new WordformKey("ws", "zulu"),
            row.GetProperty("wordform").Deserialize<WordformKey>(MotifJson.CreateHandAnalysisReadOptions()));
        Assert.Equal(5, row.GetProperty("buckets").GetProperty("no-analysis").GetInt64());
        Assert.All(row.GetProperty("limitations").EnumerateArray(),
            item => Assert.NotNull(item.Deserialize<ReadLimitation>(MotifJson.CreateHandAnalysisReadOptions())));
        Assert.Contains(wire.GetProperty("limitations").EnumerateArray(), item =>
            item.Deserialize<ReadLimitation>(MotifJson.CreateHandAnalysisReadOptions())?.Code == "missing-check");
        Assert.Equal(4, wire.GetProperty("limitationWeights").GetProperty("missing-check").GetInt64());
        Assert.Equal("02", driver.ReadNoAnalysis(summary.Stamp.Id, cursor: shared.NextCursor)
            .GetProperty("page").GetProperty("items")[0].GetProperty("wordformId").GetString());

        Assert.Equal(main, ReadSharedBytes(CoveragePath));
        Assert.Equal(wal, ReadSharedBytes(CoveragePath + "-wal"));
        Assert.False(File.Exists(CoveragePath + ".writer.lock"));
    }

    [Fact]
    public void PrivateAndMismappedFrequencyManifestsAreRefusedBeforeStorage()
    {
        using var database = Open();
        var manifest = Manifest();
        Assert.Throws<InvalidDataException>(() => Store().Prepare(database, manifest with
        {
            Provenance = manifest.Provenance with { AllowsSurfaceStorage = false },
        }));
        Assert.Throws<InvalidDataException>(() => Store().Prepare(database, manifest with
        {
            Rows = [manifest.Rows[0] with { ParserKey = "alpha" }],
        }));
        using var connection = database.OpenConnection();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM GrammarCoverageSegmentations;";
        Assert.Equal(0L, query.ExecuteScalar());
    }

    [Fact]
    public void ProjectStoreOwnsThePreparedInventory()
    {
        using var database = Open();
        new GrammarCoverageStore(Project).Prepare(database, Manifest());
        using var connection = database.OpenConnection();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM GrammarCoverageSegmentations;";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public void IncompletePageHasNoExactTotal()
    {
        using var database = Open();
        var store = Store();
        var summary = store.Build(database, "before", Save(database, store.Prepare(database, Manifest()), "before", "parsed"));

        Assert.Equal(2, summary.RankedWordforms);
        Assert.Null(store.ReadNoAnalysis(summary.Stamp.Id, new(1)).Page.Total);
        Assert.Null(store.ReadNoAnalysis(summary.Stamp.Id, new()).Page.Total);
    }

    [Fact]
    public void DetachedPageIsStampedAndRejectsAnInventedRank()
    {
        using var database = Open();
        var store = Store();
        var summary = store.Build(database, "before", Save(database, store.Prepare(database, Manifest()), "before", "parsed"));
        var page = store.ReadNoAnalysis(summary.Stamp.Id, new(1));
        var state = System.Text.Json.Nodes.JsonNode.Parse(Convert.FromBase64String(page.Page.NextCursor!))!.AsObject();
        foreach (var (property, invalid) in new (string, System.Text.Json.Nodes.JsonNode?)[]
        {
            ("WordformId", "00"), ("Version", 2), ("Schema", 2), ("Query", "analysed"), ("Order", "id"),
            ("Scope", "other"), ("StampId", "other"),
        })
        {
            var modified = state.DeepClone();
            modified[property] = invalid;
            var forged = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(modified));
            Assert.Equal(GrammarCoverageReadFailure.InvalidCursor, Assert.Throws<GrammarCoverageReadException>(
                () => store.ReadNoAnalysis(summary.Stamp.Id, new(1, forged))).Failure);
        }
        var detached = JsonSerializer.SerializeToElement(page);
        Assert.Equal(summary.Stamp.Id, detached.GetProperty("Stamp").GetProperty("Id").GetString());
        Assert.Equal("01", Assert.Single(page.Page.Items).WordformId);
    }

    [Fact]
    public void PreparedMappingCannotRelabelAnAssessmentWithAnotherLdmlProfile()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest();
        var basis = Save(database, store.Prepare(database, manifest), "before", "parsed");
        foreach (var provenance in new[]
        {
            manifest.Provenance with { ProfileHash = "other-ldml" },
            manifest.Provenance with { WritingSystemId = "other-ws" },
        })
        {
            var relabelled = store.Prepare(database, manifest with { Provenance = provenance });
            Assert.Throws<InvalidDataException>(() => store.Build(database, "before", basis with
            {
                SegmentationId = relabelled.Id, MappingDigest = relabelled.MappingDigest,
            }));
        }
    }

    [Fact]
    public void ASelectionSupersetCanSupplyLowercaseEvidenceFromAnotherOrigin()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest() with { Rows = [new("01", "ws", "Dog", "Dog", 5, "dog")] };
        var basis = Save(database, store.Prepare(database, manifest), "before", "dog",
            ["DOG", "Dog"], [new("dog", "analysed", [new(null, [], -1, "a")]) { LowercaseOf = "DOG" }],
            expectedGeneratedKeys: ["dog"]);

        Assert.Equal(5, store.Build(database, "before", basis).ProvedOccurrences.Analysed);
    }

    [Fact]
    public void FoldedPrimaryMustBeTheWordformsOwnLowercaseKey()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest() with
        {
            Provenance = Manifest().Provenance with { Fold = true },
            Rows = [new("01", "ws", "Dog", "cat", 5)],
        };
        var invalid = store.Prepare(database, manifest);
        var basis = Save(database, invalid, "folded", "none", ["cat", "dog"]);

        Assert.Throws<InvalidDataException>(() => store.Build(database, "folded", basis));

        var valid = store.Prepare(database, manifest with { Rows = [new("01", "ws", "Dog", "dog", 5)] });
        var summary = store.Build(database, "folded", basis with
        {
            SegmentationId = valid.Id, MappingDigest = valid.MappingDigest,
        });
        var miss = Assert.Single(store.ReadNoAnalysis(summary.Stamp.Id, new()).Page.Items);
        Assert.Equal("Dog", miss.Wordform.NfdCaseForm);
        Assert.Equal(5, miss.Frequency);
    }

    [Fact]
    public void IdenticalLegacyInputsCannotEstablishWhichWritingSystemProducedThem()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest();
        var basis = Save(database, store.Prepare(database, manifest), "legacy", "none", ["dog"], writingSystem: null);
        var saved = new AssessmentRepository(database).Get("legacy");
        Assert.Null(saved.Invocation!.InputProfile);
        foreach (var writingSystem in new[] { "ws", "other-ws" })
        {
            var candidate = store.Prepare(database, manifest with
            {
                Provenance = manifest.Provenance with { WritingSystemId = writingSystem },
                Rows = [new("01", writingSystem, "dog", "dog", 5)],
            });
            var requested = basis with
            {
                SegmentationId = candidate.Id, MappingDigest = candidate.MappingDigest,
            };
            var refusal = Assert.Throws<GrammarCoverageReadException>(() => store.Build(database, "legacy", requested));
            Assert.Equal(GrammarCoverageReadFailure.Unavailable, refusal.Failure);
        }
    }

    [Theory]
    [InlineData("capability: an affix rule was omitted")]
    [InlineData("warning: an affix cannot be loaded as an affix rule")]
    public void UnsupportedInvocationKeepsProvedPathsAndLeavesAbsenceUnknown(string warning)
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest() with { Rows = [new("01", "ws", "dog", "dog", 5), new("02", "ws", "É", "É", 7),
            new("03", "ws", "cat", "cat", 3)] };
        var basis = Save(database, store.Prepare(database, manifest), "limited", "dog", ["dog", "É", "cat"],
            expectedGeneratedKeys: ["e\u0301"], warning: warning);
        var summary = store.Build(database, "limited", basis);

        Assert.Equal(12, summary.ProvedOccurrences.Analysed);
        Assert.Equal(7, summary.ProvedOccurrences.Ambiguous);
        Assert.Null(summary.Aggregate.OccurrenceBuckets.Ambiguous);
        Assert.Equal(8, summary.UnknownOccurrences.Ambiguous);
        Assert.Equal(3, summary.UnknownOccurrences.NoAnalysis);
        Assert.Null(summary.Aggregate.OccurrenceBuckets.NoAnalysis);
        Assert.Contains(summary.Limitations, item => item is { Code: "unsupported-grammar", Count: 15 });
        Assert.Empty(store.ReadNoAnalysis(summary.Stamp.Id, new()).Page.Items);
    }

    [Fact]
    public void InvalidShapeAndUnavailableSearchesCannotProveAMiss()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest() with { Rows = [new("01", "ws", "shape", "shape", 5), new("02", "ws", "warning", "warning", 7)] };
        var basis = Save(database, store.Prepare(database, manifest), "before", "",
            ["shape", "warning"],
            [
                new("shape", "skipped", []) { Morphology = new("test", 0, "shape", 0, false, false, true, [], []) },
                new("warning", "no-analysis", []) { Morphology = new("test", 1, "warning", 0, false, false, false, [], ["unsupported grammar"]) },
            ]);

        var summary = store.Build(database, "before", basis);
        Assert.Equal(0, summary.RankedWordforms);
        Assert.Equal(12, summary.Aggregate.Occurrences);
        Assert.Equal(5, summary.Aggregate.InvalidShapeOccurrences);
        Assert.Equal(5, summary.ProvedInvalidShapeOccurrences);
        Assert.Equal(12, summary.UnknownOccurrences.NoAnalysis);
        Assert.Contains(summary.Limitations, item => item is { Code: "check-evidence-unavailable", Count: 7 });
    }

    [Fact]
    public void ExactPageHasAnExactTotalAcrossContinuations()
    {
        using var database = Open();
        var store = Store();
        var segmentation = store.Prepare(database, Manifest() with { Rows = Manifest().Rows.Take(2).ToArray() });
        var basis = Save(database, segmentation, "exact", "", ["zulu", "alpha"]);
        var summary = store.Build(database, "exact", basis);

        var page = store.ReadNoAnalysis(summary.Stamp.Id, new(1));
        Assert.Equal(2, page.Page.Total);
        Assert.True(page.Page.Complete);
        Assert.NotNull(page.Page.NextCursor);
        var tail = store.ReadNoAnalysis(summary.Stamp.Id, new(1, page.Page.NextCursor));
        Assert.Equal(2, tail.Page.Total);
        Assert.True(tail.Page.Complete);
        Assert.Null(tail.Page.NextCursor);
        Assert.Null(summary.Aggregate.ReachOccurrences.AnyAccepted);
    }

    [Fact]
    public void ExclusiveUseLeaseReturnsBusyAndWrongProjectIsRefused()
    {
        using (Open()) { }
        using (var lease = new FileStream(CoveragePath + ".use.lock", FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var driver = new GrammarCoverageReadDriver(Project);
            Assert.Equal("project.busy", driver.ReadSummary("stamp").GetProperty("code").GetString());
        }

        var wrong = Project with { FieldWorksProjectIdentity = "some other project" };
        var refusal = new GrammarCoverageReadDriver(wrong).ReadSummary("stamp");
        Assert.Equal("corpus.index-unavailable", refusal.GetProperty("code").GetString());
        using var database = Open();
        Assert.Throws<InvalidDataException>(() => new GrammarCoverageStore(wrong).Prepare(database, Manifest()));
    }

    [Fact]
    public void AbsentMainFileDuringUpgradeStillReturnsBusy()
    {
        using (Open()) { }
        using var lease = new FileStream(CoveragePath + ".use.lock", FileMode.Open, FileAccess.Read, FileShare.None);
        File.Move(CoveragePath, CoveragePath + ".upgrading");

        var refusal = new GrammarCoverageReadDriver(Project).ReadSummary("stamp");

        Assert.Equal("project.busy", refusal.GetProperty("code").GetString());
    }

    [Fact]
    public void ParatextStampDistinguishesGenerationsAndVersificationWithIdenticalPassages()
    {
        using var database = Open();
        var store = Store();
        var sources = new SourceIndexRepository(database);
        using var cache = NewLangProjFixture.CreateCache(_root);
        var writingSystem = cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem;
        var scope = new SourceIndexIdentity(_root, "MAT", "English", nameof(SourceStatus.Active), writingSystem.Id,
            new ProgressSourceIdentity("project-digest", []) { SidecarDigest = ProfileHash }.Serialize(), []);
        const string id = "paratext";
        const string passage = "MAT 1";
        var state = new SourceIndexState(id, nameof(SourceKind.Paratext), "gen-1", "same-content", scope.Serialize(), "ready",
            ParatextPassages.TokenizerVersion, ParatextPassages.AnchorVersion);
        bool Publish(SourceIndexState generation, string? previous = null) =>
            sources.Publish(generation, [new(passage, passage, "hash", 1, 1)], [new("word", writingSystem.Id, "dog", "dog")],
                [new(passage, 1, "MAT 1:1#1", "MAT 1:1", "dog", "word")], previous);

        Assert.True(Publish(state));
        var first = store.PrepareParatext(database, ProjectId, id, state.Generation, ProfileHash, writingSystem);
        var basis = Save(database, first, "paratext", "dog", ["dog"], writingSystem: writingSystem.Id) with
        {
            SourceGeneration = first.SourceGeneration,
        };
        var before = store.Build(database, "paratext", basis);
        var later = state with { Generation = "gen-2" };
        Assert.True(Publish(later, state.Generation));
        var second = store.PrepareParatext(database, ProjectId, id, later.Generation, ProfileHash, writingSystem);
        var otherEdition = later with { Generation = "gen-3", IdentityJson = (scope with { Versification = "Original" }).Serialize() };
        Assert.True(Publish(otherEdition, later.Generation));
        var third = store.PrepareParatext(database, ProjectId, id, otherEdition.Generation, ProfileHash, writingSystem);
        var repeated = store.Build(database, "paratext", basis with { SourceGeneration = second.SourceGeneration });
        var after = store.Build(database, "paratext", basis with
        {
            SegmentationId = third.Id,
            SourceGeneration = third.SourceGeneration,
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.MappingDigest, second.MappingDigest);
        Assert.Equal(first.WeightDigest, second.WeightDigest);
        Assert.Equal("gen-2", repeated.Stamp.SourceGeneration);
        Assert.Equal(before.Stamp.ScopeDigest, repeated.Stamp.ScopeDigest);
        Assert.Equal(before.Stamp.SourceDigest, repeated.Stamp.SourceDigest);
        Assert.NotEqual(before.Stamp.Id, repeated.Stamp.Id);
        Assert.Throws<InvalidDataException>(() => store.Build(database, "paratext", basis with { SourceGeneration = "gen-3" }));
        Assert.NotEqual(second.Id, third.Id);
        Assert.Equal(first.WeightDigest, third.WeightDigest);
        Assert.Equal(before.Provenance.ParatextScope!.PassageDigest, after.Provenance.ParatextScope!.PassageDigest);
        Assert.NotEqual(before.Stamp.SourceDigest, after.Stamp.SourceDigest);
        Assert.NotEqual(before.Stamp.ScopeDigest, after.Stamp.ScopeDigest);
        Assert.NotEqual(before.Stamp.Id, after.Stamp.Id);
        var saved = store.ReadSummary(before.Stamp.Id);
        Assert.Equal("gen-1", saved.Stamp.SourceGeneration);
        Assert.Equal("gen-3", after.Stamp.SourceGeneration);
        Assert.Equal("English", saved.Provenance.ParatextScope!.Versification);
        Assert.Equal("PARATEXT", saved.Stamp.SourceId);
        Assert.Equal("Original", after.Provenance.ParatextScope!.Versification);
        Assert.Equal("MAT", after.Provenance.ParatextScope!.Context);
    }

    [Fact]
    public void SameIdWithAnotherCasingDefinitionCannotPoisonTheParatextInventory()
    {
        using var database = Open();
        var verified = new CoreWritingSystemDefinition("en") { Id = "en" };
        var mismatched = new CoreWritingSystemDefinition(verified, cloneId: true) { CaseAlias = "tr" };
        var store = new GrammarCoverageStore(CoveragePath, Project, (_, _) => LowercasePass.For(verified));
        var identity = new SourceIndexIdentity(_root, "MAT", "English", nameof(SourceStatus.Active), verified.Id,
            new ProgressSourceIdentity("project-digest", []) { SidecarDigest = ProfileHash }.Serialize(), []);
        const string source = "paratext";
        const string passage = "MAT 1";
        const string generation = "gen-1";
        var state = new SourceIndexState(source, nameof(SourceKind.Paratext), generation, "same-content", identity.Serialize(),
            "ready", ParatextPassages.TokenizerVersion, ParatextPassages.AnchorVersion);
        Assert.True(new SourceIndexRepository(database).Publish(state, [new(passage, passage, "hash", 1, 1)],
            [new("word", "en", "i", "i")], [new(passage, 1, "MAT 1:1#1", "MAT 1:1", "I", "word")]));

        var wrong = store.PrepareParatext(database, ProjectId, source, generation, ProfileHash, mismatched);
        var correct = store.PrepareParatext(database, ProjectId, source, generation, ProfileHash, verified);

        Assert.NotEqual(wrong.Id, correct.Id);
        Assert.NotEqual(wrong.MappingDigest, correct.MappingDigest);
        Assert.Equal(wrong.WeightDigest, correct.WeightDigest);
        var basis = Save(database, correct, "paratext-case", "i", ["I", "i"], writingSystem: "en")
            with { SourceGeneration = generation };
        Assert.Throws<InvalidDataException>(() =>
            store.Build(database, "paratext-case", basis with { SegmentationId = wrong.Id, MappingDigest = wrong.MappingDigest }));
        Assert.Equal(1, store.Build(database, "paratext-case", basis).ProvedOccurrences.Analysed);
    }

    [Fact]
    public void PublishedComparisonVersionRecordsTheProducerBasisAndSurvivesAnotherPublication()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest();
        var first = store.Prepare(database, manifest);
        var basis = Save(database, first, "comparison", "parsed");
        var before = store.Build(database, "comparison", basis);
        var firstPage = store.ReadNoAnalysis(before.Stamp.Id, new()).Page.Stamp;
        var nextProducer = new GrammarCoverageStore(CoveragePath, Project, (_, _) => LowercasePass.For(new("en")),
            readIcuVersion: () => "999.0");
        var second = nextProducer.Prepare(database, manifest);
        var after = nextProducer.Build(database, "comparison",
            basis with { SegmentationId = second.Id, MappingDigest = second.MappingDigest });
        var decomposed = nextProducer.Prepare(database,
            manifest with { Provenance = manifest.Provenance with { FormNormalization = "NFD" } });
        var fromNfd = nextProducer.Build(database, "comparison",
            basis with { SegmentationId = decomposed.Id, MappingDigest = decomposed.MappingDigest });

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.MappingDigest, second.MappingDigest);
        Assert.Equal(first.WeightDigest, second.WeightDigest);
        Assert.NotEqual(before.Stamp.Id, after.Stamp.Id);
        Assert.NotEqual(after.Stamp.Id, fromNfd.Stamp.Id);
        Assert.Throws<InvalidDataException>(() => nextProducer.Build(database, "comparison", basis));
        Assert.Equal(firstPage, nextProducer.ReadNoAnalysis(before.Stamp.Id, new()).Page.Stamp);
        Assert.Equal($"nfc-to-nfd/icu-{Icu.Wrapper.IcuVersion}/ordinal/frequency-desc,wordform-id-asc", firstPage.ComparisonVersion);
        Assert.Equal("nfc-to-nfd/icu-999.0/ordinal/frequency-desc,wordform-id-asc",
            nextProducer.ReadNoAnalysis(after.Stamp.Id, new()).Page.Stamp.ComparisonVersion);
        Assert.Equal("nfd-to-nfd/icu-999.0/ordinal/frequency-desc,wordform-id-asc",
            nextProducer.ReadNoAnalysis(fromNfd.Stamp.Id, new()).Page.Stamp.ComparisonVersion);
    }

    [Fact]
    public void Schema76UpgradeKeepsAssessmentInputsAndRequiresANewParatextGeneration()
    {
        GrammarCoverageBasis oldBasis;
        using (var database = Open())
        {
            oldBasis = Save(database, new("", "", ""), "old-assessment", "parsed");
            var saved = new AssessmentRepository(database).Get("old-assessment");
            Assert.Equal(new BatchInputProfile("ws", ProfileHash, BatchInputProfile.WholeWordLowercase),
                saved.Invocation!.InputProfile);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json_type(EvidenceJson, '$.InputProfile') IS NULL FROM AssessmentInvocations;";
            Assert.Equal(1L, command.ExecuteScalar());
            command.CommandText = """
                INSERT INTO SourceIndexState VALUES ('source', 'Paratext', 'old-pin', 'digest', '{}', 'ready',
                    'fieldworks-word-forming-1', 'scripture-1');
                DROP TABLE GrammarCoverageNoAnalysisRank;
                DROP TABLE GrammarCoverageMeasurements;
                DROP TABLE GrammarCoverageSegmentations;
                ALTER TABLE AssessmentInvocations DROP COLUMN InputProfileJson;
                PRAGMA user_version = 76;
                """;
            command.ExecuteNonQuery();
        }

        using var upgraded = Open();
        Assert.True(File.Exists(CoveragePath + ".schema76.bak"));
        Assert.Equal("stale", new SourceIndexRepository(upgraded).ReadState("source")!.Status);
        var store = Store();
        Assert.Throws<InvalidDataException>(() => store.PrepareParatext(upgraded, "project", "source", "old-pin",
            ProfileHash, new CoreWritingSystemDefinition("en")));
        var segmentation = store.Prepare(upgraded, Manifest());
        var basis = oldBasis with { SegmentationId = segmentation.Id, MappingDigest = segmentation.MappingDigest };
        var carried = new AssessmentRepository(upgraded).Get("old-assessment");
        Assert.NotEmpty(carried.Words!);
        Assert.Null(carried.Invocation!.InputProfile);
        var refusal = Assert.Throws<GrammarCoverageReadException>(() => store.Build(upgraded, carried.AssessmentId, basis));
        Assert.Equal(GrammarCoverageReadFailure.Unavailable, refusal.Failure);
    }

    [Theory]
    [InlineData("tr", "IST", "ıst", "ıST")]
    [InlineData("en", "DOG", "dog", "dOG")]
    public void WholeWordAndFirstLetterPoliciesPreserveTheirExactParserKeys(
        string languageTag, string original, string whole, string first)
    {
        var casing = LowercasePass.For(new CoreWritingSystemDefinition(languageTag));
        Assert.Equal(new[] { original, whole },
            GrammarCoverageParserInputs.Expand([original], casing, BatchInputProfile.WholeWordLowercase).Words);
        Assert.Equal(new[] { original, first },
            GrammarCoverageParserInputs.Expand([original], casing, GrammarCoverageParserInputs.FirstLetterLowercase).Words);
    }

    [Fact]
    public void OpaqueLegacyDigestsAndRepeatedPathsDoNotEstablishCompetition()
    {
        using var database = Open();
        var store = Store();
        var manifest = Manifest() with
        {
            Rows = [new("01", "ws", "opaque", "opaque", 2), new("02", "ws", "repeat", "repeat", 3)],
        };
        var basis = Save(database, store.Prepare(database, manifest), "before", "", ["opaque", "repeat"],
        [
            new("opaque", "analysed", [new(null, [], -1, "a"), new(null, [], -1, "b")]),
            new("repeat", "analysed", []) { Morphology = Morphology("repeat", "analysed", [PathOne, PathOne]) },
        ]);

        var summary = store.Build(database, "before", basis);
        Assert.Equal(5, summary.ProvedOccurrences.Analysed);
        Assert.Equal(0, summary.ProvedOccurrences.Ambiguous);
        Assert.Equal(5, summary.UnknownOccurrences.Ambiguous);
        Assert.Equal(5, summary.Aggregate.OccurrenceBuckets.Unmeasured);
    }

    private ProjectLocator Project => new(Path.Combine(_root, "test.fwdata"), "test");
    private string CoveragePath => ProjectDatabaseCatalog.DatabasePathFor(Project);

    private GrammarCoverageStore Store() => new(CoveragePath, Project, (_, _) => LowercasePass.For(new("en")));

    private MotifDatabase Open() => MotifDatabase.OpenOwned(CoveragePath, Project, MotifSchema.CurrentSchema, new Version(1, 0));

    private static string ProfileHash => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([]));

    private const string ProjectId = "b4f8ee0f-ff41-4e1e-881c-e8b6d14d91e7";

    private static FrequencyManifest Manifest() => new(new(ProjectId, "source", "source-digest", "scope-digest",
        "weights", "wordmaker", "tokenizer-hash", ProfileHash, false, "explicit", "NFC", "ws",
        new Dictionary<string, long> { ["excluded"] = 9 }, AllowsSurfaceStorage: true),
    [
        new("01", "ws", "zulu", "zulu", 5),
        new("02", "ws", "alpha", "alpha", 5),
        new("03", "ws", "Parsed", "Parsed", 6, "parsed"),
        new("04", "ws", "É", "É", 7, "é"),
        new("05", "ws", "stop", "stop", 3),
        new("06", "ws", "cap", "cap", 2),
        new("07", "ws", "missing", "missing", 4),
        new("08", "ws", "partial", "partial", 1),
    ]);

    private GrammarCoverageBasis Save(MotifDatabase database, GrammarCoverageSegmentation segmentation,
        string assessmentId, string changed, string[]? selectionWords = null, AssessedWord[]? extra = null,
        string[]? expectedGeneratedKeys = null, string? warning = null, string? writingSystem = "ws")
    {
        var selection = Selection.Create("superset", selectionWords ?? ["zulu", "alpha", "Parsed", "É", "é", "stop", "cap", "outside", "partial"]);
        var words = (selectionWords is null ? selection.Words.Append("parsed") : selection.Words).Select(word =>
        {
            var outcome = word switch
            {
                "stop" or "partial" => "timed-out", "cap" => "capped", "parsed" or "E\u0301" or "outside" => "analysed",
                _ => word == changed ? "analysed" : "no-analysis",
            };
            IReadOnlyList<ParsedAnalysis> analyses = word switch
            {
                "partial" or "E\u0301" => [new(null, [], -1, "a"), new(null, [], -1, "b"), new(null, [], -1, "b")],
                _ => outcome == "analysed" ? [new(null, [], -1, "a")] : [],
            };
            var paths = analyses.Count > 1 ? new[] { PathOne, PathTwo, PathTwo } : analyses.Count == 1 ? [PathOne] : [];
            return new AssessedWord(word, outcome, analyses)
            {
                LowercaseOf = word == "parsed" ? "Parsed" : null,
                Morphology = Morphology(word, outcome, paths),
            };
        }).Where(item => extra?.Any(replacement => replacement.Word == item.Word) != true).Concat(extra ?? []).ToArray();
        var limits = ParserWorkLimit.Default;
        var scope = ScopeCodec.Write(new StoredScope.Trial("superset", selection.Words, [], null, limits));
        var source = Path.Combine(_root, assessmentId + ".fwdata");
        File.WriteAllText(source, "independent grammar bytes for " + assessmentId);
        var grammar = BatchInvocationEvidence.DigestFile(source);
        var parserKeys = selection.Words.Concat(expectedGeneratedKeys ?? (selectionWords is null ? ["parsed"] : []));
        var inputHash = AssessmentMaterial.Digest(string.Join("\n", parserKeys) + "\n");
        var invocation = new BatchInvocationEvidence(assessmentId, source, grammar, "parser-hash",
            "words", inputHash, "tsv", "tsv-hash", "stderr", "stderr-hash", null, limits, 1, false)
        {
            ExecutableVersion = "test-parser",
            InputProfile = writingSystem is null ? null : new(writingSystem, ProfileHash, BatchInputProfile.WholeWordLowercase),
            GrammarWarnings = warning,
        };
        var baseline = new BaselineToken(ProjectId, AssessmentMaterial.Digest(assessmentId), "1",
            "2020-01-01T00:00:00Z", AssessmentMaterial.Digest("bundle-" + assessmentId));
        new BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(Project), new(_root, source, baseline),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, new([], []));
        new AssessmentRepository(database).Record(new(assessmentId, null, null, PanGlossAssessor.AssessorName,
            "ParseTime", scope, AssessmentMaterial.Digest(scope), new AssessmentSubject.Words(selection, "none", "1"),
            JsonSerializer.Serialize(baseline),
            "outcomes", null, invocation.SourceBytesSha256, "model", null, null,
            new AssessmentRecordMaterial.Words(words, null, null, [], invocation)));
        return new(ProjectId, baseline.SemanticSnapshotDigest, baseline, grammar,
            "test-parser/parser-hash", GrammarCoverageAssessment.LimitsId(null, limits, null),
            segmentation.Id, segmentation.MappingDigest);
    }

    private static readonly ParseAnalysis PathOne = new(
        [new("00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000003", null, null)]);
    private static readonly ParseAnalysis PathTwo = new(
        [new("00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000003", null, null)]);

    private static ParseWordEvidence Morphology(string word, string outcome, ParseAnalysis[] paths) =>
        new("test", 0, word, 0, outcome == "capped", outcome == "timed-out", false, paths, []);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
