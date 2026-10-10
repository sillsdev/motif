using System.Text.Json;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProjectInitializationArgvTests : IDisposable
{
    private const string Confirmation = "Initialize this project to work with Motif Proposals?";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "motif-project-initialization-cli-" + Guid.NewGuid().ToString("N"));
    private string Project => NewLangProjFixture.FwDataPath(_root);
    private string WorkerRoot => Path.Combine(_root, "worker");
    private string PreferencePath => Path.Combine(_root, "advanced-ai-mode.json");

    public ProjectInitializationArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(WorkerRoot);
        using var cache = NewLangProjFixture.CreateCache(_root);
        new FwDataProjectLoader().Save(cache);
    }

    [Fact]
    public async Task InitializationRequiresAdvancedAiMode()
    {
        new FileAdvancedAiModePreferenceStore(PreferencePath).SetEnabled(false);
        var before = File.ReadAllBytes(Project);
        var result = await Run("project", "initialize", "--project", Project,
            "--confirm", Confirmation, "--json");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("command.advanced-ai-mode-required", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(Project));
    }

    [Fact]
    public async Task InitializationRequiresTheExactConfirmationAndReturnsItsTypedResult()
    {
        new FileAdvancedAiModePreferenceStore(PreferencePath).SetEnabled(true);
        var before = File.ReadAllBytes(Project);
        var missingConfirmation = await Run("project", "initialize", "--project", Project, "--json");

        Assert.NotEqual(0, missingConfirmation.ExitCode);
        using (var refusal = JsonDocument.Parse(missingConfirmation.Error))
        {
            Assert.Equal("initialization.confirmation-required",
                refusal.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(before, File.ReadAllBytes(Project));

        var initialized = await Run("project", "initialize", "--project", Project,
            "--confirm", Confirmation, "--json");

        Assert.Equal(0, initialized.ExitCode);
        using var response = JsonDocument.Parse(initialized.Output);
        Assert.Equal("initialized", response.RootElement.GetProperty("status").GetString());
        Assert.Equal("MotifHumanJudgment", response.RootElement.GetProperty("fieldName").GetString());
        Assert.NotEqual(before, File.ReadAllBytes(Project));
    }

    [Fact]
    public async Task InitializationHelpNamesTheConfirmationAndItsAdvancedAiSurface()
    {
        new FileAdvancedAiModePreferenceStore(PreferencePath).SetEnabled(true);
        var result = await Run("help", "project initialize", "--full");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(Confirmation, result.Output, StringComparison.Ordinal);
        Assert.Contains("Advanced AI mode", result.Output, StringComparison.Ordinal);
    }

    private async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments) =>
        await CliProcess.RunAsync(CliProcess.CreateStartInfoWithAdvancedAiModePath(
            WorkerRoot, null, developerCommands: false, PreferencePath, arguments));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
