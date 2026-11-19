using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Tests.Commands;
using SIL.Motif.Tests.TestFixtures;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Query;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Queries;

public sealed class FormsQueryCommandTests
{
    [Fact]
    public void ExcessiveGroupingReturnsARegisteredRefusalNamingTheColumns()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(FormsQueryCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "marker");
            var row = new QueryForm("id", "a", "a", "lexeme", "e", "", "",
                Enumerable.Range(0, 1001).Select(i => i.ToString()).ToArray(), []);
            ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.Replace(connection, transaction, new("trial", "grammar", "bundle", "ws",
                    new QueryFactsReader.Snapshot([row], [], 0)));
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            var outcome = FormsQueryCommand.Run(new(path, "1.0", new("forms", [], ["environments"])));
            Assert.Equal("query.too-many-groups", outcome.Refusal!.Code);
            Assert.Contains("environments", outcome.Refusal.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void AQuerySnapshotIsSupersededOnlyByANewerTrialOfItsOwnProposal()
    {
        using var fixture = new TrialsTests.ProjectFixture(Path.GetTempPath());
        var proposal = fixture.SaveProposal();
        var other = fixture.SaveProposal();
        var before = fixture.RecordPinnedAssessmentPair(proposal, "after", AssessmentKinds.Correctness);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "source", ["after"], baselineAssessmentIds: [before]);
        fixture.CreateJob(other.Json, "2026-09-24T12:00:00Z", "other-proposal", []);
        var input = JsonSerializer.Serialize(new TrialJobInput(proposal.Json, null), MotifJson.CreateOptions());
        var jobs = new JobRepository(fixture.Database);
        jobs.Create("other-project", "unrelated-project", "trial", input, "2026-09-24T13:00:00Z");
        using (var connection = fixture.Database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            QuerySnapshotStore.Replace(connection, transaction, new("source", "grammar", "bundle", "ws",
                new QueryFactsReader.Snapshot([], [], 0), File.GetLastWriteTimeUtc(fixture.Project.FullFwDataPath).Ticks));
            transaction.Commit();
        }
        var request = new FormsQueryRequest(fixture.Project.FullFwDataPath, "1.0", new QueryRecord("forms", [], []));
        Assert.False(FormsQueryCommand.Run(request, parserPath: FakeParser.ExecutablePath).Value!.IsStale);

        jobs.RecordInterrupted("newer-failed", ProjectWorkspaceKey.Compute(fixture.Project),
            "trial", input, "{}", "2026-09-24T14:00:00Z", JobStatus.Failed);
        Assert.True(FormsQueryCommand.Run(request, parserPath: FakeParser.ExecutablePath).Value!.IsStale);
    }

    [Fact]
    public void RefusesAbsentRowsReplacesCachedTrialAndReportsStaleness()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(FormsQueryCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "marker");
            var request = new FormsQueryRequest(path, "1.0", new QueryRecord("forms", [], []));
            var cache = new FormsQuerySnapshotCache();
            var absent = FormsQueryCommand.Run(request, cache);
            Assert.False(absent.Succeeded);
            Assert.Equal("query.no-trial-rows", absent.Refusal!.Code);
            Assert.Contains("wait for evidence", absent.Refusal.Message);
            void Publish(string trial, long ticks)
            {
                var stored = new QuerySnapshotStore.Stored(trial, "grammar", "bundle", "ws",
                    new QueryFactsReader.Snapshot([new QueryForm(trial, "a", "a", "lexeme", "e", "", "", [], [])], [], 0), ticks);
                var outcome = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
                {
                    using var connection = database.OpenConnection();
                    using var transaction = connection.BeginTransaction();
                    QuerySnapshotStore.Replace(connection, transaction, stored);
                    transaction.Commit();
                    return CommandOutcome<string>.Success("ok");
                });
                Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            }
            Publish("first", File.GetLastWriteTimeUtc(path).Ticks);
            var first = FormsQueryCommand.Run(request, cache);
            Assert.True(first.Succeeded, first.Refusal?.Message);
            Assert.True(first.Value!.IsStale);
            Assert.Equal("first", Assert.Single(first.Value.Result.Rows).Id);
            var unavailable = FormsQueryCommand.Run(request with
                { Query = request.Query with { Where = [new("tokens", ">", "0")] } }, cache);
            Assert.Equal("query.invalid", unavailable.Refusal!.Code);
            Assert.Contains("not available", unavailable.Refusal.Message);
            var newerTrial = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Jobs(JobId,ProjectKey,Kind,Status,LineageId,InputJson,CreatedUtc,UpdatedUtc) " +
                    "VALUES ('newer','project','trial','completed','newer','{}','2026','2026')";
                command.ExecuteNonQuery();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(newerTrial.Succeeded);
            var staleTrial = FormsQueryCommand.Run(request, cache);
            Assert.True(staleTrial.Succeeded);
            Assert.True(staleTrial.Value!.IsStale);
            Assert.Equal("first", staleTrial.Value.TrialId);
            var binding = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.BindBundle(connection, transaction, "first", "grammar", "retained");
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(binding.Succeeded, binding.Refusal?.Message);
            Assert.Equal("retained", FormsQueryCommand.Run(request, cache).Value!.BundleId);
            var older = FormsQueryCommand.Run(request with { Query = request.Query with { TrialId = "older" } }, cache);
            Assert.False(older.Succeeded);
            Assert.Equal("query.no-trial-rows", older.Refusal!.Code);
            Publish("second", File.GetLastWriteTimeUtc(path).AddSeconds(-1).Ticks);
            var second = FormsQueryCommand.Run(request, cache);
            Assert.True(second.Succeeded, second.Refusal?.Message);
            Assert.True(second.Value!.IsStale);
            Assert.Equal("second", Assert.Single(second.Value.Result.Rows).Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
