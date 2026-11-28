using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// What <c>generate --batch</c> makes of PanGloss's batch rows: every surface with its ids and rule path, a none row
/// with its cause, an error row kept, and a bounded summary. The rows are recorded from PanGloss v0.35.6 on a synthetic
/// snapshot, so no project or parser runs.
/// </summary>
public sealed class GenerateBatchQueryTests
{
    private const string Entry = "00000000-0000-0000-0000-000000000040";
    private const string Msa = "00000000-0000-0000-0000-000000000042";
    private const string AffixEntry = "00000000-0000-0000-0000-000000000050";
    private const string AffixMsa = "00000000-0000-0000-0000-000000000052";

    private static readonly ResolvedMorpheme Affix = new(AffixEntry, AffixMsa, "MoInflAffMsa", "-s");

    private const string OkRow = $$"""
        {"cause":null,"cellIndex":0,"comboKey":"{{AffixMsa}}","complete":true,"id":"a","line":1,"op":"generate","seeds":[{"allomorphs":[
         {"allomorphId":1,"entryGuid":"{{Entry}}","formGuid":"00000000-0000-0000-0000-000000000041","msaGuid":"{{Msa}}","role":"root","ruleName":null,"shape":"ranna"},
         {"allomorphId":null,"entryGuid":"{{AffixEntry}}","formGuid":null,"msaGuid":"{{AffixMsa}}","role":"rule","ruleName":"-s","shape":null}],
         "seedKey":"r0/p0","surfaces":[{"alternatives":[],"derivations":1,"steps":[{"input":"ranna","output":"ranna+s","rule":{"guid":"{{AffixMsa}}","id":"x","kind":"morph","name":"-s"},"stratum":"Morphology","subrule":0}],
         "stepsUnresolved":false,"surface":"rannas"}],"traceTruncated":false}],"status":"ok","stopReason":null,
         "target":{"entryGuid":"{{Entry}}","msaGuid":"{{Msa}}","senseGuid":"00000000-0000-0000-0000-000000000043"},"type":"result"}
        """;

    private const string NoneRow = $$"""
        {"cause":{"code":"generated_empty","detail":"1 seed choices ran and none produced a surface"},"cellIndex":0,"comboKey":"-","complete":true,"id":"a","line":1,
         "seeds":[{"allomorphs":[],"seedKey":"r0/p0","surfaces":[],"traceTruncated":false}],"status":"none","stopReason":null,
         "target":{"entryGuid":"{{Entry}}","msaGuid":"{{Msa}}"},"type":"result"}
        """;

    private const string CapRow = $$"""
        {"cause":{"code":"cap","detail":"work_limit"},"complete":false,"id":"b","line":2,"seeds":[],"status":"none","stopReason":"work_limit",
         "target":{"entryGuid":"{{Entry}}","msaGuid":"{{Msa}}"},"type":"result"}
        """;

    private const string ErrorRow = """
        {"cause":{"code":"unknown_entry","message":"no compiled source entry 99"},"code":"unknown_entry","id":"c","line":3,"message":"no compiled source entry 99","status":"error","type":"error"}
        """;

    private static GenerateBatchQuery.Planned PlanWithCell() => new(
        [new GenerateBatchQuery.PlannedLine(1, "a", Entry, "00000000-0000-0000-0000-000000000043", null, [[Affix]]), null, null],
        [], false);

    [Fact]
    public void AnOkRowKeepsEverySurfaceWithItsAllomorphIdsRuleStepsAndCell()
    {
        var row = GenerateBatchQuery.Project(OkRow, PlanWithCell());

        Assert.Equal("ok", row.Status);
        Assert.Null(row.Cause);
        Assert.Equal(1, row.Line);
        Assert.Equal(Msa, row.Target.Msa);
        Assert.Equal("00000000-0000-0000-0000-000000000043", row.Target.Sense);
        Assert.Equal(AffixMsa, row.ComboKey);
        Assert.Equal("-s", Assert.Single(row.Cell!).Form);
        var surface = Assert.Single(row.Surfaces);
        Assert.Equal("rannas", surface.Surface);
        Assert.Equal(Entry, surface.Allomorphs[0].EntryGuid);
        Assert.Equal("-s", surface.Steps.Single().Rule.Name);
    }

    [Fact]
    public void ANoneRowSaysWhyAndAnErrorRowIsKept()
    {
        var none = GenerateBatchQuery.Project(NoneRow, PlanWithCell());
        var error = GenerateBatchQuery.Project(ErrorRow, PlanWithCell());

        Assert.Equal("none", none.Status);
        Assert.Equal("generated_empty", none.Cause!.Code);
        Assert.Empty(none.Surfaces);
        Assert.Equal("error", error.Status);
        Assert.Equal("unknown_entry", error.Cause!.Code);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void AMalformedParserRowBecomesAnErrorRowNotAGuess()
    {
        var row = GenerateBatchQuery.Project("""{"line":4,"id":"d","status":"ok","type":"result"}""", PlanWithCell());

        Assert.Equal("error", row.Status);
        Assert.Equal("parser_row_unreadable", row.Cause!.Code);
        Assert.Equal(4, row.Line);
    }

    [Fact]
    public void TheSummaryCountsStatusesAndCausesQuotesTheFirstAndSaysWhenItIsIncomplete()
    {
        var plan = PlanWithCell();
        var rows = new[] { OkRow, NoneRow, CapRow, ErrorRow }.Select(json => GenerateBatchQuery.Project(json, plan)).ToArray();

        var summary = GenerateBatchQuery.Summarize("/tmp/out.jsonl", rows, "sha256:ab", "0.35.6", "baseline");

        Assert.Equal(4, summary.Rows);
        Assert.Equal(1, summary.Surfaces);
        Assert.Equal(1, summary.Incomplete);
        Assert.Equal([1, 2, 1], [summary.Status["error"], summary.Status["none"], summary.Status["ok"]]);
        Assert.Equal(1, summary.Causes["generated_empty"]);
        Assert.Equal(2, summary.FirstNone.Count);
        Assert.Equal("unknown_entry", Assert.Single(summary.FirstErrors).Cause.Code);
        Assert.False(summary.Complete);
    }

    [Fact]
    public void ARequestLineNamesTheTargetCellAndAlwaysAsksForTheTrace()
    {
        var line = new GenerateBatchQuery.PlannedLine(1, "a", Entry, "s1", null, [[Affix], []]).ToLine(500, 9);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal(Entry, root.GetProperty("target").GetProperty("entryGuid").GetString());
        Assert.Equal("s1", root.GetProperty("target").GetProperty("senseGuid").GetString());
        Assert.Equal(2, root.GetProperty("cell").GetProperty("msas").GetArrayLength());
        Assert.Equal(AffixMsa, root.GetProperty("cell").GetProperty("msas")[0][0].GetProperty("msaGuid").GetString());
        Assert.True(root.GetProperty("trace").GetBoolean());
        Assert.Equal(500, root.GetProperty("wordTimeoutMs").GetInt32());
        Assert.Equal(9, root.GetProperty("maxWork").GetInt32());
    }

    [Fact]
    public void TheParserRequestNamesGenerateRequestsAndOut()
    {
        var info = new System.Diagnostics.ProcessStartInfo();

        new PanGlossRequest.GenerateRequests("g.fwdata", [], "/o/rows.jsonl").AddArguments(info, "/s");

        Assert.Equal(["generate", "g.fwdata", "--requests", Path.Combine("/s", "requests.jsonl"), "--out", "/o/rows.jsonl"], info.ArgumentList);
    }

    [Fact]
    public void TheArtifactHoldsOneJsonRowPerLineInTheOrderAdded()
    {
        var plan = PlanWithCell();
        var rows = new[] { OkRow, ErrorRow }.Select(json => GenerateBatchQuery.Project(json, plan)).ToArray();
        var path = Path.Combine(Path.GetTempPath(), "motif-generate-batch-" + Guid.NewGuid().ToString("N"), "rows.jsonl");
        try
        {
            using (var writer = new GenerateBatchArtifact.Writer(path))
            {
                foreach (var row in rows) writer.Add(row);
                writer.Commit();
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            using var first = JsonDocument.Parse(lines[0]);
            Assert.Equal("rannas", first.RootElement.GetProperty("surfaces")[0].GetProperty("surface").GetString());
            Assert.Equal(GenerateBatchSchema.RowId, first.RootElement.GetProperty("schema").GetString());
            using var second = JsonDocument.Parse(lines[1]);
            Assert.Equal("unknown_entry", second.RootElement.GetProperty("cause").GetProperty("code").GetString());
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    private sealed class FakeInvoker(Func<PanGlossRequest.GenerateRequests, PanGlossOutcome> run) : IPanGlossInvoker
    {
        public TimeSpan? Cap { get; private set; }

        public Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            Cap = wallClockCap;
            return Task.FromResult(run((PanGlossRequest.GenerateRequests)request));
        }
    }

    private static PanGlossOutcome Completed() => new PanGlossOutcome.Completed(string.Empty, string.Empty, TimeSpan.Zero);

    private static GenerateBatchQuery.Planned PlanOfLines(int count) => new(
        Enumerable.Range(1, count).Select(n => (GenerateBatchQuery.PlannedLine?)new GenerateBatchQuery.PlannedLine(n, "r" + n, Entry, null, Msa, null)).ToArray(),
        [], false);

    private static GenerateBatchResponse Run(GenerateBatchQuery.Planned plan, FakeInvoker invoker, string outPath, int? timeoutMs = null)
    {
        var outcome = GenerateBatchQuery.Generate(new GenerateBatchRequest("p", "b", outPath, null, timeoutMs), invoker, "0.35.6", "g.fwdata",
            "sha256:ab", "baseline", plan, CancellationToken.None);
        return Assert.IsType<GenerateBatchResponse>(outcome.Value);
    }

    private static string OutPath() => Path.Combine(Path.GetTempPath(), "motif-generate-batch-" + Guid.NewGuid().ToString("N"), "out.jsonl");

    private static string NoneAt(int line) =>
        NoneRow.Replace("\"line\":1", "\"line\":" + line).Replace("\"id\":\"a\"", "\"id\":\"r" + line + "\"").Replace("\r", " ").Replace("\n", " ");

    [Fact]
    public void RowsStreamToTheArtifactWhileTheSummaryKeepsOnlyCountersAndTheFirstFive()
    {
        var path = OutPath();
        try
        {
            var plan = PlanOfLines(40);
            var invoker = new FakeInvoker(request =>
            {
                File.WriteAllLines(request.OutPath, Enumerable.Range(1, 40).Select(NoneAt));
                return Completed();
            });

            var summary = Run(plan, invoker, path);

            Assert.Equal(40, summary.Rows);
            Assert.Equal(40, summary.Status["none"]);
            Assert.Equal(40, File.ReadAllLines(path).Length);
            Assert.Equal([1, 2, 3, 4, 5], summary.FirstNone.Select(row => row.Line));
            Assert.Empty(summary.FirstErrors);
            Assert.Null(summary.StopReason);
            Assert.True(summary.Complete);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void ATimeoutKeepsTheRowsAlreadyWrittenAndSaysTheRunStoppedEarly()
    {
        var path = OutPath();
        try
        {
            var plan = PlanOfLines(3);
            var invoker = new FakeInvoker(request =>
            {
                // Two whole rows, then the half row a kill leaves behind.
                File.WriteAllText(request.OutPath, NoneAt(1).Replace("\n", " ") + "\n" + NoneAt(2).Replace("\n", " ") + "\n{\"line\":3,\"sta");
                return new PanGlossOutcome.TimedOut(TimeSpan.FromMinutes(10), "pangloss generate did not finish.");
            });

            var summary = Run(plan, invoker, path);

            Assert.Equal("timed_out", summary.StopReason);
            Assert.False(summary.Complete);
            Assert.Equal(path, summary.Out);
            Assert.Equal(2, summary.Status["none"]);
            Assert.Equal("no-result", Assert.Single(summary.FirstErrors).Cause.Code);
            Assert.Equal(3, File.ReadAllLines(path).Length);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void ASentLineWithNoParserRowBecomesANoResultErrorAndTheRunIsNotComplete()
    {
        var path = OutPath();
        try
        {
            var plan = PlanOfLines(3);
            var invoker = new FakeInvoker(request =>
            {
                File.WriteAllLines(request.OutPath, [NoneAt(1), NoneAt(3)]);
                return Completed();
            });

            var summary = Run(plan, invoker, path);

            var missing = Assert.Single(summary.FirstErrors);
            Assert.Equal(("no-result", 2, "r2"), (missing.Cause.Code, missing.Line, missing.Id));
            Assert.Equal(1, summary.Status["error"]);
            Assert.False(summary.Complete);
            Assert.Null(summary.StopReason);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void ARunThatWroteNothingAndWasStoppedIsStillRefused()
    {
        var path = OutPath();
        var invoker = new FakeInvoker(_ => new PanGlossOutcome.TimedOut(TimeSpan.FromMinutes(10), "no rows."));

        var outcome = GenerateBatchQuery.Generate(new GenerateBatchRequest("p", "b", path), invoker, "0.35.6", "g", "h", "baseline",
            PlanOfLines(1), CancellationToken.None);

        Assert.NotNull(outcome.Refusal);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void TheWallClockScalesWithTheRowsSentAndHonoursTheTimeout()
    {
        Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(10 * 60), GenerateBatchQuery.WallClockFor(60, null));
        Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2 * 100), GenerateBatchQuery.WallClockFor(100, 2000));
        Assert.Equal(TimeSpan.FromHours(24), GenerateBatchQuery.WallClockFor(100_000, 240000));
    }

    [Fact]
    public void ABatchIoFailureHasItsOwnRefusalCodeThatTheCatalogRegisters()
    {
        var folder = Path.Combine(Path.GetTempPath(), "motif-generate-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var outcome = GenerateBatchQuery.Generate(new GenerateBatchRequest("p", "b", folder), new FakeInvoker(_ => Completed()), "0.35.6", "g",
                "h", "baseline", PlanOfLines(0), CancellationToken.None);

            Assert.Equal(RefusalCodes.GenerateBatchIo, outcome.Refusal!.Code);
            Assert.True(RefusalCatalog.Specs.ContainsKey(RefusalCodes.GenerateBatchIo));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
