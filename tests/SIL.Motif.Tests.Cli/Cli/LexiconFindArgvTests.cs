using System.Text.Json;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class LexiconFindArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-lexicon-find-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void FindsEntriesByFormRegexAndSuffixAndSpellsIdsEitherWay()
    {
        using var project = new WalkthroughProject(pristine, _root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), _root).Succeeded);
        var first = SeededProject.FirstForm;

        using var byRegex = Find(project, "--form-regex", "^" + System.Text.RegularExpressions.Regex.Escape(first) + "$");
        var entry = byRegex.RootElement.GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == CanonicalId.FromGuid(project.Seed.FirstEntryId).Value);
        Assert.Equal(first, entry.GetProperty("lexemeForm").GetString());
        Assert.Equal("baseline", byRegex.RootElement.GetProperty("at").GetString());

        using var bySuffix = Find(project, "--suffix", first[^1..], "--id-format", "guid", "--limit", "150");
        Assert.Contains(bySuffix.RootElement.GetProperty("entries").EnumerateArray(),
            item => item.GetProperty("id").GetString() == project.Seed.FirstEntryId.ToString("D"));

        using var none = Find(project, "--form-regex", "^$never-matches-anything");
        Assert.Equal(0, none.RootElement.GetProperty("matched").GetInt32());
    }

    [Fact]
    public void ABatchAnswersEveryLineFromOneOpenOfTheBaseline()
    {
        using var project = new WalkthroughProject(pristine, _root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), _root).Succeeded);
        var file = Path.Combine(_root, "queries.jsonl");
        File.WriteAllText(file, "{\"exact\":\"" + SeededProject.FirstForm + "\"}\n\n{\"form-regex\":\"(\"}\n{\"suffix\":\"zzzz\",\"limit\":5}\n");
        var result = CliInProcess.Run(_root, null, developerCommands: true,
            "lexicon", "find", "--project", project.FwDataPath, "--batch", file);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Equal(1, lines[0].GetProperty("result").GetProperty("matched").GetInt32());
        Assert.Equal("lexicon-find.invalid-request", lines[1].GetProperty("refusal").GetProperty("code").GetString());
        Assert.Equal(0, lines[2].GetProperty("result").GetProperty("matched").GetInt32());
    }

    [Fact]
    public void RefusesABadRegexAnOutOfRangePageAndAnUnknownIdFormat()
    {
        using var project = new WalkthroughProject(pristine, _root);
        foreach (var (extra, code) in new[]
                 {
                     (new[] { "--form-regex", "(" }, "lexicon-find.invalid-request"),
                     (new[] { "--id-format", "hex" }, "lexicon-find.invalid-request"),
                     (new[] { "--limit", "500" }, "paging.invalid"),
                 })
        {
            var result = CliInProcess.Run(_root, null, developerCommands: true,
                ["lexicon", "find", "--project", project.FwDataPath, .. extra, "--json"]);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(code, result.Output + result.Error, StringComparison.Ordinal);
        }
    }

    private JsonDocument Find(WalkthroughProject project, params string[] extra)
    {
        var result = CliInProcess.Run(_root, null, developerCommands: true,
            ["lexicon", "find", "--project", project.FwDataPath, .. extra, "--json"]);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        return JsonDocument.Parse(result.Output);
    }
}
