using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Cli.Integration;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class TrialReviewReportAcceptanceTests
{
    [Fact]
    public async Task EveryDisclosureLevelKeepsSameFactsAndReachesEveryCase()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var summary = await Read(fixture, "--detail", "summary");
        var failures = await Read(fixture, "--detail", "failures");
        var full = await Read(fixture, "--detail", "full");
        Assert.True(JsonElement.DeepEquals(summary.GetProperty("summary"), failures.GetProperty("summary")));
        Assert.True(JsonElement.DeepEquals(summary.GetProperty("summary"), full.GetProperty("summary")));
        var keys = new List<string>();
        string? cursor = null;
        do
        {
            var args = new List<string> { "--detail", "full", "--limit", "1" };
            if (cursor is not null) args.AddRange(["--cursor", cursor]);
            var page = (await Read(fixture, args.ToArray())).GetProperty("page");
            keys.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("caseKey").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(new[] { "approved", "homograph/a", "homograph/b", "incomplete", "negative", "reference", "unchanged" }, keys.Order().ToArray());
        foreach (var section in new[] { "controls", "negative-controls", "incomplete", "sampled-tokens", "paradigm-cells", "warnings", "parsimony", "evidence" })
        {
            cursor = null;
            long returned = 0;
            long? expected = null;
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var args = new List<string> { "--detail", "full", "--section", section, "--limit", "1" };
                if (cursor is not null) args.AddRange(["--cursor", cursor]);
                var page = (await Read(fixture, args.ToArray())).GetProperty("page");
                returned += page.GetProperty("returned").GetInt64();
                if (page.GetProperty("totalMatching").ValueKind == JsonValueKind.Number)
                    expected = page.GetProperty("totalMatching").GetInt64();
                cursor = page.GetProperty("nextCursor").GetString();
                if (cursor is not null) Assert.True(cursors.Add(cursor));
            } while (cursor is not null);
            if (expected is not null) Assert.Equal(expected.Value, returned);
        }
        foreach (var key in keys)
        foreach (var kind in new[] { "memberships", "expectations", "sources", "readings" })
        {
            cursor = null;
            long returned = 0;
            long? expected = null;
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var args = new List<string> { "--detail", "full", "--case", key, "--section", kind, "--limit", "1" };
                if (cursor is not null) args.AddRange(["--cursor", cursor]);
                var page = (await Read(fixture, args.ToArray())).GetProperty("page");
                returned += page.GetProperty("returned").GetInt64();
                if (page.GetProperty("totalMatching").ValueKind == JsonValueKind.Number)
                    expected = page.GetProperty("totalMatching").GetInt64();
                cursor = page.GetProperty("nextCursor").GetString();
                if (cursor is not null) Assert.True(cursors.Add(cursor));
            } while (cursor is not null);
            if (expected is not null) Assert.Equal(expected.Value, returned);
        }
        var readingCount = 0;
        cursor = null;
        do
        {
            var args = new List<string> { "--detail", "full", "--case", "approved", "--section", "readings", "--limit", "1" };
            if (cursor is not null) args.AddRange(["--cursor", cursor]);
            var page = (await Read(fixture, args.ToArray())).GetProperty("page");
            readingCount += page.GetProperty("items").GetArrayLength();
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.True(readingCount >= 3);
        var beforeReadings = await Read(fixture, "--case", "approved", "--side", "before");
        var afterReadings = await Read(fixture, "--case", "approved", "--side", "after");
        Assert.Equal(2, beforeReadings.GetProperty("word").GetProperty("readings").GetProperty("totalMatching").GetInt64());
        Assert.Equal(1, afterReadings.GetProperty("word").GetProperty("readings").GetProperty("totalMatching").GetInt64());
        Assert.True(summary.GetProperty("summary").GetProperty("flags").GetArrayLength() >= 4);
        Assert.NotEqual(0, summary.GetProperty("summary").GetProperty("incomplete").GetProperty("affectedWords").GetInt64());
    }

    [Fact]
    public void WorkFailuresSectionIncludesTheMeasuredWordPercentage()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var reportId = fixture.CreateWorkOnlyReport(121);
        var result = SIL.Motif.Commands.ProjectStoreCommand.Run<SIL.Motif.Contract.Review.TrialReviewResult>(
            fixture.Project, SIL.Motif.Host.MotifProductVersion.CurrentText, (database, project) =>
                new SIL.Motif.Commands.Review.TrialReviewCommands(database).Read(new ReviewReadRequest(
                    fixture.ProjectKey, fixture.Proposal, ReportId: reportId, Section: "parser-work")));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        var report = Assert.IsType<AvailableTrialResult>(result.Value).Report;
        Assert.Equal(21, report.Summary.ParserWork.PercentChange);
        var section = Assert.Single(report.Sections);
        var word = Assert.IsType<WordRow>(Assert.Single(Assert.Single(section.Groups).Samples));
        Assert.Equal("work", word.Word);
        Assert.Equal(21, word.Work.PercentChange);
        Assert.NotEmpty(section.Links);
        var defaults = SIL.Motif.Commands.ProjectStoreCommand.Run<SIL.Motif.Contract.Review.TrialReviewResult>(
            fixture.Project, SIL.Motif.Host.MotifProductVersion.CurrentText, (database, project) =>
                new SIL.Motif.Commands.Review.TrialReviewCommands(database).Read(new ReviewReadRequest(
                    fixture.ProjectKey, fixture.Proposal, ReportId: reportId, Detail: DetailLevel.Failures)));
        var defaultReport = Assert.IsType<AvailableTrialResult>(defaults.Value).Report;
        var workSection = Assert.Single(defaultReport.Sections, item => item.Id == "parser-work");
        var group = Assert.Single(workSection.Groups);
        Assert.Equal("work-increase", group.Cause);
        Assert.Equal("parser-work", group.Link.Arguments!.Section);
        var witness = Assert.IsType<WordRow>(Assert.Single(group.Samples));
        Assert.Equal("work", Assert.Single(witness.Links).Arguments!.Case);
    }

    [Fact]
    public void DecreasedParserWorkIsNeverLabelledAsAnIncrease()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var reportId = fixture.CreateWorkOnlyReport(50);
        var result = SIL.Motif.Commands.ProjectStoreCommand.Run<TrialReviewResult>(fixture.Project,
            SIL.Motif.Host.MotifProductVersion.CurrentText, (database, _) =>
                new SIL.Motif.Commands.Review.TrialReviewCommands(database).Read(new ReviewReadRequest(
                    fixture.ProjectKey, fixture.Proposal, ReportId: reportId, Section: "parser-work")));
        var report = Assert.IsType<AvailableTrialResult>(result.Value).Report;
        var group = Assert.Single(Assert.Single(report.Sections).Groups);
        Assert.Equal("work-decrease", group.Cause);
        Assert.Equal(-50, Assert.IsType<WordRow>(Assert.Single(group.Samples)).Work.PercentChange);
    }

    [Fact]
    public async Task SelectedNegativeSetDoesNotIncludeReferenceLossAtEitherDetailLevel()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var failures = await Read(fixture, "--detail", "failures", "--section", "controls", "--set", "negatives");
        var full = await Read(fixture, "--detail", "full", "--section", "controls", "--set", "negatives");
        var section = Assert.Single(failures.GetProperty("sections").EnumerateArray());
        Assert.Empty(section.GetProperty("groups").EnumerateArray());
        Assert.Empty(full.GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.True(JsonElement.DeepEquals(failures.GetProperty("summary"), full.GetProperty("summary")));
    }

    [Fact]
    public async Task WordTextShowsTheSameLostApprovedReadingsAsJson()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var json = await Read(fixture, "--word", "approved");
        var (exit, output, error) = await Run(fixture, false, "--word", "approved");
        Assert.True(exit == 0, error);
        var word = json.GetProperty("word");
        var readings = word.GetProperty("readings").GetProperty("items").EnumerateArray().ToArray();
        Assert.True(readings.Length >= 3);
        foreach (var reading in readings)
            Assert.Contains(reading.GetProperty("identityDigest").GetString()!, output);
        Assert.Contains("Before:", output);
        Assert.Contains("After:", output);
        foreach (var expectation in word.GetProperty("expectations").GetProperty("items").EnumerateArray())
            Assert.Contains(expectation.GetProperty("id").GetString()!, output);
    }

    [Fact]
    public async Task SummaryFitsTwentyFourLinesAndStartsWithObservedFlags()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var (exit, output, error) = await Run(fixture, false);
        Assert.True(exit == 0, error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.InRange(lines.Length, 1, 24);
        Assert.StartsWith("New word failures", lines[0]);
        Assert.Contains("Incomplete:", output);
    }

    [Fact]
    public async Task AmbiguousWordNamesExactCases()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var (exit, output, error) = await Run(fixture, true, "--word", "homograph");
        Assert.NotEqual(0, exit);
        Assert.Contains("homograph/a", output + error);
        Assert.Contains("homograph/b", output + error);
    }

    private static async Task<(int Exit, string Output, string Error)> Run(ReviewAcceptanceFixture fixture, bool json, params string[] options)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "proposal", "trial", "--project", fixture.Project, fixture.Proposal, "--report", fixture.Report }
                     .Concat(json ? ["--json"] : Array.Empty<string>()).Concat(options)) start.ArgumentList.Add(arg);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
        return (process.ExitCode, await output, await error);
    }

    [Fact]
    public async Task FailuresSampleBudgetNamesRemainder()
    {
        using var fixture = new ReviewAcceptanceFixture(extraFailures: 35);
        var report = await Read(fixture, "--detail", "failures");
        var groups = report.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("groups").EnumerateArray()).ToArray();
        Assert.True(groups.Sum(group => group.GetProperty("samples").GetArrayLength()) <= 30);
        var noParse = Assert.Single(groups, group => group.GetProperty("cause").GetString() == "no-parse");
        var samples = noParse.GetProperty("samples").GetArrayLength();
        Assert.InRange(samples, 0, 5);
        Assert.True(noParse.GetProperty("remaining").GetInt64() >= 30);
        Assert.Equal(noParse.GetProperty("count").GetInt64() - samples, noParse.GetProperty("remaining").GetInt64());
    }

    [Fact]
    public async Task ReportNeverMutatesOpinionsReadinessOrLiveProject()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var before = SHA256.HashData(File.ReadAllBytes(fixture.Project));
        var proposalBefore = fixture.ProposalState();
        var summary = await Read(fixture, "--detail", "summary");
        var full = await Read(fixture, "--detail", "full", "--case", "approved");
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(fixture.Project)));
        Assert.Equal(proposalBefore, fixture.ProposalState());
        Assert.Equal(summary.GetProperty("freshness").GetRawText(), full.GetProperty("freshness").GetRawText());
    }

    [Fact]
    public async Task EveryNamedSetAndLimitationIsDiscoverable()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var report = await Read(fixture, "--detail", "full", "--section", "evidence");
        var sets = report.GetProperty("page").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).ToArray();
        Assert.Contains("references", sets);
        Assert.Contains("negatives", sets);
        Assert.NotEmpty(report.GetProperty("limitations").EnumerateArray());
    }

    [Fact]
    public async Task PublicRecursiveOutputsHaveNoRawPerformanceFields()
    {
        using var fixture = new ReviewAcceptanceFixture();
        foreach (var detail in new[] { "summary", "failures", "full" })
        {
            var report = await Read(fixture, "--detail", detail);
            AssertNoRawPerformance(report);
        }
        AssertNoRawPerformance(await Read(fixture, "--detail", "full", "--case", "approved"));
    }

    private static void AssertNoRawPerformance(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(property.Name, new[] { "elapsedMs", "selfMs", "baselineWork", "trialParserWork", "diagnosticJson", "rawJson", "verdict", "parity" }, StringComparer.OrdinalIgnoreCase);
                AssertNoRawPerformance(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) AssertNoRawPerformance(child);
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(121, true)]
    public async Task CompleteScopeWorkThresholdIsStrict(long after, bool flagged)
    {
        using var fixture = new ReviewAcceptanceFixture();
        var reportId = fixture.CreateWorkOnlyReport(after);
        var report = await ReadFor(fixture, reportId, "--detail", "summary");
        var summary = report.GetProperty("summary");
        Assert.Equal(after - 100, summary.GetProperty("parserWork").GetProperty("percentChange").GetDouble());
        var flags = summary.GetProperty("flags").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray();
        Assert.Equal(flagged, flags.Contains("parser-work-increase"));
        Assert.Equal(flagged ? 1 : 0, flags.Length);
        Assert.Equal(0, summary.GetProperty("incomplete").GetProperty("affectedWords").GetInt64());
    }

    private static Task<JsonElement> Read(ReviewAcceptanceFixture fixture, params string[] options) => ReadFor(fixture, fixture.Report, options);

    private static async Task<JsonElement> ReadFor(ReviewAcceptanceFixture fixture, string reportId, params string[] options)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "proposal", "trial", "--project", fixture.Project, fixture.Proposal, "--report", reportId, "--json" }.Concat(options))
            start.ArgumentList.Add(arg);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(process.ExitCode == 0, await error);
        return JsonDocument.Parse(await output).RootElement.GetProperty("report").Clone();
    }
}
