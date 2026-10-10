using System;
using System.Threading.Tasks;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ComposeEditAffixSlotTests : IDisposable
{
    private const string ProductVersion = "0.1.0";
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "motif-edit-affix-slot-" + Guid.NewGuid().ToString("N"));
    private readonly string _projectPath;
    private readonly string _workerRoot;
    private readonly string _preferencePath;
    private readonly CanonicalId _slotId;
    private readonly CanonicalId[] _relatedIds;

    public ComposeEditAffixSlotTests(PristineProjectFixture pristine)
    {
        System.IO.Directory.CreateDirectory(_root);
        _projectPath = pristine.CopyProjectFile();
        _workerRoot = System.IO.Path.Combine(_root, "worker");
        _preferencePath = System.IO.Path.Combine(_root, "advanced-ai-mode.json");

        using var cache = new FwDataProjectLoader().LoadCache(_projectPath);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var firstEntry = (ILexEntry)repository.GetObject(pristine.Seed.FirstEntryId);
        var secondEntry = (ILexEntry)repository.GetObject(pristine.Seed.SecondEntryId);
        var category = (IPartOfSpeech)repository.GetObject(pristine.Seed.PartOfSpeechId);
        IMoInflAffixSlot slot = null!;
        IMoInflAffixTemplate firstTemplate = null!;
        IMoInflAffixTemplate secondTemplate = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, "shared");
            firstTemplate = CreateTemplate(cache, category, "first template", slot, prefix: true);
            secondTemplate = CreateTemplate(cache, category, "second template", slot, prefix: false);
            AddInflectionalMsa(cache, firstEntry, category, slot);
            AddInflectionalMsa(cache, secondEntry, category, slot);
        });
        _slotId = CanonicalId.FromGuid(slot.Guid);
        _relatedIds =
        [
            CanonicalId.FromGuid(firstTemplate.Guid),
            CanonicalId.FromGuid(secondTemplate.Guid),
            CanonicalId.FromGuid(firstEntry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Single().Guid),
            CanonicalId.FromGuid(secondEntry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Single().Guid),
        ];
        new FwDataProjectLoader().Save(cache);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task CliReportsEverySharedUserAndReplacesPriorSlotEditsInTheDraft()
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_projectPath, ProductVersion, "slot-edit", null)).Succeeded);
        new FileAdvancedAiModePreferenceStore(_preferencePath).SetEnabled(true);

        var first = await RunComposeAsync(optional: true);
        Assert.True(first.ExitCode == 0, first.Error + Environment.NewLine + first.Output);
        Assert.Contains("Draft now has 1 operation(s).", first.Output, StringComparison.Ordinal);
        foreach (var id in _relatedIds)
            Assert.Contains(id.Value, first.Output, StringComparison.Ordinal);

        var reset = await RunComposeAsync(optional: false);
        Assert.True(reset.ExitCode == 0, reset.Error + Environment.NewLine + reset.Output);
        Assert.Contains("Draft now has 0 operation(s).", reset.Output, StringComparison.Ordinal);

        var replacement = await RunComposeAsync(optional: true);
        Assert.True(replacement.ExitCode == 0, replacement.Error + Environment.NewLine + replacement.Output);
        Assert.Contains("1 operation(s) added.", replacement.Output, StringComparison.Ordinal);
        Assert.Contains("Draft now has 1 operation(s).", replacement.Output, StringComparison.Ordinal);

        using var saved = new FwDataProjectLoader().LoadCache(_projectPath);
        var slot = (IMoInflAffixSlot)saved.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(_slotId.ToGuid());
        Assert.False(slot.Optional);
    }

    private Task<(int ExitCode, string Output, string Error)> RunComposeAsync(bool optional)
    {
        var intent = System.Text.Json.JsonSerializer.Serialize(new
        {
            target = _slotId.Value,
            expectedOptional = false,
            optional,
        });
        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(_workerRoot, null,
            developerCommands: false, _preferencePath,
            "compose-edit-affix-slot", "--project", _projectPath, "--draft", "slot-edit",
            "--intent", intent);
        return CliProcess.RunAsync(start);
    }

    private static IMoInflAffixTemplate CreateTemplate(LcmCache cache, IPartOfSpeech category, string name,
        IMoInflAffixSlot slot, bool prefix)
    {
        var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
        category.AffixTemplatesOS.Add(template);
        template.Name.set_String(cache.DefaultAnalWs, name);
        if (prefix) template.PrefixSlotsRS.Add(slot);
        else template.SuffixSlotsRS.Add(slot);
        return template;
    }

    private static IMoInflAffMsa AddInflectionalMsa(LcmCache cache, ILexEntry entry,
        IPartOfSpeech category, IMoInflAffixSlot slot)
    {
        var msa = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
            entry, SandboxGenericMSA.Create(MsaType.kInfl, category));
        msa.SlotsRC.Add(slot);
        return msa;
    }
}
