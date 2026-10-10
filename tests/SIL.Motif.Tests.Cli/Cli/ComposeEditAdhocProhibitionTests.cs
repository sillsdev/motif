using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ComposeEditAdhocProhibitionTests : IDisposable
{
    private const string ProductVersion = "0.1.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-edit-adhoc-" + Guid.NewGuid().ToString("N"));
    private readonly string _projectPath;
    private readonly string _workerRoot;
    private readonly string _preferencePath;
    private readonly CanonicalId _target;

    public ComposeEditAdhocProhibitionTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        _projectPath = pristine.CopyProjectFile();
        _workerRoot = Path.Combine(_root, "worker");
        _preferencePath = Path.Combine(_root, "advanced-ai-mode.json");

        using var cache = new FwDataProjectLoader().LoadCache(_projectPath);
        var prohibition = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
            prohibition.Disabled = false;
        });
        _target = CanonicalId.FromGuid(prohibition.Guid);
        new FwDataProjectLoader().Save(cache);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task CliStagesOneDisabledWriteWithoutChangingTheSavedProjectAndRefusesADuplicate()
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, "disable-duplicate",
            "Disable one duplicate ad hoc prohibition")).Succeeded);
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(true);

        var composed = await RunComposeAsync(expectedDisabled: false, disabled: true);

        Assert.True(composed.ExitCode == 0, composed.Error + Environment.NewLine + composed.Output);
        Assert.Contains("grammar/moAdhocProhib/setDisabled", composed.Output, StringComparison.Ordinal);
        using (var saved = new FwDataProjectLoader().LoadCache(_projectPath))
        {
            var prohibition = (IMoAlloAdhocProhib)saved.ServiceLocator.GetInstance<ICmObjectRepository>()
                .GetObject(_target.ToGuid());
            Assert.False(prohibition.Disabled);
        }

        var duplicate = await RunComposeAsync(expectedDisabled: true, disabled: false);

        Assert.True(duplicate.ExitCode == 2, duplicate.Error + Environment.NewLine + duplicate.Output);
        var refusalText = string.IsNullOrWhiteSpace(duplicate.Output) ? duplicate.Error : duplicate.Output;
        using var refusal = JsonDocument.Parse(refusalText);
        Assert.Equal("draft.invalid", refusal.RootElement.GetProperty("code").GetString());
        Assert.Contains("only one write to an ad hoc prohibition's Disabled field",
            refusal.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        using var stillSaved = new FwDataProjectLoader().LoadCache(_projectPath);
        var stillUnchanged = (IMoAlloAdhocProhib)stillSaved.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(_target.ToGuid());
        Assert.False(stillUnchanged.Disabled);
    }

    private Task<(int ExitCode, string Output, string Error)> RunComposeAsync(bool expectedDisabled, bool disabled)
    {
        var intent = JsonSerializer.Serialize(new
        {
            target = _target.Value,
            expectedDisabled,
            disabled,
        });
        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(_workerRoot, null,
            developerCommands: false, _preferencePath,
            "compose-edit-adhoc-prohibition", "--project", _projectPath, "--draft", "disable-duplicate",
            "--intent", intent, "--json");
        return CliProcess.RunAsync(start);
    }
}
