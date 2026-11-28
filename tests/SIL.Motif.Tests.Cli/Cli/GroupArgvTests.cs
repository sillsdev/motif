using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GroupArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-group-argv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<CliProcessResult> Run(string project, params string[] options)
    {
        Directory.CreateDirectory(_root);
        return await CliProcess.RunAsync(_root, null, false, ["group", "suffix", "--project", project, "--ending", "an", .. options]);
    }

    private static string Normalize(string text, string project) => text.Replace(project, "<project>", StringComparison.Ordinal).Replace("\r\n", "\n");

    private static void Matches(string golden, string actual)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var path = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.Cli", "Golden", golden);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, actual);
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }

    private static void Outline(JsonElement element, string path, List<string> lines)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) Outline(property.Value, path + "/" + property.Name, lines);
        else if (element.ValueKind == JsonValueKind.Array)
        {
            lines.Add(path + " array");
            if (element.GetArrayLength() > 0) Outline(element[0], path + "[]", lines);
        }
        else lines.Add(path + " " + element.ValueKind);
    }

    [Fact]
    public async Task TextOutputMatchesItsGolden()
    {
        var project = GroupCorpusFixture.Create(pristine);
        var result = await Run(project);
        Assert.True(result.ExitCode == 0, result.Error);
        Matches("group-suffix.txt", Normalize(result.Output, project));
    }

    [Fact]
    public async Task JsonOutputMatchesItsGoldenAndFormatJsonIsTheSame()
    {
        var project = GroupCorpusFixture.Create(pristine);
        var json = await Run(project, "--json");
        var format = await Run(project, "--format", "json");
        Assert.True(json.ExitCode == 0, json.Error);
        Assert.Equal(json.Output, format.Output);
        using var document = JsonDocument.Parse(json.Output);
        Assert.Equal(3, document.RootElement.GetProperty("groups").GetInt32());
        var rows = document.RootElement.GetProperty("rows");
        Assert.Equal(["known", "fitting", "not-fitting"], rows.EnumerateArray().Select(row => row.GetProperty("status").GetString()!).ToArray());
        var shape = new List<string>();
        Outline(document.RootElement, "", shape);
        Matches("group-suffix.shape.txt", string.Join('\n', shape));
    }

    [Fact]
    public async Task BadFlagsAreUsageErrorsAndAReadDoesNotChangeTheProject()
    {
        var project = GroupCorpusFixture.Create(pristine);
        var before = File.ReadAllBytes(project);
        Assert.NotEqual(0, (await Run(project, "--format", "yaml")).ExitCode);
        Assert.NotEqual(0, (await Run(project, "--limit", "0")).ExitCode);
        Assert.NotEqual(0, (await Run(project, "--page", "9", "--json")).ExitCode);
        var refused = await Run(project, "--cursor", "9", "--json");
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("--cursor", refused.Output + refused.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(project));
    }
}
