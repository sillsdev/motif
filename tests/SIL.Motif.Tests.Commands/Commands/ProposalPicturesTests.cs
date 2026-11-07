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

    // A header-only lossless WebP: RIFF, WEBP, one VP8L chunk with its signature byte and packed size.
    internal static byte[] Webp(int width, int height)
    {
        var packed = (uint)(width - 1) | (uint)(height - 1) << 14;
        return [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 22, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P',
            (byte)'V', (byte)'P', (byte)'8', (byte)'L', 10, 0, 0, 0, 0x2F, (byte)packed, (byte)(packed >> 8),
            (byte)(packed >> 16), (byte)(packed >> 24), 0, 0, 0, 0, 0];
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    [Fact]
    public void SnifferReadsTheTypeAndSizeOfAPngAndAJpegAndNothingElse()
    {
        Assert.Equal(("image/png", 640, 480), PictureSniffer.Read(Png(640, 480)));
        Assert.Equal(("image/jpeg", 300, 200), PictureSniffer.Read(Jpeg(300, 200)));
        Assert.Equal(("image/webp", 640, 480), PictureSniffer.Read(Webp(640, 480)));
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

        Assert.Equal(10, ProposalPictureRepository.MaxPicturesPerProposal);
        for (var i = 1; i < ProposalPictureRepository.MaxPicturesPerProposal; i++)
            Assert.True(ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(5, 5))).Succeeded);
        var one = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(5, 5)));
        Assert.Equal("proposal.picture-invalid", one.Refusal?.Code);
        Assert.Contains("Remove one to add another", one.Refusal?.Message);
        Assert.Equal(10, ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures.Count);
    }

    [Fact]
    public void AProposalOverTheCapKeepsItsPicturesAndRefusesOnlyNewOnes()
    {
        var (path, id) = Finalized("over-cap");
        ProjectStoreCommand.Run(path, Version, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var insert = connection.CreateCommand();
            insert.CommandText = "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 12) " +
                "INSERT INTO ProposalPictures (ProposalId, MediaType, Width, Height, Bytes, CreatedUtc) " +
                "SELECT $id, 'image/png', 5, 5, x'00', '2026-11-06T00:00:00Z' FROM n;";
            insert.Parameters.AddWithValue("$id", id);
            insert.ExecuteNonQuery();
            return CommandOutcome<string>.Success("");
        });

        var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(5, 5)));

        Assert.Equal("proposal.picture-invalid", added.Refusal?.Code);
        Assert.Equal(12, ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures.Count);
    }

    [Fact]
    public void APictureLongerThanAThousandPixelsIsRefusedAndOneOfExactlyThatIsKept()
    {
        var (path, id) = Finalized("side");

        var tooWide = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Webp(1001, 10)));
        var exact = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Webp(1000, 700)));

        Assert.Equal("proposal.picture-invalid", tooWide.Refusal?.Code);
        Assert.Contains("1000 pixels", tooWide.Refusal?.Message);
        Assert.True(exact.Succeeded, exact.Refusal?.Message);
        Assert.Equal("image/webp", exact.Value!.Picture!.MediaType);
    }

    private static byte[] Stored(string path, string id, long pictureId) => Convert.FromBase64String(
        ProposalPictures.Show(new ShowPicturesRequest(path, Version, id, pictureId)).Value!.DataBase64!);

    private static bool Has(byte[] bytes, string ascii) =>
        bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(ascii)) >= 0;

    [Fact]
    public void AnAttachedPictureIsStoredWithoutItsMetadata()
    {
        var (path, id) = Finalized("metadata");
        var jpeg = Jpeg(20, 10);
        byte[] exif = [0xFF, 0xE1, 0, 16, .. "Exif\0\0GPS-HERE"u8.ToArray()];
        byte[] comment = [0xFF, 0xFE, 0, 10, .. "camera-1"u8.ToArray()];
        var taggedJpeg = (byte[])[.. jpeg[..4], 0, 4, 0, 0, .. exif, .. comment, .. jpeg[8..]];
        var png = Png(8, 8, padding: 4);
        byte[] text = [0, 0, 0, 8, .. "tEXt"u8.ToArray(), .. "GPS=1234"u8.ToArray(), 0, 0, 0, 0];
        var taggedPng = (byte[])[.. png[..33], .. text, .. png[33..]];
        var webp = Webp(30, 20);
        byte[] webpExif = [.. "EXIF"u8.ToArray(), 4, 0, 0, 0, .. "GPS!"u8.ToArray()];
        var taggedWebp = (byte[])[.. webp, .. webpExif];
        taggedWebp[4] = (byte)(taggedWebp.Length - 8);

        Assert.True(Has(taggedJpeg, "GPS-HERE") && Has(taggedPng, "GPS=") && Has(taggedWebp, "GPS!"));
        foreach (var (bytes, marker) in new[] { (taggedJpeg, "GPS-HERE"), (taggedPng, "GPS="), (taggedWebp, "GPS!") })
        {
            var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, bytes));

            Assert.True(added.Succeeded, added.Refusal?.Message);
            var kept = Stored(path, id, added.Value!.PictureId);
            Assert.False(Has(kept, marker), marker);
            Assert.True(kept.Length < bytes.Length);
            Assert.NotNull(PictureSniffer.Read(kept));
        }
        Assert.Equal(jpeg, PictureMetadata.Strip(taggedJpeg));
        Assert.Equal(webp, PictureMetadata.Strip(taggedWebp));
        Assert.Equal(png, PictureMetadata.Strip(taggedPng));
    }

    [Fact]
    public void ARefusedPictureSaysItsSizeAndWhatToDo()
    {
        var (path, id) = Finalized("messages");

        var wide = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(1920, 1080)));
        var heavy = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(10, 10, ProposalPictures.MaxBytes)));
        var lines = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8), Caption: "a\nb"));

        Assert.Contains("1920 x 1080 pixels", wide.Refusal?.Message);
        Assert.Contains("at most 1000 pixels", wide.Refusal?.Message);
        Assert.Contains("Motif window", wide.Refusal?.Message);
        Assert.Contains("bytes; Motif keeps pictures at most 1048576 bytes", heavy.Refusal?.Message);
        Assert.Contains("caption is one line", lines.Refusal?.Message);
    }

    [Fact]
    public void ACaptionIsStoredWithThePictureAndListedWithIt()
    {
        var (path, id) = Finalized("caption");

        var captioned = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8), Caption: "  The chart  "));
        var plain = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8), Caption: "  "));
        var twoLines = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8), Caption: "one\ntwo"));
        var tooLong = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(8, 8),
            Caption: new string('x', ProposalPictures.MaxCaptionLength + 1)));

        Assert.Equal("The chart", captioned.Value!.Picture!.Caption);
        Assert.Null(plain.Value!.Picture!.Caption);
        Assert.Equal("proposal.picture-invalid", twoLines.Refusal?.Code);
        Assert.Equal("proposal.picture-invalid", tooLong.Refusal?.Code);
        var listed = ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures;
        Assert.Equal(["The chart", null], listed.Select(picture => picture.Caption));
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
