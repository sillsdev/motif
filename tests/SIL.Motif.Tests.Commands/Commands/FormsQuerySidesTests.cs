using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using Xunit;
using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Tests.Commands;

/// <summary>Pins <c>query forms --as-of</c> and <c>--sides both</c> over a Baseline and a Proposal's Trial copy.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class FormsQuerySidesTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.FormsQuerySidesTests", Guid.NewGuid().ToString("N"));

    private static QueryRecord Forms(string? asOf, string? sides = null, string? sort = null, params QueryCondition[] where) =>
        new(QuerySources.Forms, where, [], AsOf: asOf, Sides: sides, Sort: sort);

    private FormsQueryResponse Run(TrialsTests.ProjectFixture fixture, QueryRecord query)
    {
        var outcome = FormsQueryCommand.Run(new FormsQueryRequest(fixture.Project.FullFwDataPath, "test", query));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private void InstallBaseline(TrialsTests.ProjectFixture fixture)
    {
        var record = new BaselineRepository(fixture.Database).GetCurrent(ProjectWorkspaceKey.Compute(fixture.Project))!;
        var source = GroupCorpusFixture.Create(pristine);
        Directory.CreateDirectory(Path.GetDirectoryName(record.FwDataPath)!);
        CopyDirectory(Path.GetDirectoryName(source)!, Path.GetDirectoryName(record.FwDataPath)!);
        var copied = Path.Combine(Path.GetDirectoryName(record.FwDataPath)!, Path.GetFileName(source));
        if (copied != record.FwDataPath) File.Move(copied, record.FwDataPath, true);
    }

    private string InstallTrial(TrialsTests.ProjectFixture fixture, string proposalJson, string proposalId, out string scratch)
    {
        var jobId = "trial/sides-" + Guid.NewGuid().ToString("N");
        fixture.CreateJob(proposalJson, "2026-09-24T11:00:00Z", jobId, ["trial-root"]);
        scratch = TrialScratchPath.ForJob(jobId);
        var baseline = new BaselineRepository(fixture.Database).GetCurrent(ProjectWorkspaceKey.Compute(fixture.Project))!;
        CopyDirectory(Path.GetDirectoryName(baseline.FwDataPath)!, scratch);
        GroupCorpusFixture.AddBahaStem(Path.Combine(scratch, Path.GetFileName(baseline.FwDataPath)));
        return jobId;
    }

    private static QueryCondition Only(string forms) => new("form", "in", forms);

    private static QueryPairedRow Row(FormsQueryResponse response, string form) =>
        response.Sides!.Rows.Single(row => (row.Baseline ?? row.Proposal)!.Form.Form == form);

    [Fact]
    public void TheBaselineReadsWithoutAnyTrialAndCarriesParseStateAndMorphs()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        InstallBaseline(fixture);
        var response = Run(fixture, Forms("baseline"));
        var state = Assert.Single(response.Sides!.States);
        Assert.Equal((QuerySideNames.Baseline, QuerySideNames.Ready), (state.Side, state.Status));
        Assert.Equal(QuerySideNames.Baseline, response.Sides.Mode);
        var alternate = Row(response, "kaonan").Baseline!;
        Assert.Equal(("alternate", "kaon", "approved", 3), (alternate.Form.Kind, alternate.Form.Base, alternate.Parse.State, alternate.Parse.Tokens));
        Assert.Equal(3, alternate.Form.Tokens);
        var morph = Assert.Single(Assert.Single(alternate.Parse.Analyses).Morphs);
        Assert.Equal(("kaon", "eat"), (morph.Form, morph.Gloss));
        Assert.NotEmpty(morph.Entry);
        Assert.True(alternate.Parse.Analyses[0].Approved);
        Assert.Null(Row(response, "kaonan").Proposal);
        var lexeme = Row(response, "kaon").Baseline!;
        Assert.Equal(("lexeme", QuerySideNames.Absent, 0), (lexeme.Form.Kind, lexeme.Parse.State, lexeme.Parse.Tokens));
        Assert.Empty(lexeme.Parse.Analyses);
        Assert.Equal(response.Sides.Rows.Count, response.Result.Rows.Count);
    }

    [Fact]
    public void WithoutAsOfTheQueryStillNeedsATrial()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var outcome = FormsQueryCommand.Run(new FormsQueryRequest(fixture.Project.FullFwDataPath, "test", Forms(null)));
        Assert.Equal(RefusalCodes.QueryNoTrialRows, outcome.Refusal!.Code);
    }

    [Fact]
    public void AProposalWithNoTrialIsPendingNotRefused()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        var response = Run(fixture, Forms(proposal.Id.Value));
        var state = Assert.Single(response.Sides!.States);
        Assert.Equal((QuerySideNames.Proposal, QuerySideNames.Pending, proposal.Id.Value), (state.Side, state.Status, state.Source));
        Assert.Null(state.TrialId);
        Assert.NotEmpty(state.Reason!);
        Assert.Empty(response.Sides.Rows);
    }

    [Fact]
    public void AProposalWithATrialReadsItsCopyUnderThePostProposalGrammar()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        InstallBaseline(fixture);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        var job = InstallTrial(fixture, proposal.Json, proposal.Id.Value, out var scratch);
        try
        {
            var response = Run(fixture, Forms(proposal.Id.Value));
            var state = Assert.Single(response.Sides!.States);
            Assert.Equal((QuerySideNames.Ready, job), (state.Status, state.TrialId));
            var baha = Row(response, "baha").Proposal!;
            Assert.Equal(("lexeme", "parsed"), (baha.Form.Kind, baha.Parse.State));
            Assert.Single(baha.Parse.Analyses);
            Assert.Null(Row(response, "baha").Baseline);
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    [Fact]
    public void BothSidesComeSideBySideAndAPendingProposalIsMarkedPending()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        InstallBaseline(fixture);
        var proposal = fixture.SaveProposal();
        var pending = Run(fixture, Forms(proposal.Id.Value, QuerySideNames.Both));
        Assert.Equal(QuerySideNames.Both, pending.Sides!.Mode);
        Assert.Equal([QuerySideNames.Ready, QuerySideNames.Pending], pending.Sides.States.Select(state => state.Status));
        Assert.All(pending.Sides.Rows, row => { Assert.NotNull(row.Baseline); Assert.Null(row.Proposal); });

        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        InstallTrial(fixture, proposal.Json, proposal.Id.Value, out var scratch);
        try
        {
            var both = Run(fixture, Forms(proposal.Id.Value, QuerySideNames.Both, "proposal.form"));
            Assert.Equal([QuerySideNames.Ready, QuerySideNames.Ready], both.Sides!.States.Select(state => state.Status));
            var kaonan = Row(both, "kaonan");
            Assert.Equal(kaonan.Baseline!.Form.Id, kaonan.Proposal!.Form.Id);
            Assert.Equal("approved", kaonan.Baseline.Parse.State);
            var baha = Row(both, "baha");
            Assert.Null(baha.Baseline);
            Assert.Equal("parsed", baha.Proposal!.Parse.State);
            var proposalForms = both.Sides.Rows.Select(row => row.Proposal?.Form.Form).Where(form => form is not null).ToList();
            Assert.Equal(proposalForms.Order(StringComparer.Ordinal), proposalForms);
            AssertShape(both);
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    [Fact]
    public void SortingAndFilteringWorkOnANamedSide()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        InstallBaseline(fixture);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        InstallTrial(fixture, proposal.Json, proposal.Id.Value, out var scratch);
        try
        {
            var three = Only("kaon,kaonan,baha");
            var descending = Run(fixture, Forms(proposal.Id.Value, QuerySideNames.Both, "proposal.form:desc", three));
            Assert.Equal(["kaonan", "kaon", "baha"], descending.Sides!.Rows.Select(row => (row.Proposal ?? row.Baseline)!.Form.Form));
            var parsedOnProposal = Run(fixture, Forms(proposal.Id.Value, QuerySideNames.Both, "proposal.form",
                three, new QueryCondition("proposal.parse-state", "is", "parsed")));
            Assert.Equal("baha", Assert.Single(parsedOnProposal.Sides!.Rows).Proposal!.Form.Form);
            var approvedOnBaseline = Run(fixture, Forms(proposal.Id.Value, QuerySideNames.Both, "baseline.form",
                three, new QueryCondition("baseline.parse-state", "is", "approved")));
            Assert.Equal("kaonan", Assert.Single(approvedOnBaseline.Sides!.Rows).Baseline!.Form.Form);
            var alternates = Run(fixture, Forms(proposal.Id.Value, QuerySideNames.Both, "baseline.form",
                three, new QueryCondition("kind", "is", "alternate")));
            Assert.Equal("kaonan", Assert.Single(alternates.Sides!.Rows).Baseline!.Form.Form);
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    [Fact]
    public void AGroupedSingleSideRunKeepsTheGroupsAndNoPairedRows()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        InstallBaseline(fixture);
        var response = Run(fixture, new QueryRecord(QuerySources.Forms, [], ["kind"], AsOf: "baseline"));
        Assert.Empty(response.Sides!.Rows);
        Assert.Equal(["alternate", "lexeme"], response.Result.Groups.Select(group => group.Key[0]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RefusalsUseTheirRegisteredCodes()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal().Id.Value;
        string Code(QueryRecord query) =>
            FormsQueryCommand.Run(new FormsQueryRequest(fixture.Project.FullFwDataPath, "test", query)).Refusal!.Code;
        Assert.Equal(RefusalCodes.QueryAsOfUnavailable, Code(Forms("baseline")));
        Assert.Equal(RefusalCodes.QueryAsOfUnavailable, Code(Forms("sha256:abc")));
        Assert.Equal(RefusalCodes.QueryInvalid, Code(Forms("baseline", QuerySideNames.Both)));
        Assert.Equal(RefusalCodes.QueryInvalid, Code(Forms(null, QuerySideNames.Both)));
        Assert.Equal(RefusalCodes.QueryInvalid, Code(Forms(proposal, "left")));
        Assert.Equal(RefusalCodes.QueryInvalid, Code(Forms(proposal, QuerySideNames.Both, "form")));
        Assert.Equal(RefusalCodes.QueryInvalid, Code(Forms(proposal, null, "baseline.form")));
        Assert.Equal(RefusalCodes.QueryInvalid, Code(new QueryRecord(QuerySources.Stems, [], [], AsOf: proposal)));
        Assert.Equal(RefusalCodes.QueryInvalid, Code(Forms(proposal, null, null, new QueryCondition("baseline.kind", "is", "lexeme"))));
    }

    [Fact]
    public void AVariantOfTwoEntriesGivesTwoRowsWithDistinctIds()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        InstallBaseline(fixture);
        var baseline = new BaselineRepository(fixture.Database).GetCurrent(ProjectWorkspaceKey.Compute(fixture.Project))!;
        GroupCorpusFixture.AddBahaStem(baseline.FwDataPath);
        GroupCorpusFixture.AddTwoComponentVariant(baseline.FwDataPath);
        var result = Run(fixture, Forms(QuerySideNames.Baseline, null, null, new QueryCondition("form", "in", "kaonvar")));
        var rows = result.Sides!.Rows;
        Assert.Equal(["baha", "kaon"], rows.Select(row => row.Baseline!.Form.Base).Order(StringComparer.Ordinal));
        Assert.Equal(2, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, row => Assert.Equal(row.Id, row.Baseline!.Form.Id));
        Assert.Equal(2, result.Result.Rows.Count);
    }

    [Fact]
    public void TheStatesStayInTheirClosedSetsAndTheShapeIsVersioned()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        InstallBaseline(fixture);
        var proposal = fixture.SaveProposal();
        var both = Run(fixture, Forms(proposal.Id.Value, QuerySideNames.Both));
        Assert.Equal(QuerySidesResult.CurrentVersion, both.Sides!.Version);
        Assert.All(both.Sides.States, state =>
        {
            Assert.Contains(state.Status, QuerySideNames.Statuses);
            Assert.True(state.Reason is null || QuerySideNames.Reasons.Contains(state.Reason));
        });
        Assert.Equal(QuerySideNames.ReasonNoTrial, both.Sides.States[1].Reason);
        Assert.All(both.Sides.Rows, row => Assert.Contains(row.Baseline!.Parse.State, QuerySideNames.ParseStates));
    }

    private static void AssertShape(FormsQueryResponse response)
    {
        using var document = JsonDocument.Parse(ProjectionJson.Serialize(response));
        var sides = document.RootElement.GetProperty("sides");
        var lines = new List<string>();
        Outline(sides.GetProperty("version"), "/sides/version", lines);
        Outline(sides.GetProperty("mode"), "/sides/mode", lines);
        foreach (var state in sides.GetProperty("states").EnumerateArray()) Outline(state, "/sides/states[" + state.GetProperty("side").GetString() + "]", lines);
        var rows = sides.GetProperty("rows").EnumerateArray().ToList();
        foreach (var form in new[] { "kaonan", "baha" })
            Outline(rows.Single(row => row.EnumerateObject().Where(p => p.Name != "id").Any(p => p.Value.GetProperty("form").GetProperty("form").GetString() == form)),
                "/sides/rows[" + form + "]", lines);
        var path = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")), "tests",
            "SIL.Motif.Tests.Commands", "Golden", "query-sides.shape.txt");
        var actual = string.Join('\n', lines);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, actual);
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
        Assert.Equal(new[] { "bundleId", "grammarHash", "isStale", "notMeasured", "result", "sides", "trialId", "unresolvedVariants" },
            document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    private static void Outline(JsonElement element, string path, List<string> lines)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) Outline(property.Value, path + "/" + property.Name, lines);
        else if (element.ValueKind == JsonValueKind.Array)
        {
            lines.Add(path + " array");
            foreach (var item in element.EnumerateArray().Take(1)) Outline(item, path + "[]", lines);
        }
        else lines.Add(path + " " + element.ValueKind);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
