using System;
using System.IO;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Cli;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AlternateFormAuthoringTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _workerRoot = CreateWorkerRoot();

    [Fact]
    public void AlternateFormCommandReturnsItsCreateThroughThePublicCliRoute()
    {
        var entry = CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value;
        string path;
        string formWs;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            formWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        }

        var draft = SIL.Motif.Commands.ProposalCommands.New(
            new SIL.Motif.Commands.Requests.NewDraftRequest(path, SIL.Motif.Host.MotifProductVersion.CurrentText,
                "alternate-form", null));
        Assert.True(draft.Succeeded, draft.Refusal?.Message);

        var intent = JsonSerializer.Serialize(new
        {
            entry,
            form = "motifa-alt",
            formWs,
            morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
        });
        var composed = CliInProcess.Run(_workerRoot, null, developerCommands: true,
            "compose-author-alternate-form", "--project", path, "--draft", "alternate-form",
            "--intent", intent, "--json");

        Assert.True(composed.ExitCode == 0, composed.Error);
        var createLine = Assert.Single(composed.Output.Split(Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries), line =>
            line.Contains("(lexical/lexEntry/createAlternateForms)", StringComparison.Ordinal));
        var marker = ") creates ";
        var markerIndex = createLine.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, createLine);
        var createdId = createLine[(markerIndex + marker.Length)..].Trim();
        Assert.True(CanonicalId.TryParse(createdId, out _), $"Expected a canonical created id in: {createLine}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); }
        catch (IOException) { }
    }

    private static string CreateWorkerRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-alternate-form-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
