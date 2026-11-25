using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The mapping of PanGloss's rejection trace onto Motif's response. Each acceptance-row shape (a completed lookup
/// beside an unapplied template, an accepted sister, a requested-versus-built surface, a returned competing reading,
/// two distinct outputs) is built from synthetic GUIDs, never from a language's data.
/// </summary>
public sealed class RejectionTraceTests
{
    private const string Build = "b0b0";
    private const string Hash = "h1";
    private static string Key(string kind, string tail) => $"{Build}:snapshot-semantic-sha256-v1:{Hash}:{kind}:{tail}";
    private static string G(int n) => $"00000000-0000-0000-0000-{n:x12}";
    private static string Canon(int n) => ToCanonical(Guid.Parse(G(n)));

    // Independent of CanonicalId: big-endian bytes of the textual GUID, base64url without padding.
    private static string ToCanonical(Guid guid)
    {
        var hex = guid.ToString("N");
        var bytes = Enumerable.Range(0, 16).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class Trace
    {
        public readonly List<JsonObject> Records = [];
        public string Status = "complete";
        public string SearchStatus = "complete";
        public string[] StopReasons = [];
        public JsonObject? WorkCap;

        public Trace Add(JsonObject record) { Records.Add(record); return this; }

        public Trace Authored(string kind, int guid) => Add(new JsonObject
        {
            ["type"] = "mapping", ["row"] = 0,
            ["object"] = new JsonObject
            {
                ["kind"] = kind, ["key"] = G(guid), ["quality"] = "authored", ["bindingStatus"] = "exact",
                ["sources"] = new JsonArray(new JsonObject
                    { ["guid"] = G(guid), ["kind"] = kind, ["ordinal"] = 0, ["relation"] = "authored" }),
            },
        });

        public Trace Structural(string kind, string tail) => Add(new JsonObject
        {
            ["type"] = "mapping", ["row"] = 0,
            ["object"] = new JsonObject
            {
                ["kind"] = kind, ["key"] = Key(kind, tail), ["quality"] = "synthetic", ["bindingStatus"] = "structural",
                ["sources"] = new JsonArray(),
            },
        });

        public Trace Allomorph(string tail, int form, int entry, int msa) => Add(new JsonObject
        {
            ["type"] = "mapping", ["row"] = 0,
            ["object"] = new JsonObject
            {
                ["kind"] = "allomorph", ["key"] = Key("allomorph", tail), ["quality"] = "compiled",
                ["bindingStatus"] = "exact",
                ["sources"] = new JsonArray(
                    Source("form", form, "form_part"), Source("entry", entry, "owning_entry"), Source("msa", msa, "msa")),
            },
        });

        public Trace Path(int id, string outcome, int? firstReject = null, int? outputId = null, int? parent = 0) => Add(new JsonObject
        {
            ["type"] = "path", ["row"] = 0, ["pathId"] = id, ["outcome"] = outcome, ["depth"] = parent is null ? 0 : 1,
            ["parentPathId"] = parent, ["parentStatus"] = parent is null ? "root" : "retained",
            ["firstRejectEventId"] = firstReject, ["outputId"] = outputId,
            ["outputStatus"] = outputId is null ? "not_applicable" : "retained",
        });

        public Trace Event(int id, int path, string kind, string stage, string reason, JsonNode operands,
            params (string Role, string Kind, string Key)[] refs) => Add(new JsonObject
        {
            ["type"] = "event", ["row"] = 0, ["eventId"] = id, ["pathId"] = path, ["direction"] = "analysis",
            ["kind"] = kind, ["stage"] = stage, ["reason"] = reason, ["operands"] = operands,
            ["objectRefs"] = new JsonArray(refs.Select(r => (JsonNode)new JsonObject
                { ["role"] = r.Role, ["kind"] = r.Kind, ["key"] = r.Key }).ToArray()),
        });

        public Trace Output(int id, string surface, params (int Entry, int Msa, int Form)[] morphs) => Add(new JsonObject
        {
            ["type"] = "output", ["row"] = 0, ["outputId"] = id, ["surface"] = surface, ["rootIndex"] = 0,
            ["projection"] = "exact",
            ["morphs"] = new JsonArray(morphs.Select(m => (JsonNode)new JsonObject
            {
                ["kind"] = "authored", ["senseResolution"] = "unique_association",
                ["associatedSenseGuids"] = new JsonArray(G(900 + m.Entry)),
                ["entry"] = new JsonObject { ["kind"] = "entry", ["key"] = G(m.Entry) },
                ["msa"] = new JsonObject { ["kind"] = "msa", ["key"] = G(m.Msa) },
                ["forms"] = new JsonArray(new JsonObject { ["kind"] = "form", ["key"] = G(m.Form) }),
            }).ToArray()),
        });

        public PanGlossOutcome.Completed Completed(int outputs = 0, string word = "w")
        {
            var header = new JsonObject
            {
                ["type"] = "header", ["schemaVersion"] = "pangloss.diagnostic.v1", ["mode"] = "lineage",
                ["parserVersion"] = "0.27.0", ["parserBuild"] = Build,
                ["grammar"] = new JsonObject { ["hash"] = Hash, ["semantics"] = "snapshot-semantic-sha256-v1" },
            };
            if (WorkCap is not null) header["searchOptions"] = new JsonObject { ["workCap"] = WorkCap };
            var wordEnd = new JsonObject
            {
                ["type"] = "word_end", ["row"] = 0,
                ["search"] = new JsonObject
                {
                    ["status"] = SearchStatus, ["outputCensus"] = "complete", ["outputCount"] = outputs,
                    ["workUnits"] = 10, ["stopReasons"] = new JsonArray(StopReasons.Select(r => (JsonNode)r).ToArray()),
                    ["error"] = null,
                },
                ["capture"] = new JsonObject
                {
                    ["coverage"] = "lineage", ["truncatedBy"] = new JsonArray(),
                    ["omittedPaths"] = Count(), ["omittedEvents"] = Count(), ["omittedOutputs"] = Count(),
                    ["omittedInputs"] = Count(),
                },
            };
            var footer = new JsonObject { ["type"] = "footer", ["status"] = Status, ["error"] = null };
            var lines = new[] { header }.Concat(Records).Append(wordEnd).Append(footer).Select(r => r.ToJsonString());
            return new PanGlossOutcome.Completed(string.Join('\n', lines) + "\n", string.Empty, TimeSpan.Zero);
        }

        private static JsonObject Count() => new() { ["value"] = 0, ["accuracy"] = "exact" };

        private static JsonObject Source(string kind, int guid, string relation) =>
            new() { ["guid"] = G(guid), ["kind"] = kind, ["ordinal"] = 0, ["relation"] = relation };
    }

    private static JsonObject Lookup(int count) => new()
        { ["kind"] = "lookup", ["status"] = "captured", ["materializedCount"] = count, ["root"] = null };

    private static RejectionTraceResponse Project(PanGlossOutcome outcome, int maxPaths = 16)
    {
        var result = RejectionTraceQuery.Shape(outcome, "p", "w", maxPaths, "baseline");
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }

    [Fact]
    public void ACompletedLookupBesideAnUnappliedTemplateStaysTwoIndependentWitnesses()
    {
        var trace = new Trace()
            .Authored("template", 14).Allomorph("a0", 31, 30, 32)
            .Structural("stratum", "0")
            .Path(0, "intermediate", parent: null)
            .Path(1, "unresolved")
            .Event(0, 1, "observe", "lookup", "lookup_completed", Lookup(1), ("subject", "stratum", Key("stratum", "0")),
                ("candidate", "allomorph", Key("allomorph", "a0")))
            .Path(2, "rejected", firstReject: 1)
            .Event(1, 2, "reject", "completion", "applicable_templates_not_applied",
                new JsonObject { ["kind"] = "template_slot", ["status"] = "captured", ["clause"] = "applicable" },
                ("subject", "stratum", Key("stratum", "0")), ("tested_template", "template", G(14)));

        var response = Project(trace.Completed());

        Assert.Equal(2, response.Paths.Count);
        var unresolved = response.Paths.Single(p => p.Outcome == "unresolved");
        Assert.Equal("unknown", unresolved.Attribution);
        Assert.Null(unresolved.EarliestRejection);
        var rejected = response.Paths.Single(p => p.Outcome == "rejected");
        Assert.Equal("applicable_templates_not_applied", rejected.EarliestRejection!.Reason);
        Assert.Equal("rejected", rejected.Attribution);
        var template = Assert.Single(rejected.EarliestRejection.Related).Object;
        Assert.Equal(Canon(14), Assert.Single(template.Ids).Id);
        Assert.True(template.Resolved);
    }

    [Fact]
    public void AnAcceptedSisterNamesItsMorphsByCanonicalId()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null).Path(1, "accepted", outputId: 0)
            .Authored("entry", 30).Authored("msa", 32).Authored("form", 31)
            .Authored("entry", 40).Authored("msa", 42).Authored("form", 41)
            .Output(0, "hangutun", (30, 32, 31), (40, 42, 41));

        var response = Project(trace.Completed(outputs: 1));

        var output = Assert.Single(response.Outputs);
        Assert.Equal("hangutun", output.Surface);
        Assert.Equal([Canon(30), Canon(40)], output.Morphs.Select(m => m.Entry!.Ids.Single().Id));
        Assert.Equal([Canon(32), Canon(42)], output.Morphs.Select(m => m.Msa!.Ids.Single().Id));
        Assert.Equal("accepted", Assert.Single(response.Paths).Attribution);
        Assert.Equal(0, response.Paths[0].OutputId);
    }

    [Fact]
    public void ARequestedVersusBuiltSurfaceKeepsBothShapes()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null).Structural("stratum", "1")
            .Path(1, "rejected", firstReject: 0)
            .Event(0, 1, "reject", "surface", "surface_mismatch",
                new JsonObject
                {
                    ["kind"] = "shape", ["status"] = "captured", ["requested"] = "apusun",
                    ["built"] = new JsonObject { ["value"] = "apsun", ["kind"] = "literal" },
                }, ("subject", "stratum", Key("stratum", "1")));

        var rejection = Project(trace.Completed()).Paths.Single().EarliestRejection!;

        Assert.Equal("apusun", rejection.Operands["requested"]);
        Assert.Equal("apsun", rejection.Operands["built"]);
        Assert.Contains("differs from the surface requested", rejection.Sentence);
    }

    [Fact]
    public void AReturnedCompetingReadingSitsBesideTheRejectedPaths()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null).Path(1, "accepted", outputId: 0)
            .Authored("entry", 50).Authored("msa", 52).Authored("form", 51).Output(0, "kinabuhi", (50, 52, 51))
            .Structural("stratum", "0").Path(2, "rejected", firstReject: 0)
            .Event(0, 2, "reject", "lookup", "lookup_no_seed", Lookup(0), ("subject", "stratum", Key("stratum", "0")));

        var response = Project(trace.Completed(outputs: 1));

        Assert.Single(response.Outputs);
        Assert.Contains(response.Paths, p => p.Attribution == "accepted");
        Assert.Contains(response.Paths, p => p.Attribution == "rejected");
        Assert.Contains(response.Notes, n => n.Contains("has outputs"));
    }

    [Fact]
    public void TwoDistinctOutputsStaySeparate()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null)
            .Path(1, "accepted", outputId: 0).Path(2, "accepted", outputId: 1)
            .Authored("entry", 60).Authored("msa", 62).Authored("form", 61)
            .Authored("entry", 70).Authored("msa", 72).Authored("form", 71)
            .Output(0, "mbako", (60, 62, 61)).Output(1, "mbako", (70, 72, 71));

        var response = Project(trace.Completed(outputs: 2));

        Assert.Equal([0, 1], response.Outputs.Select(o => o.OutputId));
        Assert.NotEqual(response.Outputs[0].Morphs[0].Entry!.Ids[0].Id, response.Outputs[1].Morphs[0].Entry!.Ids[0].Id);
    }

    [Fact]
    public void AnUnboundObjectRendersAsUnknownWithItsCompiledKey()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null).Structural("stratum", "0")
            .Path(1, "rejected", firstReject: 0)
            .Event(0, 1, "reject", "lookup", "lookup_no_seed", Lookup(0), ("subject", "stratum", Key("stratum", "0")));

        var subject = Project(trace.Completed()).Paths.Single().EarliestRejection!.Subject;

        Assert.False(subject.Resolved);
        Assert.Empty(subject.Ids);
        Assert.StartsWith("unknown:", subject.Unknown);
        Assert.Equal(Key("stratum", "0"), subject.Key);
    }

    [Fact]
    public void AnAllomorphWithOnlyRelatedLinksIsNotResolvedAsItself()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null)
            .Add(new JsonObject
            {
                ["type"] = "mapping", ["row"] = 0,
                ["object"] = new JsonObject
                {
                    ["kind"] = "allomorph", ["key"] = Key("allomorph", "z"), ["quality"] = "compiled",
                    ["bindingStatus"] = "exact",
                    ["sources"] = new JsonArray(new JsonObject
                        { ["guid"] = G(5), ["kind"] = "entry", ["ordinal"] = 0, ["relation"] = "owning_entry" }),
                },
            })
            .Path(1, "rejected", firstReject: 0)
            .Event(0, 1, "reject", "validity", "bound_root", new JsonObject { ["kind"] = "none", ["status"] = "captured" },
                ("subject", "allomorph", Key("allomorph", "z")));

        var subject = Project(trace.Completed()).Paths.Single().EarliestRejection!.Subject;

        Assert.False(subject.Resolved);
        Assert.False(Assert.Single(subject.Ids).IsSubject);
        Assert.Contains("related", subject.Unknown);
    }

    [Fact]
    public void ARejectedPathWithoutARetainedEventIsUnknownNotInvented()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null).Path(1, "rejected");

        var path = Project(trace.Completed()).Paths.Single();

        Assert.Equal("unknown", path.Attribution);
        Assert.Contains("kept no terminal event", path.Unknown);
        Assert.Null(path.EarliestRejection);
    }

    [Fact]
    public void AWordWithManyPathsIsBoundedAndSaysWhatWasLeftOut()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null).Structural("stratum", "0");
        for (var i = 0; i < 40; i++)
        {
            trace.Path(i + 1, "rejected", firstReject: i)
                .Event(i, i + 1, "reject", "lookup", "lookup_no_seed", Lookup(0), ("subject", "stratum", Key("stratum", "0")));
        }

        var response = Project(trace.Completed(), maxPaths: 16);

        Assert.Equal(16, response.Paths.Count);
        Assert.Equal(40, response.Bound.TerminalPathsRetained);
        Assert.Equal(24, response.Bound.PathsOmittedByCap);
        Assert.True(response.Bound.Truncated);
        Assert.Equal(Enumerable.Range(1, 16), response.Paths.Select(p => p.PathId));
    }

    [Fact]
    public void OutputPreviewsAreCapped()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null).Authored("entry", 1).Authored("msa", 2).Authored("form", 3);
        for (var i = 0; i < 70; i++) trace.Output(i, "w" + i, (1, 2, 3));

        var response = Project(trace.Completed(outputs: 70));

        Assert.Equal(RejectionTraceLimits.OutputPreviewCap, response.Outputs.Count);
        Assert.Equal(6, response.Bound.OutputsOmittedByCap);
    }

    [Fact]
    public void AnIncompleteSearchSaysAPathWithoutAStopIsNotADeadEnd()
    {
        var trace = new Trace { SearchStatus = "stopped" }.Path(0, "intermediate", parent: null).Path(1, "unresolved");

        var response = Project(trace.Completed());

        Assert.Equal("stopped", response.Search.Status);
        Assert.Contains(response.Notes, n => n.Contains("not a proven dead end"));
    }

    [Fact]
    public void AWordStoppedAtTheWorkLimitSaysSoWithTheWorkAndTheRecordedLimit()
    {
        var trace = new Trace
        {
            SearchStatus = "stopped", StopReasons = ["work_limit"],
            WorkCap = new JsonObject { ["state"] = "bounded", ["value"] = 2000 },
        }.Path(0, "intermediate", parent: null).Path(1, "unresolved");

        var response = Project(trace.Completed());

        Assert.True(response.Search.WorkLimitReached);
        Assert.Equal("work limit reached (10 work units against a limit of 2,000)", response.Search.Description);
        Assert.Equal(new RejectionTraceWorkCap("bounded", 2000), response.Search.WorkCap);
        Assert.Contains(response.Notes, n => n.Contains("stopped this word at its work limit"));
    }

    [Fact]
    public void AWorkLimitStopWithNoRecordedLimitSaysItWasNotRecordedNeverAGuess()
    {
        var trace = new Trace { SearchStatus = "stopped", StopReasons = ["work_limit"] }.Path(1, "unresolved");

        var response = Project(trace.Completed());

        Assert.Null(response.Search.WorkCap);
        Assert.Equal("work limit reached (10 work units against a limit the parser did not record)", response.Search.Description);
    }

    [Fact]
    public void ASearchStoppedForAnotherReasonIsNotCalledAWorkLimit()
    {
        var response = Project(new Trace { SearchStatus = "stopped", StopReasons = ["time_limit"] }
            .Path(1, "unresolved").Completed());

        Assert.False(response.Search.WorkLimitReached);
        Assert.Equal("search stopped (time_limit)", response.Search.Description);
    }

    [Fact]
    public void ACompleteSearchStillSaysSearchComplete()
    {
        var response = Project(new Trace().Path(1, "unresolved").Completed());

        Assert.Equal("search complete", response.Search.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void APathCapOutsideOneToSixtyFourIsATypedRefusal(int cap)
    {
        var result = RejectionTraceQuery.Parse(new ParseTraceRequest("p", "word", null, cap));

        Assert.Equal(RefusalCodes.RejectionTraceRequestInvalid, result.Refusal!.Code);
    }

    [Theory]
    [InlineData("-3")]
    [InlineData("many")]
    [InlineData("99999999999")]
    public void ANonNumericOrOutOfRangeCapTextIsTheTypedRefusalNotUsage(string text)
    {
        var result = RejectionTraceQuery.InvalidCap(text);

        Assert.Equal(RefusalCodes.RejectionTraceRequestInvalid, result.Refusal!.Code);
        Assert.Contains(text, result.Refusal.Message);
    }

    [Fact]
    public void AMultipleBindingWithSeveralSubjectIdsIsUnknownNotResolved()
    {
        var trace = new Trace().Path(0, "intermediate", parent: null)
            .Add(new JsonObject
            {
                ["type"] = "mapping", ["row"] = 0,
                ["object"] = new JsonObject
                {
                    ["kind"] = "allomorph", ["key"] = Key("allomorph", "m"), ["quality"] = "compiled",
                    ["bindingStatus"] = "multiple",
                    ["sources"] = new JsonArray(
                        new JsonObject { ["guid"] = G(5), ["kind"] = "allomorph", ["ordinal"] = 0, ["relation"] = "form_part" },
                        new JsonObject { ["guid"] = G(6), ["kind"] = "allomorph", ["ordinal"] = 1, ["relation"] = "form_part" }),
                },
            })
            .Path(1, "rejected", firstReject: 0)
            .Event(0, 1, "reject", "validity", "bound_root", new JsonObject { ["kind"] = "none", ["status"] = "captured" },
                ("subject", "allomorph", Key("allomorph", "m")));

        var subject = Project(trace.Completed()).Paths.Single().EarliestRejection!.Subject;

        Assert.False(subject.Resolved);
        Assert.Equal(2, subject.Ids.Count);
        Assert.StartsWith("unknown:", subject.Unknown);
    }

    [Fact]
    public void ARevisionDigestAtIsATypedRefusalNotAGuess()
    {
        var result = RejectionTraceQuery.Parse(new ParseTraceRequest("p", "w", "sha256:abc"), parserPath: null);

        Assert.Equal(RefusalCodes.RejectionTraceUnsupported, result.Refusal!.Code);
    }

    [Fact]
    public void ATypedParserErrorBecomesACaptureRefusalWithItsCode()
    {
        var refused = new PanGlossOutcome.Refused(2,
            "PANGLOSS_DIAGNOSTIC_ERROR {\"code\":\"bad_request\",\"message\":\"--trace-max-depth must be in 1..=256\"}\n",
            string.Empty, "pangloss parse exited 2");

        var result = RejectionTraceQuery.Shape(refused, "p", "w", 16, "baseline");

        Assert.Equal(RefusalCodes.RejectionTraceCaptureRefused, result.Refusal!.Code);
        Assert.Equal("bad_request", result.Refusal.Facts!["parserCode"]);
        Assert.Contains("--trace-max-depth", result.Refusal.Message);
    }

    [Fact]
    public void AnUnknownSchemaRecordTypeOrMissingFooterFailsVisibly()
    {
        var good = new Trace().Path(0, "intermediate", parent: null).Completed().Output;
        var wrongSchema = new PanGlossOutcome.Completed(good.Replace("pangloss.diagnostic.v1", "pangloss.diagnostic.v2"), "", TimeSpan.Zero);
        var unknownType = new PanGlossOutcome.Completed(good.Replace("\"type\":\"path\"", "\"type\":\"surprise\""), "", TimeSpan.Zero);
        var lines = good.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var noFooter = new PanGlossOutcome.Completed(string.Join('\n', lines.Take(lines.Length - 1)), "", TimeSpan.Zero);

        foreach (var outcome in new[] { wrongSchema, unknownType, noFooter })
            Assert.Equal(RefusalCodes.RejectionTraceMalformed,
                RejectionTraceQuery.Shape(outcome, "p", "w", 16, "baseline").Refusal!.Code);
    }

    [Fact]
    public void ARefusedFooterIsACaptureLimitRefusal()
    {
        var result = RejectionTraceQuery.Shape(new Trace { Status = "refused" }.Completed(), "p", "w", 16, "baseline");

        Assert.Equal(RefusalCodes.RejectionTraceCaptureLimit, result.Refusal!.Code);
    }

    [Fact]
    public void TheTypedRefusalsAreInTheClosedCatalog()
    {
        foreach (var code in new[]
        {
            RefusalCodes.RejectionTraceRequestInvalid, RefusalCodes.RejectionTraceUnsupported,
            RefusalCodes.RejectionTraceCaptureRefused, RefusalCodes.RejectionTraceMalformed,
            RefusalCodes.RejectionTraceCaptureLimit,
        })
            Assert.True(RefusalCatalog.Specs.ContainsKey(code), code);
    }
}

/// <summary>Skips unless the directory of captures the parser's own release probes wrote is named.</summary>
public sealed class PinnedTraceFactAttribute : FactAttribute
{
    public const string Variable = "MOTIF_TRACE_PIN_DIR";

    public PinnedTraceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)) ||
            !System.IO.Directory.Exists(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a directory of pangloss.diagnostic.v1 captures to replay them.";
    }
}

public sealed class RejectionTracePinReplayTests
{
    private static string Dir => Environment.GetEnvironmentVariable(PinnedTraceFactAttribute.Variable)!;

    private static CommandOutcome<RejectionTraceResponse> Read(string file) =>
        RejectionTraceQuery.Shape(new PanGlossOutcome.Completed(System.IO.File.ReadAllText(System.IO.Path.Combine(Dir, file)),
            string.Empty, TimeSpan.Zero), "p", "w", 16, "baseline");

    [PinnedTraceFact]
    public void EveryParserProbeCaptureProjects()
    {
        foreach (var file in System.IO.Directory.GetFiles(Dir, "*.jsonl"))
        {
            var result = Read(System.IO.Path.GetFileName(file));
            Assert.True(result.Succeeded, file + ": " + result.Refusal?.Message);
            Assert.All(result.Value!.Paths.Where(p => p.EarliestRejection is not null),
                p => Assert.False(string.IsNullOrEmpty(p.EarliestRejection!.Sentence)));
        }
    }

    [PinnedTraceFact]
    public void ATerminalLookupStopOnAStructuralStratumIsAnExplicitUnknown()
    {
        var path = Read("terminal.jsonl").Value!.Paths.Single(p => p.Outcome == "rejected");

        Assert.Equal("lookup_no_seed", path.EarliestRejection!.Reason);
        Assert.False(path.EarliestRejection.Subject.Resolved);
        Assert.StartsWith("unknown:", path.EarliestRejection.Subject.Unknown);
    }

    [PinnedTraceFact]
    public void ATargetProbeNamesItsOutputMorphsByCanonicalId()
    {
        var output = Assert.Single(Read("target.jsonl").Value!.Outputs);

        Assert.Equal("kkt", output.Surface);
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAMA", Assert.Single(output.Morphs).Entry!.Ids.Single().Id);
    }
}
