using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>wordform dispose</c>, <c>wordform undispose</c> and <c>disposition show|list</c> through the CLI.</summary>
public sealed class WordformDisposeArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-dispose-argv-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(), "motif-dispose-worker-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public WordformDisposeArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_workerRoot);
        _project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(_project, "<languageproject>\n<rt class=\"LangProject\" guid=\"" +
            Guid.Parse("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f") + "\">\n</rt>\n</languageproject>\n");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void DisposeNoOpContradictionReplaceShowListAndUndisposeRunThroughTheReleasedExecutable()
    {
        var first = Run("wordform", "dispose", "Nairobi", "Mombasa", "--project", _project, "--as", "name",
            "--evidence", "towns in the gazetteer", "--by", "ana", "--json");
        Assert.True(first.ExitCode == 0, first.Error);
        Assert.Equal(2, Json(first).GetProperty("recorded").GetInt32());

        var again = Json(Run("wordform", "dispose", "Nairobi", "--project", _project, "--as", "name", "--evidence", "x", "--json"));
        Assert.Equal(1, again.GetProperty("alreadyRecorded").GetInt32());

        var contradiction = Run("wordform", "dispose", "Nairobi", "--project", _project, "--as", "loan", "--evidence", "x", "--json");
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), contradiction.ExitCode);
        Assert.Contains("disposition.contradiction", contradiction.Error + contradiction.Output, StringComparison.Ordinal);

        var replaced = Json(Run("wordform", "dispose", "--replace", "Nairobi", "--project", _project, "--as", "loan",
            "--evidence", "borrowed", "--by", "ben", "--json"));
        Assert.Equal(1, replaced.GetProperty("replaced").GetInt32());

        var shown = Json(Run("disposition", "show", "Nairobi", "--project", _project, "--json"));
        Assert.Equal("loan", shown.GetProperty("current").GetProperty("category").GetString());
        Assert.Equal("word-wide", shown.GetProperty("scope").GetString());
        Assert.Equal("replaced", shown.GetProperty("history")[0].GetProperty("endedKind").GetString());

        var listed = Json(Run("disposition", "list", "--project", _project, "--as", "name", "--format", "json"));
        Assert.Equal(1, listed.GetProperty("total").GetInt32());
        Assert.Equal("Mombasa", listed.GetProperty("items")[0].GetProperty("form").GetString());

        var cleared = Json(Run("wordform", "undispose", "Mombasa", "Absent", "--project", _project, "--reason", "wrong", "--json"));
        Assert.Equal((1, 1), (cleared.GetProperty("cleared").GetInt32(), cleared.GetProperty("notRecorded").GetInt32()));
        var gone = Json(Run("disposition", "show", "Mombasa", "--project", _project, "--json"));
        Assert.True(!gone.TryGetProperty("current", out var standing) || standing.ValueKind == JsonValueKind.Null);

        var text = Run("disposition", "list", "--project", _project);
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("1 word types", text.Output, StringComparison.Ordinal);
        Assert.Contains("Nairobi  loan: borrowed", text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AMisspellingNeedsItsIntendedFormAndAMissingEvidenceFlagPrintsTheUsage()
    {
        var noIntended = Run("wordform", "dispose", "recieve", "--project", _project, "--as", "misspelling", "--evidence", "typo");
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), noIntended.ExitCode);
        Assert.Contains("--intended", noIntended.Error + noIntended.Output, StringComparison.Ordinal);

        var ok = Run("wordform", "dispose", "recieve", "--project", _project, "--as", "misspelling", "--intended", "receive", "--evidence", "typo");
        Assert.True(ok.ExitCode == 0, ok.Error);

        var usage = Run("wordform", "dispose", "x", "--project", _project, "--as", "name");
        Assert.Equal(1, usage.ExitCode);
        Assert.Contains("wordform dispose <form>... --project <fwdata>", usage.Error, StringComparison.Ordinal);
        Assert.Equal(1, Run("disposition", "list", "--project", _project, "--format", "yaml").ExitCode);
        Assert.Equal(1, Run("disposition", "show", "--project", _project).ExitCode);
        Assert.Contains(CommandCatalog.All, command => command.Name == "wordform dispose");
    }

    [Fact]
    public void FromFileDryRunRefusalsSkipRefusedAndJsonLinesRunThroughTheReleasedExecutable()
    {
        var file = Path.Combine(_root, "d.tsv");
        File.WriteAllLines(file, new[] { "form\tcategory\tevidence", "Nairobi\tname\ta town" }.Append("Bad\tplace\tx")
            .Append("Kisumu\tloan\tborrowed"));

        var dry = Run("wordform", "dispose", "--from-file", file, "--project", _project, "--dry-run", "--skip-refused", "--json");
        var plan = Json(dry);
        Assert.Equal((2, 1, false, true), (plan.GetProperty("added").GetInt32(), plan.GetProperty("refused").GetInt32(),
            plan.GetProperty("written").GetBoolean(), plan.GetProperty("dryRun").GetBoolean()));

        var refused = Run("wordform", "dispose", "--from-file", file, "--project", _project);
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), refused.ExitCode);
        Assert.Contains("row 3  Bad  disposition.invalid", refused.Error, StringComparison.Ordinal);
        Assert.Contains("Nothing was written", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, Json(Run("disposition", "list", "--project", _project, "--json")).GetProperty("total").GetInt32());

        var lines = Run("wordform", "dispose", "--from-file", file, "--project", _project, "--skip-refused", "--format", "json");
        Assert.True(lines.ExitCode == 0, lines.Error + lines.Output);
        var rows = lines.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        Assert.Equal(4, rows.Length);
        Assert.Equal("refused", rows[1].GetProperty("result").GetString());
        Assert.Equal("disposition.invalid", rows[1].GetProperty("code").GetString());
        Assert.True(rows[3].GetProperty("summary").GetProperty("written").GetBoolean());
        Assert.Equal(2, Json(Run("disposition", "list", "--project", _project, "--json")).GetProperty("total").GetInt32());

        Assert.Equal(1, Run("wordform", "dispose", "--from-file", file, "--project", _project, "--format", "yaml").ExitCode);
        Assert.Equal(1, Run("wordform", "dispose", "--from-file", file, "--project", _project, "extra").ExitCode);
    }

    private static JsonElement Json(CliRun run)
    {
        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        return JsonDocument.Parse(run.Output).RootElement;
    }

    private CliRun Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = null;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit((int)Wait.Scaled(TimeSpan.FromMinutes(30)).TotalMilliseconds),
            "The CLI process did not exit within 30 minutes (scaled by MOTIF_TEST_DEADLINE_SCALE).");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
