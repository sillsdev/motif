using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class SlowParserWalkthroughProject : IDisposable
{
    private const int WordCount = 512;
    private const string BugId = "switch-cancellation-slow-parser";
    private readonly string _root;

    private SlowParserWalkthroughProject(string root, string projectPath)
    {
        _root = root;
        FwDataPath = projectPath;
        ManagedRoot = Path.Combine(root, "managed");
        Directory.CreateDirectory(ManagedRoot);
    }

    public string FwDataPath { get; }

    public string ManagedRoot { get; }

    public IReadOnlyList<string> Words { get; } = Enumerable.Range(0, WordCount)
        .Select(index => StemForm(index) + "ler")
        .ToArray();

    public static async Task<SlowParserWalkthroughProject> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.SwitchProject", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var repository = RepositoryRoot();
            var samplePath = Path.Combine(repository, "samples", "synthetic-turkic", "sample.json");
            var sample = JsonNode.Parse(await File.ReadAllTextAsync(samplePath))!.AsObject();
            var stems = sample["stems"]!.AsArray();
            for (var index = 0; index < WordCount; index++)
            {
                var stem = StemForm(index);
                stems.Add(new JsonObject
                {
                    ["id"] = $"switch-cancel-{index:D4}",
                    ["form"] = stem,
                    ["partOfSpeech"] = "noun",
                    ["gloss"] = $"switch cancellation {index:D4}",
                });
            }

            sample["texts"] = new JsonArray(new JsonObject
            {
                ["id"] = "switch-cancellation",
                ["title"] = SeededProject.TextTitle,
                ["sentences"] = new JsonArray(JsonValue.Create(StemForm(0) + "ler")),
            });

            var specPath = Path.Combine(root, "sample.json");
            var bugsPath = Path.Combine(root, "bugs.json");
            await File.WriteAllTextAsync(specPath, sample.ToJsonString());
            await File.WriteAllTextAsync(bugsPath, $$"""
                [{
                  "id": "{{BugId}}",
                  "title": "Repeated optional plural slots",
                  "disclaimer": "Synthetic parser workload for a cancellation walkthrough.",
                  "symptom": {
                    "kind": "slow",
                    "words": ["{{StemForm(0)}}ler"],
                    "reason": "Repeated optional slots create redundant parse paths."
                  },
                  "fix": ["Keep one Plural slot in the noun template."],
                  "patch": [{
                    "op": "duplicateOptionalSlot",
                    "templateId": "noun",
                    "slotId": "plural",
                    "count": 64
                  }]
                }]
                """);

            var outputPath = Path.Combine(root, "project");
            var builderPath = Path.Combine(BuildOutput.ProductDirectory,
                OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
            var start = new ProcessStartInfo(builderPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("build");
            start.ArgumentList.Add(specPath);
            start.ArgumentList.Add(outputPath);
            start.ArgumentList.Add("--bugs");
            start.ArgumentList.Add(bugsPath);
            start.ArgumentList.Add("--bug");
            start.ArgumentList.Add(BugId);

            using var process = Process.Start(start) ?? throw new InvalidOperationException(
                "The synthetic slow parser project builder did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var standardOutput = await output;
            var standardError = await error;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"The synthetic slow parser project did not build: {standardError}");

            using var result = JsonDocument.Parse(standardOutput);
            var projectPath = result.RootElement.GetProperty("projectPath").GetString()!;
            return new SlowParserWalkthroughProject(root, projectPath);
        }
        catch
        {
            DeleteDirectory(root);
            throw;
        }
    }

    public void Dispose() => DeleteDirectory(_root);

    private static string StemForm(int index)
    {
        const string vowels = "aeiou";
        var encoded = new char[4];
        for (var position = encoded.Length - 1; position >= 0; position--)
        {
            encoded[position] = vowels[index % vowels.Length];
            index /= vowels.Length;
        }

        return $"e{new string(encoded)}e";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
