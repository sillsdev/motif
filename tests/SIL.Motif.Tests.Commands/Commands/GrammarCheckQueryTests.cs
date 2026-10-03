using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Pins how grammar-health report diagnostics become Motif findings.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class GrammarCheckQueryTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.GrammarCheckQueryTests", Guid.NewGuid().ToString("N"));

    public GrammarCheckQueryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData("FsClosedValue", false)]
    [InlineData("FsFeatStruc", false)]
    [InlineData("FsClosedValue", true)]
    [InlineData("FsFeatStruc", true)]
    public void NormalizedFeatureSubjectSurvivesReportStorageAndYourWords(string sourceClass, bool conflicting)
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var loader = new FwDataProjectLoader();
        string guid;
        string msaId;
        using (var cache = loader.LoadCache(fwDataPath))
        {
            var services = cache.ServiceLocator;
            var msa = (IMoStemMsa)services.GetInstance<ILexEntryRepository>()
                .GetObject(_pristine.Seed.FirstEntryId).MorphoSyntaxAnalysesOC.First();
            IFsClosedValue spec = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                msa.MsFeaturesOA = services.GetInstance<IFsFeatStrucFactory>().Create();
                spec = services.GetInstance<IFsClosedValueFactory>().Create();
                msa.MsFeaturesOA.FeatureSpecsOC.Add(spec);
            });
            guid = (sourceClass == "FsClosedValue" ? spec.Guid : msa.MsFeaturesOA.Guid).ToString("D");
            msaId = msa.Guid.ToString("D");
            loader.Save(cache);
        }
        Capture(fwDataPath);
        var report = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "GrammarHealth", "schema-v4-producer.json")))!;
        var diagnostic = report["diagnostics"]![0]!;
        diagnostic["subjects"] = System.Text.Json.Nodes.JsonNode.Parse($$$"""
            [{"status":"object","guid":"{{{guid}}}","kind":"Unknown",
              "source_class":"{{{(conflicting ? "LexSense" : sourceClass)}}}","field":"Value","title":"Specification",
              "subtitle":null,"internal_id":null,
              "fieldworks":{"status":"unavailable","guid":"{{{guid}}}","reason":"unsupported_kind"}}]
            """);
        report["diagnostics"] = new System.Text.Json.Nodes.JsonArray(diagnostic.DeepClone());
        var result = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(report.ToJsonString(), string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        foreach (var findings in new[] { result.Value!.Findings, stored.Value!.Check!.Findings })
        {
            var finding = Assert.Single(findings);
            var subject = Assert.Single(finding.Subject, part => part.SubjectGuid is not null);
            Assert.Equal("Unknown", subject.FieldWorksKind);
            Assert.Equal(conflicting ? "LexSense" : sourceClass, subject.SourceClass);
            var reach = subject.Reach!;
            var word = new AssessmentWordResult("synthetic", "no-analysis", false, "Search completed", 1, null)
            {
                StoredAnalyses = [new ParserReading([new ParserReadingMorph("synthetic", "", "", null, false, null)
                    { GrammaticalInfoId = msaId }]) { StoredAnalysisOpinion = ReadingGrade.Approved }],
            };
            var attributed = WarningWordsQuery.YourWordsOf(finding, [word], [])!;
            if (conflicting)
            {
                Assert.Equal(WarningAttributionReason.WrongClass, reach.Reason);
                Assert.Empty(attributed.Words);
            }
            else
            {
                Assert.Equal(WarningWordsPath.ThroughFeatureOwners, reach.Path);
                Assert.Equal([msaId], reach.GrammaticalInfoIds);
                Assert.Equal(WarningAttributionState.ExactUses, attributed.State);
                Assert.Equal("synthetic", Assert.Single(attributed.Words).Row.Word);
            }
        }
    }

    [Fact]
    public void CapturedSubjectStatusPreventsLiveReachEvenWhenTheGuidNowExists()
    {
        var subject = new GrammarWarningPart("Missing entry", GrammarWarningPartRole.Missing,
            _pristine.Seed.FirstEntryId.ToString("D"), "LexEntry")
        {
            SubjectGuid = _pristine.Seed.FirstEntryId.ToString("D"), Status = GrammarSubjectStatus.UnresolvedReference,
        };
        var unresolved = SIL.Motif.Projection.Grammar.WarningReachReader.Reach(subject,
            () => throw new InvalidOperationException("Captured unresolved references never resolve against a later project."));
        Assert.Equal(WarningWordsPath.MissingObject, unresolved!.Path);
        Assert.Equal("UnresolvedReference", unresolved.Reason.ToString());
        var settings = SIL.Motif.Projection.Grammar.WarningReachReader.Reach(subject with
        {
            Status = GrammarSubjectStatus.ProjectSettings,
        }, () => throw new InvalidOperationException("Settings never resolve as objects."));
        Assert.Equal(WarningWordsPath.ProjectWide, settings!.Path);
    }

    [Theory]
    [InlineData("locale")]
    [InlineData("explanation")]
    [InlineData("status")]
    [InlineData("live-link")]
    public void SchemaFourMissingFieldsAndFalseLiveLinksAreRefused(string defect)
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var raw = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "GrammarHealth", "schema-v4-producer.json")))!.AsObject();
        var finding = raw["diagnostics"]![1]!;
        var subject = finding["subjects"]![1]!;
        if (defect == "locale") raw.Remove("locale");
        else if (defect == "explanation") finding.AsObject().Remove("explanation");
        else if (defect == "status") subject.AsObject().Remove("status");
        else subject["fieldworks"] = raw["diagnostics"]![0]!["subjects"]![0]!["fieldworks"]!.DeepClone();
        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(raw.ToJsonString(), string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);
        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.malformed-findings", outcome.Refusal!.Code);
    }

    [Fact]
    public void ProducerSchemaFourRetainsAdviceAndUnresolvedSubjects()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth", "schema-v4-producer.json"));
        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(raw, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        using var projected = JsonDocument.Parse(JsonSerializer.Serialize(outcome.Value));
        Assert.Equal("en", projected.RootElement.GetProperty("Locale").GetString());
        var finding = projected.RootElement.GetProperty("Findings")[1];
        Assert.Equal("An affix template refers to a slot that cannot be resolved.", finding.GetProperty("Explanation").GetString());
        Assert.Equal("posEdit", finding.GetProperty("FieldWorksPlaces")[0].GetProperty("Tool").GetString());
        var subject = finding.GetProperty("Subject")[1];
        Assert.Equal("unresolved_reference", subject.GetProperty("Status").GetString());
        Assert.Equal("SuffixSlots", subject.GetProperty("Field").GetString());
        Assert.Null(subject.GetProperty("FieldWorksLink").GetString());
        Assert.Equal("unresolved_reference", subject.GetProperty("LinkReason").GetString());
        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Contains("An affix template refers", JsonSerializer.Serialize(stored.Value), StringComparison.Ordinal);
    }

    [Fact]
    public void CapturedPinnedParserAdviceSurvivesReadingAndStorageForEveryKind()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "pangloss-v0.6.0-seeded.json"));
        using var document = JsonDocument.Parse(raw);
        var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.True(diagnostics.Select(item => item.GetProperty("code").GetString()).Distinct().Count() >= 3);
        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(raw, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        foreach (var findings in new[] { outcome.Value!.Findings, stored.Value!.Check!.Findings })
        {
            Assert.Equal(diagnostics.Length, findings.Count);
            foreach (var (diagnostic, finding) in diagnostics.Zip(findings))
            {
                Assert.Equal(diagnostic.GetProperty("code").GetString(), finding.Code);
                Assert.Equal(diagnostic.GetProperty("explanation").GetString(), finding.Explanation);
                Assert.Equal(diagnostic.GetProperty("guidance").GetString(), finding.Guidance);
                Assert.Equal(diagnostic.GetProperty("description").GetString(), finding.Description);
                Assert.False(string.IsNullOrWhiteSpace(finding.Explanation));
                Assert.False(string.IsNullOrWhiteSpace(finding.Guidance));
            }
        }
    }

    [Fact]
    public void NoBaselineIsASuccessfulEmptyAnswer_AndNeverReachesTheParser()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var invoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("Must not reach the parser without a Baseline."),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.HasBaseline);
        Assert.Empty(outcome.Value.Findings);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public void CancelledGrammarCheckIsATypedCancellationRefusal()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = GrammarCheckQuery.Query(
            new GrammarCheckRequest(fwDataPath), new FakeInvoker(), cancellation.Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.cancelled", outcome.Refusal!.Code);
    }

    [Fact]
    public void ABaselineReadsReportDiagnosticsAndIgnoresStderr()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var entryGuid = _pristine.Seed.FirstEntryId.ToString("D");
        var openGuid = _pristine.Seed.SecondEntryId.ToString("D");
        var report = Report(entryGuid, openGuid);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                report,
                "warning: stderr-only warning\ncapability: stderr-only capability\n", TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.True(response.HasBaseline);
        Assert.Equal(2, response.Findings.Count);
        Assert.Equal(2, response.Summary.Count);
        Assert.DoesNotContain(response.Findings, finding => finding.Text.Contains("stderr-only", StringComparison.Ordinal));

        var warning = response.Findings[0];
        Assert.Equal(GrammarDiagnosticLevel.Warning, warning.Severity);
        Assert.Equal(GrammarFindingOrigin.Import, warning.Origin);
        Assert.Equal("Partial morpheme analysis", warning.Group);
        Assert.Equal("Lexical entry 'mbo' has no grammatical category.", warning.Description);
        Assert.Equal("In Lexicon > Lexicon Edit, set Grammatical Info. > Category.", warning.Guidance);
        Assert.Equal(2, warning.Subject.Count);

        var entry = warning.Subject[0];
        Assert.Equal("mbo (ADD)", entry.Text);
        Assert.Equal("mbo", entry.Title);
        Assert.Equal("ADD", entry.Subtitle);
        Assert.Equal("LexEntry", entry.FieldWorksKind);
        Assert.Equal(entryGuid, entry.ObjectId);
        Assert.Equal(entryGuid, entry.SubjectGuid);
        Assert.Null(entry.InternalId);
        Assert.Equal(FieldWorksLinkStatus.Available, entry.LinkStatus);
        Assert.Equal("lexiconEdit", entry.FieldWorksTool);
        Assert.Equal(openGuid, entry.FieldWorksGuid);
        Assert.Contains(openGuid, entry.FieldWorksLink, StringComparison.Ordinal);

        var phoneme = warning.Subject[1];
        Assert.Equal("ng", phoneme.Text);
        Assert.Equal("PhPhoneme", phoneme.FieldWorksKind);
        Assert.Equal(FieldWorksLinkReason.UnsupportedKind, phoneme.LinkReason);
        Assert.Null(phoneme.FieldWorksLink);

        var info = response.Findings[1];
        Assert.Equal(GrammarDiagnosticLevel.Information, info.Severity);
        Assert.Equal(GrammarFindingOrigin.Check, info.Origin);
        Assert.Equal("Duplicate segment features", info.Group);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        Assert.False(json.RootElement.TryGetProperty("FieldWorksProject", out _));
        var reportFinding = json.RootElement.GetProperty("Findings")[0];
        Assert.Equal("warning", reportFinding.GetProperty("Severity").GetString());
        Assert.Equal("import", reportFinding.GetProperty("Origin").GetString());
        Assert.False(reportFinding.TryGetProperty("Audience", out _));
        Assert.Equal(entryGuid, reportFinding.GetProperty("Subject")[0].GetProperty("SubjectGuid").GetString());
        Assert.Null(reportFinding.GetProperty("Subject")[0].GetProperty("InternalId").GetString());
        Assert.False(reportFinding.GetProperty("Subject")[0].TryGetProperty("OpenTargetTool", out _));
        Assert.Equal("available", reportFinding.GetProperty("Subject")[0].GetProperty("LinkStatus").GetString());
        Assert.Equal("object", reportFinding.GetProperty("Subject")[0].GetProperty("Role").GetString());
        Assert.Equal("unsupported_kind", reportFinding.GetProperty("Subject")[1].GetProperty("LinkReason").GetString());
        Assert.Equal("info", json.RootElement.GetProperty("Summary")[1].GetProperty("Level").GetString());

        var request = Assert.IsType<PanGlossRequest.GrammarHealth>(Assert.Single(invoker.Requests).Request);
        Assert.EndsWith(".fwdata", request.GrammarPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFileNameWithoutExtension(fwDataPath), request.FieldWorksProjectName);
    }

    [Fact]
    public void AVersionFourReportRetainsErrorsAsDistinctFindings()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var fixturePath = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "SIL.Motif.Tests.Support",
            "TestFixtures", "GrammarHealth", "schema-v4-error.json");
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(File.ReadAllText(fixturePath), string.Empty, TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var error = Assert.Single(outcome.Value!.Findings);
        Assert.Equal("error", error.Severity.ToWireValue());
        Assert.Equal("error: hc-invalid-feature-system: A feature system could not be loaded.", error.Text);
        Assert.Equal("error", outcome.Value.Summary.Single().Level.ToWireValue());
    }

    [Fact]
    public void ErrorFindingsInTheReportSurviveTheParserNonzeroExit()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var report = JsonSerializer.Serialize(new
        {
            schema_version = 4, locale = "en",
            fieldworks_project = new { name = "Synthetic", source = "argument" },
            summary = new[]
            {
                new { code = "grammar.msa.no-allomorphs", group_name = "No usable entry allomorphs", level = "error", count = 1 },
            },
            diagnostics = new[]
            {
                new
                {
                    level = "error", code = "grammar.msa.no-allomorphs",
                    group_name = "No usable entry allomorphs", origin = "import",
                    description = "Lexical entry 'kat' has no usable allomorphs.",
                    guidance = "Add or correct an allomorph for the named lexical entry.",
                    title = "No usable entry allomorphs", explanation = "Lexical entry 'kat' has no usable allomorphs.", help_path = (string?)null, help_body = (string?)null,
                    fieldworks_places = Array.Empty<object>(), scope = "project_settings",
                    subjects = Array.Empty<object>(),
                },
            },
        });
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Refused(1, "grammar has errors", report,
                "pangloss grammar-health exited 1: grammar has errors"),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var finding = Assert.Single(outcome.Value!.Findings);
        Assert.Equal("Error", finding.Severity.ToString());
        Assert.Equal("error", finding.Severity.ToWireValue());
        Assert.Equal("grammar.msa.no-allomorphs", finding.Code);
        Assert.Equal(1, Assert.Single(outcome.Value.Summary).Count);
    }

    [Fact]
    public void AParserNonzeroExitWithoutErrorFindingsRemainsARefusal()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Refused(1, "unexpected failure",
                Report(_pristine.Seed.FirstEntryId.ToString("D"), "5c9e433d-cc9b-4d12-b8cb-b5840f46dbd2"),
                "pangloss grammar-health exited 1: unexpected failure"),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.parser-refused", outcome.Refusal!.Code);
    }

    [Fact]
    public void AReportWithTheOldFindingsMemberIsRejected()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                "{\"schema_version\":4,\"summary\":[],\"findings\":[]}",
                string.Empty, TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.malformed-findings", outcome.Refusal!.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void AnUnsupportedSchemaVersionNamesTheVersionAndUpdateRequirement(int schemaVersion)
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                $"{{\"schema_version\":{schemaVersion}}}", string.Empty, TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.unsupported-schema", outcome.Refusal!.Code);
        Assert.Contains($"version {schemaVersion}", outcome.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version 4", outcome.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("update PanGloss and Motif", outcome.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"schema_version\":4,\"fieldworks_project\":{\"name\":null,\"source\":null},\"summary\":[],\"findings\":[]}")]
    public void AReportWithoutTheReportObjectAndDiagnosticsEnvelopeIsMalformed(string report)
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(report, string.Empty, TimeSpan.Zero),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.malformed-findings", outcome.Refusal!.Code);
    }

    [Fact]
    public void EveryRequestedReloadChecksTheParserAgain()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                GrammarHealthReports.With(("hc-undeclared-segment", [])),
                string.Empty, TimeSpan.Zero),
        };
        var request = new GrammarCheckRequest(fwDataPath);

        var first = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-1");
        var second = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-1");
        var otherParser = GrammarCheckQuery.Query(request, invoker, CancellationToken.None, parserStamp: "build-2");

        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.Equal(first.Value!.Findings.Single().Text, second.Value!.Findings.Single().Text);
        Assert.True(otherParser.Succeeded, otherParser.Refusal?.Message);
        Assert.Equal(3, invoker.Requests.Count);
    }

    [Fact]
    public void AParserThatDeclinesIsATypedRefusal_NotAnException()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Unavailable("Could not find the pangloss executable."),
        };

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(fwDataPath), invoker, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammarcheck.parser-unavailable", outcome.Refusal!.Code);
    }

    private static string Report(string entryGuid, string openGuid) => JsonSerializer.Serialize(new
    {
        schema_version = 4, locale = "en",
        fieldworks_project = new { name = "Synthetic", source = "argument" },
        summary = new[]
        {
            new { code = "hc-partial-morpheme", group_name = "Partial morpheme analysis", level = "warning", count = 1 },
            new { code = "hc-duplicate-feature-bundle", group_name = "Duplicate segment features", level = "info", count = 1 },
        },
        diagnostics = new object[]
        {
            new
            {
                level = "warning",
                code = "hc-partial-morpheme",
                group_name = "Partial morpheme analysis",
                origin = "import",
                description = "Lexical entry 'mbo' has no grammatical category.",
                guidance = "In Lexicon > Lexicon Edit, set Grammatical Info. > Category.",
                    title = "Partial morpheme analysis", explanation = "Lexical entry 'mbo' has no grammatical category.", help_path = (string?)null, help_body = (string?)null,
                    fieldworks_places = Array.Empty<object>(), scope = "object",
                subjects = new object[]
                {
                    new
                    {
                        kind = "LexEntry", status = "object", field = (string?)null, source_class = (string?)null, title = "mbo", subtitle = "ADD", guid = entryGuid,
                        internal_id = (string?)null,
                        opens_in = new { tool = "lexiconEdit", guid = openGuid },
                        fieldworks = new
                        {
                            status = "available", guid = openGuid, tool = "lexiconEdit",
                            url = $"silfw://localhost/link?database=Sena%203&tool=lexiconEdit&guid={openGuid}&tag=",
                        },
                    },
                    new
                    {
                        kind = "PhPhoneme", status = "object", field = (string?)null, source_class = (string?)null, title = "ng", subtitle = (string?)null, guid = (string?)null,
                        internal_id = (string?)null,
                        fieldworks = new { status = "unavailable", reason = "unsupported_kind", guid = (string?)null },
                    },
                },
            },
            new
            {
                level = "info", code = "hc-duplicate-feature-bundle", group_name = "Duplicate segment features",
                origin = "check", description = "Two phonemes share the same feature values.", guidance = (string?)null,
                    title = "Duplicate segment features", explanation = "Two phonemes share the same feature values.", help_path = (string?)null, help_body = (string?)null,
                    fieldworks_places = Array.Empty<object>(), scope = "project_settings",
                subjects = Array.Empty<object>(),
            },
        },
    });

    [Fact]
    public void EachSubjectReachesWhatItLeadsToInTheCheckedProject_AndTheStoredCheckKeepsIt()
    {
        var grammar = WarningGrammar.Author(_pristine);
        var seed = _pristine.Seed;
        Capture(grammar.FwDataPath);
        var report = GrammarHealthReports.With(
            ("allomorph", [new("MoForm", "motifa", seed.FirstLexemeFormId)]),
            ("grammatical-info", [new("MoStemMsa", "motifa", grammar.FirstMsa)]),
            ("entry", [new("LexEntry", "motifa", seed.FirstEntryId)]),
            ("sense", [new("LexSense", "first seeded gloss", seed.FirstSenseId)]),
            ("environment", [new("PhEnvironment", "/ _ [V]", grammar.VowelsBefore)]),
            ("natural-class", [new("PhNaturalClass", "V", grammar.Vowels)]),
            ("rule", [new("PhRegularRule", "Vowel harmony", grammar.Harmony)]),
            ("phoneme", [new("PhPhoneme", "u", grammar.U)]),
            ("letter", [new("PhPhoneme", "ng")]),
            ("template", [new("MoInflAffixTemplate", "Verb template", Guid.NewGuid())]),
            ("gone", [new("LexEntry", "kata", Guid.NewGuid())]),
            ("nothing", []));

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(grammar.FwDataPath), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(report, string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var reach = outcome.Value!.Findings.ToDictionary(finding => finding.Code!,
            finding => finding.Subject.SingleOrDefault()?.Reach);
        string Id(Guid guid) => guid.ToString("D");
        Assert.Equal((WarningWordsPath.Uses, Id(seed.FirstLexemeFormId)),
            (reach["allomorph"]!.Path, Assert.Single(reach["allomorph"]!.AllomorphIds)));
        Assert.Equal((WarningWordsPath.Uses, Id(grammar.FirstMsa)),
            (reach["grammatical-info"]!.Path, Assert.Single(reach["grammatical-info"]!.GrammaticalInfoIds)));
        Assert.Equal((WarningWordsPath.ThroughAllomorphs, Id(seed.FirstLexemeFormId)),
            (reach["entry"]!.Path, Assert.Single(reach["entry"]!.AllomorphIds)));
        Assert.Equal((WarningWordsPath.ThroughGrammaticalInfo, Id(grammar.FirstMsa)),
            (reach["sense"]!.Path, Assert.Single(reach["sense"]!.GrammaticalInfoIds)));
        Assert.Equal((WarningWordsPath.ThroughAllomorphs, Id(seed.SecondLexemeFormId)),
            (reach["environment"]!.Path, Assert.Single(reach["environment"]!.AllomorphIds)));
        Assert.Equal(WarningWordsPath.ThroughEnvironmentsAndRules, reach["natural-class"]!.Path);
        Assert.Empty(reach["natural-class"]!.AllomorphIds);
        Assert.Equal([WarningAttributionReason.UnresolvedEnvironmentNotation], reach["natural-class"]!.AttributionLimits);
        Assert.Equal([new TraceTimingKey("phon_rule", Id(grammar.Harmony))], reach["natural-class"]!.TimingKeys);
        Assert.Equal([new TraceTimingKey("phon_rule", Id(grammar.Harmony))], reach["rule"]!.TimingKeys);
        Assert.Equal(WarningWordsPath.RuleTimes, reach["rule"]!.Path);
        Assert.Equal((WarningWordsPath.Spelling, "u"), (reach["phoneme"]!.Path, Assert.Single(reach["phoneme"]!.Spellings)));
        Assert.Equal((WarningWordsPath.Spelling, "ng"), (reach["letter"]!.Path, Assert.Single(reach["letter"]!.Spellings)));
        Assert.Equal((WarningWordsPath.MissingObject, WarningAttributionReason.StaleGuid),
            (reach["template"]!.Path, reach["template"]!.Reason));
        Assert.Equal(WarningAttributionReason.StaleGuid, reach["gone"]!.Reason);
        Assert.Null(reach["nothing"]);
        Assert.All(outcome.Value.Findings, finding => Assert.Null(finding.YourWords));

        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(grammar.FwDataPath)).Value!.Check!;
        Assert.Equal(JsonSerializer.Serialize(outcome.Value.Findings.Select(finding => finding.Subject)),
            JsonSerializer.Serialize(stored.Findings.Select(finding => finding.Subject)));
    }

    private void Capture(string fwDataPath)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
