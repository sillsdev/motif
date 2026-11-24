using System.Diagnostics;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SkiaSharp;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class PictureAttachmentParityTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task WindowAndCliAttachTheSameJpegAsIdenticalPictures()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var version = MotifProductVersion.CurrentText;
        var draft = ProposalCommands.New(new NewDraftRequest(path, version, "picture parity", "picture parity"));
        Assert.True(draft.Succeeded, draft.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, version, "picture parity",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, "en", "picture parity")).Succeeded);
        DraftRationale.Author(path, "picture parity", "picture parity", "Why this change is right.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, version, "picture parity"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var id = finalized.Value!.ProposalId;

        using var bitmap = new SKBitmap(320, 240);
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)x, (byte)y, (byte)(x ^ y)));
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
        var source = encoded.ToArray();
        var file = Path.Combine(Path.GetDirectoryName(path)!, "picture.jpg");
        await File.WriteAllBytesAsync(file, source);

        var chosen = await AvaloniaPictureIntake.ReadAsync(new MemoryStream(source));
        Assert.Equal(source, chosen.Bytes);
        var model = new ProposalPicturesViewModel(
            (request, _) => Task.FromResult(ProposalPictures.Show(request)),
            (request, _) => Task.FromResult(ProposalPictures.Add(request)),
            (request, _) => Task.FromResult(ProposalPictures.Remove(request)), new ChosenPicture(chosen));
        await model.LoadAsync(path, id, canEdit: true);
        await model.AddCommand.ExecuteAsync(null);
        var windowBytes = Assert.Single(model.Items).Bytes;

        var start = new ProcessStartInfo(BuildOutput.Cli);
        foreach (var argument in new[] { "proposal", "picture", "add", id, "--project", path, "--file", file, "--json" })
            start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        var cli = ToolProcess.Run(start);
        Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
        var listed = ProposalPictures.Show(new ShowPicturesRequest(path, version, id));
        var cliPicture = Assert.Single(listed.Value!.Pictures, picture => picture.PictureId != model.Items[0].PictureId);
        var shown = ProposalPictures.Show(new ShowPicturesRequest(path, version, id, cliPicture.PictureId));
        Assert.Equal(windowBytes, Convert.FromBase64String(shown.Value!.DataBase64!));
    }

    [Fact]
    public async Task PickerRefusesADeclaredPictureOverThePixelLimitBeforeTheCommand()
    {
        byte[] source = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13,
            (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0x3A, 0x98, 0, 0, 0x3A, 0x98, 8, 2, 0, 0, 0];
        var chosen = await AvaloniaPictureIntake.ReadAsync(new MemoryStream(source));
        Assert.Equal(PictureIntakeStatus.TooLarge, chosen.Status);
    }

    private sealed class ChosenPicture(PictureIntakeResult result) : IPictureIntake
    {
        public Task<PictureIntakeResult> ChooseAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
