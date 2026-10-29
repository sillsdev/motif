using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Pins <c>motif trace</c>: a live trace of a seeded word prints the record Try a Word reads, whole, and a saved trace
/// read with <c>--load</c> prints the same reading the window builds from that file.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TraceArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private const string GoldenFile = "trace-motifa.golden.json";
    private readonly string _managedRoot = Path.Combine(
        Path.GetTempPath(), "motif-trace-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TraceRenderer_UsesCapturedRuleNamesAndExplainsRecordedFailureCodes()
    {
        Directory.CreateDirectory(_managedRoot);
        var path = Path.Combine(_managedRoot, "captured-rule.json");
        File.WriteAllText(path, TraceEnvelope.CapturedRuleLabel);
        var text = await CliInProcess.RunAsync(_managedRoot, null, false, "trace", "--load", path);
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("Stopped 1 attempt(s): Recorded reason code PartialParse: This attempt did not complete the required rules and templates. Owner explanation not recorded.", text.Output);
        Assert.Empty(WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!.Reading.RulesOnBestPath);
        Assert.Contains("phonologicalRule Vowel harmony", text.Output);
        Assert.DoesNotContain("Producer name", text.Output);

        var json = await CliInProcess.RunAsync(_managedRoot, null, false, "trace", "--load", path, "--json");
        Assert.True(json.ExitCode == 0, json.Error);
        var response = ProjectionJson.Deserialize<WordTraceResponse>(json.Output)!;
        Assert.Equal("Producer name", Assert.Single(response.Reading.Refs).Label);
        Assert.Equal("Vowel harmony", Assert.Single(response.Reading.Refs).CapturedFieldWorksLabel);
    }

    [Fact]
    public async Task TraceRenderer_ShowsEveryCompletedZeroMatchLookupAndKeepsItsEvidenceInJson()
    {
        var shapes = new[] { "mtu", "mtuu", "msoma" };
        foreach (var shape in shapes)
        {
            var diagnostic = Envelope(shape, LookupTree([shape]));

            var text = await LoadTrace("zero-match-" + shape, diagnostic);
            Assert.True(text.ExitCode == 0, text.Error);
            Assert.Contains($"No lexical root matched '{shape}' in Morphology.", text.Output, StringComparison.Ordinal);

            var json = await LoadTrace("zero-match-" + shape, diagnostic, asJson: true);
            Assert.True(json.ExitCode == 0, json.Error);
            var response = ProjectionJson.Deserialize<WordTraceResponse>(json.Output)!;
            var lookup = Assert.Single(Walk(response.Reading.Root),
                step => step.EventEvidence?.LookupResult is not null)
                .EventEvidence!.LookupResult!;
            Assert.True(lookup.Completed);
            Assert.Equal(0, lookup.MatchCount);
            Assert.Equal("zeroMatches", lookup.Status);
            Assert.Contains(shape, response.DiagnosticJson, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TraceRenderer_ReportsUnknownNoParseReasonWhenLookupEvidenceIsMissing(bool incompleteSearch)
    {
        var diagnostic = Envelope("mtuuu", null, capped: incompleteSearch);

        var result = await LoadTrace("missing-lookup", diagnostic);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("No reason for the lack of a parse was recorded.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("No lexical root matched", result.Output, StringComparison.Ordinal);
        if (incompleteSearch)
        {
            Assert.Contains("Search: stopped early.", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Search: complete", result.Output, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Search: complete", result.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TraceRenderer_ExplainsInvalidShapeFromRecordedReason()
    {
        var result = await LoadTrace("invalid-shape", Envelope("bad-shape", null, invalidShape: true));

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("PanGloss can't read this word with this project's phonemes (invalidShape).", result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("No reason for the lack of a parse was recorded.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraceRenderer_ExplainsKnownAndUnknownFailureCodesWithoutInventingAnOwner()
    {
        var codes = new[] { "Environments", "RequiredSyntacticFeatureStruct", "SurfaceFormMismatch", "FutureReason" };
        var diagnostic = Envelope("nbaya", FailureTree(codes));

        var text = await LoadTrace("failure-codes", diagnostic);
        Assert.True(text.ExitCode == 0, text.Error);
        var stops = string.Join(Environment.NewLine, text.Output.Split(Environment.NewLine)
            .Where(line => line.StartsWith("  Stopped ", StringComparison.Ordinal)));
        Assert.Contains("Recorded reason code Environments: This attempt does not fit the required sound environment.", stops);
        Assert.Contains("Recorded reason code RequiredSyntacticFeatureStruct: This attempt needs an inflection feature the word doesn't have.", stops);
        Assert.Contains("Recorded reason code SurfaceFormMismatch: This attempt builds a form that differs from the word being parsed.", stops);
        Assert.Contains("Recorded reason code FutureReason: PanGloss recorded an unfamiliar reason: FutureReason.", stops);
        Assert.Equal(codes.Length, stops.Split("  Stopped ", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Sibling rule", stops);
        Assert.All(codes, code => Assert.Contains($"Recorded reason code {code}:", stops, StringComparison.Ordinal));
        Assert.All(codes, _ => Assert.Contains("Owner explanation not recorded.", stops, StringComparison.Ordinal));

        var json = await LoadTrace("failure-codes", diagnostic, asJson: true);
        Assert.True(json.ExitCode == 0, json.Error);
        var response = ProjectionJson.Deserialize<WordTraceResponse>(json.Output)!;
        Assert.Equal(codes.Length, response.Reading.StopGroups.Count);
        foreach (var group in response.Reading.StopGroups)
        {
            Assert.Null(group.Explanation);
            var attempt = Assert.Single(group.Attempts);
            Assert.Equal(TraceEvidenceAvailability.NotRecorded, attempt.ExplanationAvailability);
            Assert.Equal("unavailable", attempt.FailureEvidence!.Status);
            Assert.Equal(group.ReasonCode, attempt.FailureEvidence.ReasonCode);
        }
    }

    [Fact]
    public async Task TraceRenderer_ShowsPartialParseCompletionGateAndSurfaceMismatchEvidence()
    {
        var baya = await LoadTrace("baya-partial", Envelope("baya", PartialTemplateTree()));
        Assert.True(baya.ExitCode == 0, baya.Error);
        Assert.Contains("Recorded reason code PartialParse: This attempt did not complete the required rules and templates.",
            baya.Output, StringComparison.Ordinal);
        Assert.Contains("Recorded completion gate: ApplicableTemplatesNotApplied", baya.Output, StringComparison.Ordinal);
        Assert.Contains("Lookup completed: 1 materialized roots (lexicon); not analyses", baya.Output, StringComparison.Ordinal);
        Assert.Contains("morph baya", baya.Output, StringComparison.Ordinal);

        var nbaya = await LoadTrace("nbaya-mismatch", Envelope("nbaya", SurfaceMismatchTree()));
        Assert.True(nbaya.ExitCode == 0, nbaya.Error);
        Assert.Contains("Recorded reason code SurfaceFormMismatch: This attempt builds a form that differs from the word being parsed.",
            nbaya.Output, StringComparison.Ordinal);
        Assert.Contains("Lookup completed: 1 materialized roots (lexicon); not analyses", nbaya.Output, StringComparison.Ordinal);
        Assert.Contains("morph baya", nbaya.Output, StringComparison.Ordinal);
        Assert.Contains("requestedShape", nbaya.Output, StringComparison.Ordinal);
        Assert.Contains("builtShape", nbaya.Output, StringComparison.Ordinal);
        Assert.Contains("m+?baya", nbaya.Output, StringComparison.Ordinal);

        var mismatchJson = await LoadTrace("nbaya-mismatch", Envelope("nbaya", SurfaceMismatchTree()), asJson: true);
        Assert.True(mismatchJson.ExitCode == 0, mismatchJson.Error);
        var mismatch = ProjectionJson.Deserialize<WordTraceResponse>(mismatchJson.Output)!;
        var evidence = Assert.Single(Walk(mismatch.Reading.Root),
            step => step.FailureEvidence?.Payload is not null).FailureEvidence!.Payload!.Value;
        Assert.Equal("nbaya", evidence.GetProperty("requestedShape").GetString());
        Assert.Equal("m+?baya", evidence.GetProperty("builtShape").GetString());
    }

    [Fact]
    public async Task TraceRenderer_ShowsScopedNamesWithoutOpaqueIdentityAndKeepsJsonIdentity()
    {
        var diagnostic = ScopedStratumEnvelope();

        var text = await LoadTrace("scoped-stratum", diagnostic);
        Assert.True(text.ExitCode == 0, text.Error);
        var names = text.Output[text.Output.IndexOf("  Names:", StringComparison.Ordinal)..];
        Assert.Contains("stratum Captured morphology", names, StringComparison.Ordinal);
        Assert.DoesNotContain("grammarHash", names, StringComparison.Ordinal);
        Assert.DoesNotContain("scope", names, StringComparison.Ordinal);

        var json = await LoadTrace("scoped-stratum", diagnostic, asJson: true);
        Assert.True(json.ExitCode == 0, json.Error);
        var loaded = ProjectionJson.Deserialize<WordTraceResponse>(json.Output)!;
        var reference = Assert.Single(loaded.Reading.Refs);
        var sourceReference = Assert.Single(WordTraceQuery.LoadDiagnostic(diagnostic).Value!.Reading.Refs);
        Assert.Equal(sourceReference.Id, reference.Id);
        Assert.Contains("grammarHash", reference.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraceRenderer_ShowsEachBaselineTimestampOnce()
    {
        var (project, _) = TracedProject();

        var text = await CliInProcess.RunAsync(_managedRoot, null, false,
            "trace", "--project", project, "--word", SeededProject.FirstForm);
        var json = await CliInProcess.RunAsync(_managedRoot, null, false,
            "trace", "--project", project, "--word", SeededProject.FirstForm, "--json");

        Assert.True(text.ExitCode == 0, text.Error);
        Assert.True(json.ExitCode == 0, json.Error);
        var baseline = ProjectionJson.Deserialize<WordTraceResponse>(json.Output)!.HostCapture!.Baseline!;
        Assert.Equal(1, Count(text.Output, baseline.Token.CapturedUtc.ToString()));
        Assert.Equal(1, Count(text.Output, baseline.SourceLastWriteUtc.ToString("O")));
        Assert.DoesNotContain(baseline.CaptureDescription, text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraceRenderer_RefusalShowsWorkingCaptureCommandAndCaptureEnablesTrace()
    {
        var project = pristine.CopyProjectFile();

        var refusalResult = await CliInProcess.RunAsync(_managedRoot, null, false,
            "trace", "--project", project, "--word", SeededProject.FirstForm, "--json");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), refusalResult.ExitCode);
        var refusal = ProjectionJson.Deserialize<FailureEnvelope>(refusalResult.Error)!;
        Assert.Equal("wordtrace.no-baseline", refusal.Code);
        Assert.Contains("motif baseline capture --project <fwdata>", refusal.Message, StringComparison.Ordinal);

        var captureResult = await CliInProcess.RunAsync(_managedRoot, null, false,
            "baseline", "capture", "--project", project, "--json");
        Assert.True(captureResult.ExitCode == 0, captureResult.Error);
        var baseline = ProjectionJson.Deserialize<BaselineCaptureResponse>(captureResult.Output)!;
        ConfigureTracedParser(baseline);

        var traceResult = await CliInProcess.RunAsync(_managedRoot, null, false,
            "trace", "--project", project, "--word", SeededProject.FirstForm, "--json");
        Assert.True(traceResult.ExitCode == 0, traceResult.Error);
        Assert.True(ProjectionJson.Deserialize<WordTraceResponse>(traceResult.Output)!.Parsed);
    }

    [Fact]
    public async Task TraceJsonOnTheSeededProjectMatchesTheGolden()
    {
        var (project, baseline) = TracedProject();

        var result = await CliInProcess.RunAsync(_managedRoot, null, false,
            "trace", "--project", project, "--word", SeededProject.FirstForm, "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        var actual = Normalize(result.Output, baseline);
        var golden = Path.Combine(AppContext.BaseDirectory, "TestFixtures", GoldenFile);
        var expected = File.Exists(golden) ? File.ReadAllText(golden) : string.Empty;
        if (actual != expected.ReplaceLineEndings("\n"))
        {
            var written = Path.Combine(Path.GetTempPath(), $"{Environment.ProcessId}-{GoldenFile}");
            File.WriteAllText(written, actual);
            Assert.Fail($"motif trace --json no longer matches {GoldenFile}; the new output is at {written}.");
        }
    }

    [Fact]
    public async Task TraceTextNamesTheParseAndWhereEachNameOpens()
    {
        var (project, _) = TracedProject();

        var result = await CliInProcess.RunAsync(_managedRoot, null, false,
            "trace", "--project", project, "--word", SeededProject.FirstForm);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Trace of motifa: parsed, 1 analysis", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Analysis: motifa ({SeededProject.FirstGloss})", result.Output, StringComparison.Ordinal);
        Assert.Contains("opens in Lexicon Edit: silfw:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASavedTraceLoadsIntoTheReadingTheWindowBuildsFromIt()
    {
        Directory.CreateDirectory(_managedRoot);
        var fixture = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-kumata.json");
        var path = Path.Combine(_managedRoot, "adapted-trace.json");
        File.WriteAllText(path, SIL.Motif.Tests.TestFixtures.TestTraceFixture.Read(fixture));

        var result = await CliInProcess.RunAsync(_managedRoot, null, false, "trace", "--load", path, "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        var loaded = ProjectionJson.Deserialize<WordTraceResponse>(result.Output)!;
        var window = WordTraceQuery.LoadDiagnostic(File.ReadAllText(path)).Value!;
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.TryGetProperty("root", out _));
        Assert.False(json.RootElement.TryGetProperty("candidates", out _));
        Assert.False(json.RootElement.TryGetProperty("analyses", out _));
        var authority = json.RootElement.GetProperty("reading");
        Assert.Equal(JsonValueKind.Object, authority.GetProperty("root").ValueKind);
        Assert.Equal(JsonValueKind.Array, authority.GetProperty("attempts").ValueKind);
        Assert.Equal(JsonValueKind.Array, authority.GetProperty("analyses").ValueKind);
        Assert.Equal(ProjectionJson.Serialize(window.Reading!), ProjectionJson.Serialize(loaded.Reading!));
        Assert.NotEmpty(loaded.Reading!.Refs);
    }

    [Fact]
    public async Task AMissingSavedTraceIsNotFound()
    {
        var result = await CliInProcess.RunAsync(_managedRoot, null, false,
            "trace", "--load", Path.Combine(_managedRoot, "absent.json"), "--json");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.NotFound), result.ExitCode);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal("wordtrace.diagnostic-unreadable", envelope.Code);
    }

    [Theory]
    [InlineData("trace", "--project", "x.fwdata")]
    [InlineData("trace", "--word", "motifa")]
    [InlineData("trace", "--load", "x.json", "--word", "motifa")]
    public async Task InvalidTraceArgumentsPrintTheUsageLine(params string[] arguments)
    {
        var result = await CliInProcess.RunAsync(_managedRoot, null, false, arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif trace", result.Error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<CliProcessResult> LoadTrace(string name, string diagnostic, bool asJson = false)
    {
        Directory.CreateDirectory(_managedRoot);
        var path = Path.Combine(_managedRoot, name + ".json");
        File.WriteAllText(path, diagnostic);
        return asJson
            ? await CliInProcess.RunAsync(_managedRoot, null, false, "trace", "--load", path, "--json")
            : await CliInProcess.RunAsync(_managedRoot, null, false, "trace", "--load", path);
    }

    private static string Envelope(string word, string? trace, bool capped = false, bool invalidShape = false)
    {
        var envelope = JsonNode.Parse(TraceEnvelope.Of("", trace, capped, invalidShape))!.AsObject();
        envelope["word"] = word;
        return envelope.ToJsonString();
    }

    private static string LookupTree(IEnumerable<string> shapes) => new JsonObject
    {
        ["type"] = "WordAnalysis",
        ["children"] = new JsonArray(shapes.Select(shape => (JsonNode?)new JsonObject
        {
            ["type"] = "LexicalLookup",
            ["source"] = "Morphology",
            ["inputShape"] = shape,
            ["sourceIdentity"] = new JsonObject
            {
                ["kind"] = "stratum", ["id"] = "0", ["quality"] = "grammar-local",
            },
            ["lookupResult"] = new JsonObject
            {
                ["status"] = "zeroMatches", ["completed"] = true, ["matchCount"] = 0, ["mode"] = "lexicon",
            },
            ["children"] = new JsonArray(),
        }).ToArray()),
    }.ToJsonString();

    private static string FailureTree(IEnumerable<string> codes)
    {
        var children = new List<JsonNode?>
        {
            new JsonObject
            {
                ["type"] = "MorphologicalRuleSynthesis", ["source"] = "Sibling rule",
                ["failureReason"] = "Pattern", ["children"] = new JsonArray(),
            },
        };
        children.AddRange(codes.Select(code => (JsonNode?)new JsonObject
        {
            ["type"] = "Failed",
            ["failureReason"] = code,
            ["failureContext"] = new JsonObject
            {
                ["kind"] = "decisionGate", ["source"] = "rejection-owner", ["reasonCode"] = code,
                ["status"] = "unavailable", ["unavailableReason"] = "not-captured",
            },
            ["children"] = new JsonArray(),
        }));
        return new JsonObject { ["type"] = "WordAnalysis", ["children"] = new JsonArray(children.ToArray()) }
            .ToJsonString();
    }

    private static string PartialTemplateTree() => new JsonObject
    {
        ["type"] = "WordAnalysis",
        ["children"] = new JsonArray(MatchedStemLookup("baya", new JsonObject
        {
            ["type"] = "MorphologicalRuleSynthesis", ["source"] = "prefix template", ["inputShape"] = "baya",
            ["partialParseCause"] = "ApplicableTemplatesNotApplied", ["children"] = new JsonArray(new JsonObject
            {
                ["type"] = "Failed", ["failureReason"] = "PartialParse", ["children"] = new JsonArray(),
            }),
        })),
    }.ToJsonString();

    private static string SurfaceMismatchTree() => new JsonObject
    {
        ["type"] = "WordAnalysis",
        ["children"] = new JsonArray(MatchedStemLookup("baya", new JsonObject
        {
            ["type"] = "MorphologicalRuleSynthesis", ["source"] = "prefix n-", ["inputShape"] = "baya",
            ["outputShape"] = "m+?baya", ["children"] = new JsonArray(new JsonObject
            {
                ["type"] = "Failed", ["failureReason"] = "SurfaceFormMismatch",
                ["failureContext"] = new JsonObject
                {
                    ["kind"] = "surfaceComparison", ["source"] = "surface-check",
                    ["reasonCode"] = "SurfaceFormMismatch", ["status"] = "unavailable",
                    ["unavailableReason"] = "owner-detail-not-captured",
                    ["evidence"] = new JsonObject { ["requestedShape"] = "nbaya", ["builtShape"] = "m+?baya" },
                },
                ["children"] = new JsonArray(),
            }),
        })),
    }.ToJsonString();

    private static JsonObject MatchedStemLookup(string shape, JsonObject child) => new()
    {
        ["type"] = "LexicalLookup", ["source"] = "Morphology", ["inputShape"] = shape,
        ["sourceIdentity"] = new JsonObject { ["kind"] = "stratum", ["id"] = "0", ["quality"] = "grammar-local" },
        ["attemptedMorphs"] = new JsonArray(new JsonObject
        {
            ["form"] = "baya",
            ["identity"] = new JsonObject { ["morphemeId"] = 7, ["allomorphId"] = 0, ["quality"] = "grammar-local" },
        }),
        ["lookupResult"] = new JsonObject
        {
            ["status"] = "matches", ["completed"] = true, ["matchCount"] = 1, ["mode"] = "lexicon",
        },
        ["children"] = new JsonArray(child),
    };

    private static string ScopedStratumEnvelope()
    {
        var envelope = JsonNode.Parse(Envelope("mtu", LookupTree(["mtu"])))!.AsObject();
        envelope["provenance"] = new JsonObject
        {
            ["grammar"] = new JsonObject
            {
                ["grammarHash"] = "grammar-hash-from-producer", ["grammarHashSemantics"] = "snapshot-semantic-sha256-v1",
            },
        };
        var reference = Assert.Single(WordTraceQuery.LoadDiagnostic(envelope.ToJsonString()).Value!.Reading.Refs);
        envelope["hostCapture"] = new JsonObject
        {
            ["traceLabels"] = new JsonArray(new JsonObject { ["refId"] = reference.Id, ["label"] = "Captured morphology" }),
        };
        return envelope.ToJsonString();
    }

    private static IEnumerable<TraceStep> Walk(TraceStep step) =>
        new[] { step }.Concat(step.Children.SelectMany(Walk));

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private (string Project, BaselineCaptureResponse Baseline) TracedProject()
    {
        var project = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var baseline = Assert.IsType<BaselineCaptureResponse>(captured.Value);
        ConfigureTracedParser(baseline);
        return (project, baseline);
    }

    private void ConfigureTracedParser(BaselineCaptureResponse baseline)
    {
        var seed = pristine.Seed;
        var stem = new JsonObject
        {
            ["identity"] = new JsonObject
            {
                ["entryId"] = seed.FirstEntryId.ToString("D"),
                ["formId"] = seed.FirstLexemeFormId.ToString("D"),
                ["quality"] = "authored",
            },
            ["form"] = SeededProject.FirstForm,
            ["gloss"] = SeededProject.FirstGloss,
        };
        var lookup = new JsonObject
        {
            ["type"] = "LexicalLookup", ["source"] = "Morphology", ["inputShape"] = SeededProject.FirstForm,
            ["sourceIdentity"] = new JsonObject { ["kind"] = "stratum", ["id"] = "0", ["quality"] = "grammar-local" },
            ["attemptedMorphs"] = new JsonArray(stem.DeepClone()),
            ["children"] = new JsonArray(new JsonObject
            {
                ["type"] = "Successful", ["outputShape"] = SeededProject.FirstForm, ["children"] = new JsonArray(),
            }),
        };
        var tree = new JsonObject
        {
            ["type"] = "WordAnalysis", ["inputShape"] = SeededProject.FirstForm, ["children"] = new JsonArray(lookup),
        };
        var analysis = new JsonObject
        {
            ["analysisId"] = "analysis-0", ["index"] = 0, ["surface"] = SeededProject.FirstForm,
            ["morphs"] = new JsonArray(stem),
        };
        FakeParser.Behave(Path.GetDirectoryName(baseline.FwDataPath)!, new
        {
            traceSignature = "motifa|motifa",
            traceJson = tree.ToJsonString(),
            traceAnalyses = new[] { JsonDocument.Parse(analysis.ToJsonString()).RootElement },
        });
    }

    // Identities the seed makes afresh each run become their names; times and digests become placeholders.
    private string Normalize(string output, BaselineCaptureResponse baseline)
    {
        var seed = pristine.Seed;
        var text = output
            .Replace(seed.FirstEntryId.ToString("D"), "{FirstEntryId}", StringComparison.OrdinalIgnoreCase)
            .Replace(seed.FirstLexemeFormId.ToString("D"), "{FirstLexemeFormId}", StringComparison.OrdinalIgnoreCase)
            .Replace(baseline.Token.ProjectIdentity, "{ProjectIdentity}", StringComparison.OrdinalIgnoreCase);
        var documentScope = JsonNode.Parse(output)!["reading"]!["root"]!["documentScope"]!.GetValue<string>();
        text = text.Replace(documentScope, "{DiagnosticScope}", StringComparison.Ordinal);
        var root = JsonNode.Parse(text)!;
        Blank(root);
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).ReplaceLineEndings("\n") + "\n";

        static void Blank(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject value:
                    foreach (var name in value.Select(property => property.Key).ToArray())
                    {
                        if (name is "elapsedMs" or "capturedUtc" or "wallElapsedMs" or "bundleDigest" or "diagnosticJson" or "sourceLastWriteUtc" or "publishedUtc" or "semanticSnapshotDigest" or "captureDescription")
                            value[name] = "<varies>";
                        else
                            Blank(value[name]);
                    }
                    break;
                case JsonArray items:
                    foreach (var item in items) Blank(item);
                    break;
            }
        }
    }
}
