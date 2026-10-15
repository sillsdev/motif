using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class PhonologyVerbArgvTests(PristineProjectFixture pristine)
{
    [Fact]
    public void SaveListAndShowDispatchToTheTypedExampleCommands()
    {
        var project = pristine.CopyProjectFile();
        var payload = Path.Combine(Path.GetDirectoryName(project)!, "examples.json");
        File.WriteAllText(payload, """
            {"name":"Vowel harmony","examples":[{"id":"front","underlying":"a-ku","expected":["e-ki"],"context":{"kind":"hcXmlEntry","entryId":"entry-17"},"citation":{"source":"Field notes","locator":"example 12"}}]}
            """);

        var save = Run("phonology-examples", "save", "--project", project, "--file", payload, "--json");
        Assert.True(save.ExitCode == 0, $"Save failed. stdout: {save.Output}\nstderr: {save.Error}");
        using var savedJson = JsonDocument.Parse(save.Output);
        var revisionId = savedJson.RootElement.GetProperty("exampleSet").GetProperty("revision")
            .GetProperty("revisionId").GetString()!;

        var list = Run("phonology-examples", "list", "--project", project, "--json");
        Assert.Equal(0, list.ExitCode);
        using var listJson = JsonDocument.Parse(list.Output);
        Assert.Equal(revisionId, listJson.RootElement.GetProperty("items")[0]
            .GetProperty("revision").GetProperty("revisionId").GetString());

        var show = Run("phonology-examples", "show", "--project", project, revisionId, "--json");
        Assert.Equal(0, show.ExitCode);
        using var shownJson = JsonDocument.Parse(show.Output);
        var example = shownJson.RootElement.GetProperty("items")[0];
        Assert.Equal("front", example.GetProperty("id").GetString());
        Assert.Equal("Field notes", example.GetProperty("citation").GetProperty("source").GetString());
    }

    [Fact]
    public void CursorJsonRejectsRepeatedAndUnknownFieldsBeforeOpeningAProject()
    {
        var duplicate = Run("phonology-examples", "list", "--project", "/missing/project.fwdata",
            "--continuation", "{\"projectIdentity\":\"one\",\"projectIdentity\":\"two\",\"afterSetId\":\"set\"}", "--json");
        var unknown = Run("phonology-examples", "show", "--project", "/missing/project.fwdata",
            "example-revision/id", "--continuation", "{\"ownerId\":\"id\",\"afterOrdinal\":0,\"other\":true}", "--json");

        Assert.NotEqual(0, duplicate.ExitCode);
        Assert.NotEqual(0, unknown.ExitCode);
        using var duplicateEnvelope = JsonDocument.Parse(duplicate.Error);
        using var unknownEnvelope = JsonDocument.Parse(unknown.Error);
        var duplicateMessage = duplicateEnvelope.RootElement.GetProperty("message").GetString()!;
        var unknownMessage = unknownEnvelope.RootElement.GetProperty("message").GetString()!;
        Assert.Contains("repeats 'projectIdentity'", duplicateMessage, StringComparison.Ordinal);
        Assert.Contains("'other'", unknownMessage, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var root = Path.Combine(Path.GetTempPath(), "motif-phonology-cli-" + Guid.NewGuid().ToString("N"));
        start.Environment[RunnerOptions.RootVariable] = root;
        var preferencePath = Path.Combine(root, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath;
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
