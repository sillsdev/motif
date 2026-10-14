using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Integration;

[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PostApplyBaselineRefreshTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterApplyTheNextDryRunRefreshesItsBaselineAsManualCaptureDoes(bool captureInitialBaseline)
    {
        var path = pristine.CopyProjectFile();
        var root = Path.Combine(Path.GetDirectoryName(path)!, "post-apply-worker");
        var options = IsolatedRunner.Options(root);
        output.WriteLine($"CLI: {BuildOutput.Cli}; Worker: {options.WorkerExecutable}");

        async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments)
        {
            var start = CliProcess.Start(options, arguments);
            start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
            CliProcess.EnableAdvancedAiMode(start, root);
            var result = await CliProcess.RunAsync(start);
            output.WriteLine($"{arguments[0]} exit={result.ExitCode}; stdout={result.Output}; stderr={result.Error}");
            return result;
        }

        async Task<string> Author(string draft, string phoneme)
        {
            var created = await Run("new", "--project", path, "--draft", draft, "--json");
            Assert.True(created.ExitCode == 0, created.Error);
            var composed = await Run("compose-author-phoneme", "--project", path, "--draft", draft,
                "--intent", JsonSerializer.Serialize(new { name = phoneme, representations = new[] { phoneme } }),
                "--json");
            Assert.True(composed.ExitCode == 0, composed.Error);
            Assert.Equal(0, (await Run("label", "--project", path, "--draft", draft, "Add a phoneme")).ExitCode);
            Assert.Equal(0, (await Run("comment", "--project", path, "--draft", draft,
                "The authored phoneme is needed by this disposable grammar.")).ExitCode);
            var finalized = await Run("finalize", "--project", path, "--draft", draft, "--json");
            Assert.True(finalized.ExitCode == 0, finalized.Error);
            const string marker = "-> Proposal ";
            var position = finalized.Output.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(position >= 0, finalized.Output);
            return finalized.Output[(position + marker.Length)..].Split(' ', '\r', '\n')[0];
        }

        async Task<(int ExitCode, string Output, string Error)> DryRun(string id) =>
            await Run("dry-run", id, "--project", path, "--wait", "--wait-timeout-ms", "30000", "--json");

        if (captureInitialBaseline)
        {
            var captured = await Run("baseline", "capture", "--project", path, "--json");
            Assert.True(captured.ExitCode == 0, captured.Error);
        }
        var first = await Author("first", "p");
        var measured = await DryRun(first);
        Assert.True(measured.ExitCode == 0, measured.Error);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var applied = await Run("apply", first, "--project", path, "--force", "--json");
        Assert.True(applied.ExitCode == 0, applied.Error);
        using (var response = JsonDocument.Parse(applied.Output))
            Assert.True(response.RootElement.GetProperty("applied").GetBoolean());
        Assert.NotEqual(Convert.ToHexString(before), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

        var second = await Author("second", "k");
        var automatic = await DryRun(second);
        var warnings = await Run("warnings", "--project", path, "--json");
        Assert.Equal(2, warnings.ExitCode);
        Assert.Empty(warnings.Output);
        using (var refusal = JsonDocument.Parse(warnings.Error))
        {
            Assert.Equal("grammarcheck.not-run", refusal.RootElement.GetProperty("code").GetString());
            Assert.Contains("motif grammar check --project <fwdata>",
                refusal.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.ChangeExtension(path, ".motif.db"),
                   Mode = SqliteOpenMode.ReadOnly,
                   Pooling = false,
               }.ToString()))
        {
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT Kind, Status, FailureCategory, ResultJson FROM Jobs ORDER BY CreatedUtc;";
            using var rows = query.ExecuteReader();
            while (rows.Read())
                output.WriteLine(string.Join(" | ", Enumerable.Range(0, rows.FieldCount)
                    .Select(index => rows.IsDBNull(index) ? "null" : rows.GetValue(index).ToString())));
        }

        var manual = await Run("baseline", "capture", "--project", path, "--json");
        Assert.True(manual.ExitCode == 0, manual.Error);
        var control = await DryRun(second);
        Assert.True(control.ExitCode == 0, control.Error);
        Assert.True(automatic.ExitCode == 0,
            $"Post-Apply automatic refresh failed, while manual capture and the same Dry Run succeeded: {automatic.Error}");
        using var initialEvidence = JsonDocument.Parse(measured.Output);
        using var automaticEvidence = JsonDocument.Parse(automatic.Output);
        using var manualEvidence = JsonDocument.Parse(control.Output);
        Assert.True(automaticEvidence.RootElement.GetProperty("evidenceCurrent").GetBoolean());
        Assert.NotEqual(initialEvidence.RootElement.GetProperty("baseline").GetProperty("semanticSnapshotDigest").GetString(),
            automaticEvidence.RootElement.GetProperty("baseline").GetProperty("semanticSnapshotDigest").GetString());
        Assert.Equal(manualEvidence.RootElement.GetProperty("baseline").GetProperty("semanticSnapshotDigest").GetString(),
            automaticEvidence.RootElement.GetProperty("baseline").GetProperty("semanticSnapshotDigest").GetString());
        Assert.Equal(manualEvidence.RootElement.GetProperty("effectDigest").GetString(),
            automaticEvidence.RootElement.GetProperty("effectDigest").GetString());
        Assert.Equal(manualEvidence.RootElement.GetProperty("intentDigest").GetString(),
            automaticEvidence.RootElement.GetProperty("intentDigest").GetString());
    }
}
