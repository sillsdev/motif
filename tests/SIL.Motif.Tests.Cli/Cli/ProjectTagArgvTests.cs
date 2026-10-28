using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ProjectTagArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-project-tag-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;

    public static TheoryData<string[]> ForwardedSetterCannotSupplyMissingValueCases => new()
    {
        new[] { "--json", "--set", "--", "--set", "cebu" },
        new[] { "--set", "--json", "--", "--set", "cebu" },
    };

    public static TheoryData<string[], bool, string> RepeatedSetterCases => new()
    {
        { new[] { "--set", "fji", "--set", "tuv", "--" }, true, "tuv" },
        { new[] { "--set", "fji", "--set", "--json", "--" }, false, "ceb" },
    };

    public ProjectTagArgvTests()
    {
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task ANewProjectUsesItsVernacularCodeAndARepeatedCodeUsesItsFolder()
    {
        var first = Project("cebuano", "ceb");
        var second = Project("wolff", "ceb");

        Assert.Equal("ceb", await Read(first));
        Assert.Equal("wolff", await Read(second));

        var text = await Run("project", "tag", "--project", first);
        Assert.True(text.ExitCode == 0, ProcessFailure(text));
        Assert.Equal("Project tag: ceb" + Environment.NewLine, text.Output);
    }

    [Fact]
    public async Task ATwoLetterVernacularUsesItsThreeLetterLanguageCode()
    {
        var project = Project("french", "fr-Latn");

        Assert.Equal("fra", await Read(project));
    }

    [Fact]
    public async Task AnInvalidOrTakenFolderFallbackIsRefusedWithThePersonSetter()
    {
        var first = Project("first", "wol");
        Assert.Equal("wol", await Read(first));

        var invalidFolder = Project("###", "wol");
        var invalid = await Run("project", "tag", "--project", invalidFolder, "--json");
        Assert.True(invalid.ExitCode != 0, ProcessFailure(invalid));
        Assert.Contains("project.tag-invalid", invalid.Error, StringComparison.Ordinal);
        Assert.Contains("project tag --set", invalid.Error, StringComparison.Ordinal);

        var folderOwner = Project("folder-owner", "eng");
        Assert.Equal("eng", await Read(folderOwner));
        var setFolderTag = await Run("project", "tag", "--project", folderOwner, "--set", "wolff", "--json");
        Assert.True(setFolderTag.ExitCode == 0, ProcessFailure(setFolderTag));
        var takenFolder = Project("nested/wolff", "wol");
        var taken = await Run("project", "tag", "--project", takenFolder, "--json");
        Assert.True(taken.ExitCode != 0, ProcessFailure(taken));
        Assert.Contains("project.tag-taken", taken.Error, StringComparison.Ordinal);
        Assert.Contains("project tag --set", taken.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASetFlagWithoutAValueRefusesAndLeavesTheExistingTag(bool jsonFollowsSet)
    {
        var project = Project("cebuano", "ceb");
        Assert.Equal("ceb", await Read(project));

        var explicitTag = await Run("project", "tag", "--project", project, "--set", "cebu", "--json");
        Assert.True(explicitTag.ExitCode == 0, ProcessFailure(explicitTag));
        Assert.Equal("cebu", await Read(project));

        var arguments = new List<string> { "project", "tag", "--project", project, "--set" };
        if (jsonFollowsSet) arguments.Add("--json");
        var missingValue = await Run(arguments.ToArray());
        Assert.True(missingValue.ExitCode != 0, ProcessFailure(missingValue));
        Assert.Contains("project tag --set", missingValue.Error, StringComparison.Ordinal);
        Assert.Equal("cebu", await Read(project));
    }

    [Theory]
    [MemberData(nameof(ForwardedSetterCannotSupplyMissingValueCases))]
    public async Task AForwardedSetterCannotSupplyAMissingMotifSetterValue(string[] argumentsAfterProject)
    {
        var project = Project("cebuano", "ceb");
        Assert.Equal("ceb", await Read(project));

        var arguments = new List<string> { "project", "tag", "--project", project };
        arguments.AddRange(argumentsAfterProject);
        var missingValue = await Run(arguments.ToArray());

        Assert.True(missingValue.ExitCode != 0, ProcessFailure(missingValue));
        Assert.Contains("project tag --set", missingValue.Error, StringComparison.Ordinal);
        Assert.Equal("ceb", await Read(project));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASetterBeforeForwardingCannotBeInvalidatedOrOverriddenByTheForwardedSetter(
        bool forwardedSetterHasValue)
    {
        var project = Project("cebuano", "ceb");
        var arguments = new List<string>
        {
            "project", "tag", "--project", project, "--set", "fji", "--", "--set",
        };
        if (forwardedSetterHasValue) arguments.Add("tuv");

        var set = await Run(arguments.ToArray());

        Assert.True(set.ExitCode == 0, ProcessFailure(set));
        Assert.Equal("fji", await Read(project));
    }

    [Theory]
    [MemberData(nameof(RepeatedSetterCases))]
    public async Task TheLastSetterBeforeForwardingControlsTheTag(
        string[] setterArguments, bool succeeds, string expectedTag)
    {
        var project = Project("cebuano", "ceb");
        var arguments = new List<string> { "project", "tag", "--project", project };
        arguments.AddRange(setterArguments);
        var set = await Run(arguments.ToArray());

        Assert.True((set.ExitCode == 0) == succeeds, ProcessFailure(set));
        if (!succeeds)
            Assert.Contains("project tag --set", set.Error, StringComparison.Ordinal);
        Assert.Equal(expectedTag, await Read(project));
    }

    [Fact]
    public async Task TheLiteralTrueIsAValidExplicitTag()
    {
        var project = Project("cebuano", "ceb");
        Assert.Equal("ceb", await Read(project));

        var set = await Run("project", "tag", "--project", project, "--set", "true", "--json");
        Assert.True(set.ExitCode == 0, ProcessFailure(set));

        Assert.Equal("true", await Read(project));
    }

    [Fact]
    public async Task APersonCanSetAndReadAValidTagAndInvalidOrTakenTagsAreRefused()
    {
        var first = Project("cebuano", "ceb");
        var second = Project("wolff", "wol");
        Assert.Equal("ceb", await Read(first));
        Assert.Equal("wol", await Read(second));

        var set = await Run("project", "tag", "--project", first, "--set", "cebu", "--json");
        Assert.True(set.ExitCode == 0, ProcessFailure(set));
        Assert.Equal("cebu", await Read(first));

        var invalid = await Run("project", "tag", "--project", first, "--set", "Bad_Tag", "--json");
        Assert.True(invalid.ExitCode != 0, ProcessFailure(invalid));
        Assert.Contains("project.tag-invalid", invalid.Error, StringComparison.Ordinal);
        Assert.Equal("cebu", await Read(first));

        var taken = await Run("project", "tag", "--project", second, "--set", "cebu", "--json");
        Assert.True(taken.ExitCode != 0, ProcessFailure(taken));
        Assert.Contains("project.tag-taken", taken.Error, StringComparison.Ordinal);
        Assert.Equal("wol", await Read(second));

        var otherCommand = await Run("new", "--project", first, "--draft", "local-draft", "--json");
        Assert.True(otherCommand.ExitCode == 0, ProcessFailure(otherCommand));
        Assert.Equal("cebu", await Read(first));
    }

    private string Project(string folder, string vernacular)
    {
        var path = Path.Combine(_root, "projects", folder, "project.fwdata");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <languageproject version="7000072">
              <rt class="LangProject" guid="{Guid.NewGuid():D}" ownerguid="">
                <CurVernWss><Uni>{vernacular}</Uni></CurVernWss>
              </rt>
            </languageproject>
            """);
        return path;
    }

    private async Task<string> Read(string project)
    {
        var result = await Run("project", "tag", "--project", project, "--json");
        Assert.True(result.ExitCode == 0, ProcessFailure(result));
        return JsonDocument.Parse(result.Output).RootElement.GetProperty("tag").GetString()!;
    }

    private async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments)
    {
        var start = CliProcess.CreateStartInfo(_workerRoot, null, developerCommands: true, arguments: arguments);
        CliProcess.EnableAdvancedAiMode(start, _workerRoot);
        return await CliInProcess.RunAsync(start);
    }

    private static string ProcessFailure((int ExitCode, string Output, string Error) result) =>
        $"Exit {result.ExitCode}.{Environment.NewLine}Standard error:{Environment.NewLine}{result.Error}" +
        $"{Environment.NewLine}Standard output:{Environment.NewLine}{result.Output}";

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
