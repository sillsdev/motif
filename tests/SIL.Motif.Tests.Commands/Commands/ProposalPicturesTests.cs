using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ProposalPicturesTests(PristineProjectFixture pristine)
{
    private static string Version => MotifProductVersion.CurrentText;

    // Header-only pictures built at run time; the sniffer decodes nothing.
    internal static byte[] Png(int width, int height, int padding = 0)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        bytes.AddRange(BigEndian(width));
        bytes.AddRange(BigEndian(height));
        bytes.AddRange(new byte[] { 8, 2, 0, 0, 0 });
        bytes.AddRange(new byte[padding]);
        return [.. bytes];
    }

    internal static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8 };
        bytes.Add((byte)(height >> 8)); bytes.Add((byte)height);
        bytes.Add((byte)(width >> 8)); bytes.Add((byte)width);
        bytes.AddRange(new byte[] { 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1, 0xFF, 0xD9 });
        return [.. bytes];
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    [Fact]
    public void SnifferReadsTheTypeAndSizeOfAPngAndAJpegAndNothingElse()
    {
        Assert.Equal(("image/png", 640, 480), PictureSniffer.Read(Png(640, 480)));
        Assert.Equal(("image/jpeg", 300, 200), PictureSniffer.Read(Jpeg(300, 200)));
        Assert.Null(PictureSniffer.Read("GIF89a not supported"u8));
        Assert.Null(PictureSniffer.Read(Png(0, 10)));
        Assert.Null(PictureSniffer.Read(Png(PictureSniffer.MaxSide + 1, 10)));
        Assert.Null(PictureSniffer.Read([0xFF, 0xD8, 0xFF, 0xD9]));
    }

    [Fact]
    public void APersonAddsListsReadsAndRemovesAPictureOnAnOpenProposal()
    {
        var (path, id) = Finalized("pictured");
        var bytes = Png(40, 30, padding: 100);

        var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, bytes));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Equal("image/png", added.Value!.Picture!.MediaType);
        Assert.Equal((40, 30, bytes.Length), (added.Value.Picture.Width, added.Value.Picture.Height, added.Value.Picture.ByteCount));
        var listed = ProposalPictures.Show(new ShowPicturesRequest(path, Version, id));
        Assert.Equal(added.Value.PictureId, Assert.Single(listed.Value!.Pictures).PictureId);
        Assert.Null(listed.Value.DataBase64);
        var read = ProposalPictures.Show(new ShowPicturesRequest(path, Version, id, added.Value.PictureId));
        Assert.Equal(bytes, Convert.FromBase64String(read.Value!.DataBase64!));

        Assert.True(ProposalPictures.Remove(new RemovePictureRequest(path, Version, id, added.Value.PictureId)).Succeeded);
        Assert.Empty(ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures);
        Assert.Equal("proposal.not-found",
            ProposalPictures.Remove(new RemovePictureRequest(path, Version, id, added.Value.PictureId)).Refusal?.Code);
    }

    [Fact]
    public void ACommentCanHoldAPictureAndDeletingTheCommentRemovesIt()
    {
        var (path, id) = Finalized("on-comment");
        var comment = ProposalComments.Add(new AddCommentRequest(path, Version, id, "See this.", "John")).Value!.CommentId;

        var onComment = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Jpeg(20, 10), comment));
        var elsewhere = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Jpeg(20, 10), comment + 99));

        Assert.Equal(comment, onComment.Value!.Picture!.CommentId);
        Assert.Equal("proposal.not-found", elsewhere.Refusal?.Code);
        Assert.True(ProposalComments.Delete(new DeleteCommentRequest(path, Version, id, comment)).Succeeded);
        Assert.Empty(ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures);
    }

    [Fact]
    public void AnOversizeUnsupportedEmptyOrNumerousPictureIsRefusedAndNothingIsStored()
    {
        var (path, id) = Finalized("limits");

        var tooBig = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(10, 10, ProposalPictures.MaxBytes)));
        var notAPicture = ProposalPictures.Add(new AddPictureRequest(path, Version, id, "plain text, not a picture"u8.ToArray()));
        var empty = ProposalPictures.Add(new AddPictureRequest(path, Version, id, []));
        var atTheCap = ProposalPictures.Add(new AddPictureRequest(path, Version, id,
            Png(10, 10, ProposalPictures.MaxBytes - Png(10, 10).Length)));

        Assert.Equal("proposal.picture-invalid", tooBig.Refusal?.Code);
        Assert.Equal("proposal.picture-invalid", notAPicture.Refusal?.Code);
        Assert.Equal("proposal.picture-invalid", empty.Refusal?.Code);
        Assert.True(atTheCap.Succeeded, atTheCap.Refusal?.Message);
        Assert.Single(ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures);

        for (var i = 1; i < ProposalPictureRepository.MaxPicturesPerProposal; i++)
            Assert.True(ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(5, 5))).Succeeded);
        var one = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(5, 5)));
        Assert.Equal("proposal.picture-invalid", one.Refusal?.Code);
        Assert.Equal(ProposalPictureRepository.MaxPicturesPerProposal,
            ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures.Count);
    }

    [Fact]
    public void PicturesFreezeAtTheDecisionThawOnReopenAndGoWhenAnUnappliedProposalIsDeleted()
    {
        var (path, id) = Finalized("freeze");
        var picture = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8))).Value!.PictureId;
        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);

        Assert.Equal("proposal.closed", ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8))).Refusal?.Code);
        Assert.Equal("proposal.closed", ProposalPictures.Remove(new RemovePictureRequest(path, Version, id, picture)).Refusal?.Code);
        Assert.Single(ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures);

        Assert.True(ProposalCommands.Reopen(new ReopenRequest(path, Version, "thaw", id)).Succeeded);
        Assert.True(ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8))).Succeeded);
        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);

        Assert.True(ProposalLifecycleCommand.Delete(new DeleteProposalRequest(path, id)).Succeeded);
        Assert.Equal(0L, PictureRows(path));
    }

    [Fact]
    public void ThePictureCommandsAreHumanOnlyDeveloperCommands()
    {
        foreach (var name in new[] { "proposal picture add", "proposal picture remove", "proposal picture show" })
        {
            var command = CommandCatalog.All.Single(candidate => candidate.Name == name);
            Assert.Equal(AgentClass.HumanOnly, command.Agent);
            Assert.Equal(CommandSurface.Developer, command.Surface);
        }
    }

    private static long PictureRows(string path) =>
        ProjectStoreCommand.Run(path, Version, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ProposalPictures;";
            return CommandOutcome<string>.Success(Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!);
        }).Value is { } count ? long.Parse(count, System.Globalization.CultureInfo.InvariantCulture) : -1;

    private (string Path, string Id) Finalized(string name)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var created = ProposalCommands.New(new NewDraftRequest(path, Version, name, name));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, name,
            CanonicalId.Mint().Value, "en", name)).Succeeded);
        DraftRationale.Author(path, name, name, "Why " + name + " is right.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, name));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return (path, created.Value!.ProposalId);
    }
}
