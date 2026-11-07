using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SkiaSharp;
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

    // A real picture drawn at run time: a gradient with a pattern.
    internal static byte[] Real(int width, int height, bool jpeg = false)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 255 / width), (byte)(y * 255 / height), (byte)((x ^ y) & 255)));
        using var data = bitmap.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 95);
        return data.ToArray();
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
        var bytes = Real(40, 30);

        var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, bytes));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Equal("image/webp", added.Value!.Picture!.MediaType);
        Assert.Equal((40, 30), (added.Value.Picture.Width, added.Value.Picture.Height));
        var listed = ProposalPictures.Show(new ShowPicturesRequest(path, Version, id));
        Assert.Equal(added.Value.PictureId, Assert.Single(listed.Value!.Pictures).PictureId);
        Assert.Null(listed.Value.DataBase64);
        var read = ProposalPictures.Show(new ShowPicturesRequest(path, Version, id, added.Value.PictureId));
        var kept = Convert.FromBase64String(read.Value!.DataBase64!);
        Assert.Equal(kept.Length, added.Value.Picture.ByteCount);
        Assert.Equal(("image/webp", 40, 30), PictureSniffer.Read(kept));

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

        var onComment = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(20, 10, jpeg: true), comment));
        var elsewhere = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(20, 10, jpeg: true), comment + 99));

        Assert.Equal(comment, onComment.Value!.Picture!.CommentId);
        Assert.Equal("proposal.not-found", elsewhere.Refusal?.Code);
        Assert.True(ProposalComments.Delete(new DeleteCommentRequest(path, Version, id, comment)).Succeeded);
        Assert.Empty(ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures);
    }

    [Fact]
    public void AnUnsupportedEmptyUnreadableOrNumerousPictureIsRefusedAndNothingIsStored()
    {
        var (path, id) = Finalized("limits");

        var notAPicture = ProposalPictures.Add(new AddPictureRequest(path, Version, id, "plain text, not a picture"u8.ToArray()));
        var empty = ProposalPictures.Add(new AddPictureRequest(path, Version, id, []));
        var headerOnly = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(40, 30)));
        var bomb = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Png(15000, 15000)));

        Assert.Equal("proposal.picture-invalid", notAPicture.Refusal?.Code);
        Assert.Contains("Only WebP, PNG, JPEG, GIF and BMP", notAPicture.Refusal?.Message);
        Assert.Equal("proposal.picture-invalid", empty.Refusal?.Code);
        Assert.Contains("cannot be opened", headerOnly.Refusal?.Message);
        Assert.Contains("too large to attach", bomb.Refusal?.Message);
        Assert.Empty(ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures);

        Assert.Equal(10, ProposalPictureRepository.MaxPicturesPerProposal);
        var small = Real(5, 5);
        for (var i = 0; i < ProposalPictureRepository.MaxPicturesPerProposal; i++)
            Assert.True(ProposalPictures.Add(new AddPictureRequest(path, Version, id, small)).Succeeded);
        var one = ProposalPictures.Add(new AddPictureRequest(path, Version, id, small));
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

        var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(5, 5)));

        Assert.Equal("proposal.picture-invalid", added.Refusal?.Code);
        Assert.Equal(12, ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures.Count);
    }

    [Fact]
    public void AFullHdPngIsStoredAsALosslessWebpWithItsLongSideAtAThousandPixels()
    {
        var (path, id) = Finalized("side");

        var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(1920, 1080)));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        var kept = Stored(path, id, added.Value!.PictureId);
        var (mediaType, width, height) = PictureSniffer.Read(kept)!.Value;
        Assert.Equal("image/webp", mediaType);
        Assert.Equal(1000, width);
        Assert.InRange(height, 562, 563);
        Assert.Equal((width, height), (added.Value.Picture!.Width, added.Value.Picture.Height));
        Assert.True(Has(kept, "VP8L"), "a PNG becomes a lossless WebP");
        Assert.True(kept.Length <= ProposalPictures.MaxBytes);
    }

    private static byte[] Stored(string path, string id, long pictureId) => Convert.FromBase64String(
        ProposalPictures.Show(new ShowPicturesRequest(path, Version, id, pictureId)).Value!.DataBase64!);

    private static bool Has(byte[] bytes, string ascii) =>
        bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(ascii)) >= 0;

    [Fact]
    public void AGpsTaggedLargeJpegIsStoredAsALossyWebpWithoutItsMetadata()
    {
        var (path, id) = Finalized("metadata");
        var jpeg = Real(1600, 1200, jpeg: true);
        byte[] exif = [0xFF, 0xE1, 0, 16, .. "Exif\0\0GPS-HERE"u8.ToArray()];
        byte[] comment = [0xFF, 0xFE, 0, 10, .. "camera-1"u8.ToArray()];
        var tagged = (byte[])[.. jpeg[..2], .. exif, .. comment, .. jpeg[2..]];
        Assert.True(Has(tagged, "GPS-HERE") && Has(tagged, "camera-1"));

        var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, tagged));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        var kept = Stored(path, id, added.Value!.PictureId);
        Assert.Equal(("image/webp", 1000, 750), PictureSniffer.Read(kept));
        Assert.False(Has(kept, "VP8L"), "a JPEG becomes a lossy WebP");
        foreach (var marker in new[] { "GPS-HERE", "camera-1", "Exif", "EXIF", "XMP" })
            Assert.False(Has(kept, marker), marker);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TaggedPngAndWebpAreStoredWithoutMetadata(bool png)
    {
        var (path, id) = Finalized("tagged-picture");
        var source = png ? TaggedPng() : TaggedWebp();
        Assert.True(Has(source, "GPS-HERE"));
        using var decoded = SKBitmap.Decode(source);
        Assert.NotNull(decoded);

        var added = ProposalPictures.Add(new AddPictureRequest(path, Version, id, source));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        var kept = Stored(path, id, added.Value!.PictureId);
        Assert.Equal(("image/webp", 40, 30), PictureSniffer.Read(kept));
        Assert.False(Has(kept, "GPS-HERE"));
        Assert.False(Has(kept, "XMP"));
    }

    private static byte[] TaggedPng()
    {
        var png = Real(40, 30);
        byte[] type = "tEXt"u8.ToArray();
        byte[] text = "Comment\0GPS-HERE"u8.ToArray();
        byte[] content = [.. type, .. text];
        uint crc = uint.MaxValue;
        foreach (var value in content)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return [.. png[..33], .. BigEndian(text.Length), .. content, .. BigEndian(unchecked((int)~crc)), .. png[33..]];
    }

    private static byte[] TaggedWebp()
    {
        using var bitmap = SKBitmap.Decode(Real(40, 30));
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 80);
        var webp = data.ToArray();
        byte[] xmp = "<x:xmpmeta xmlns:x='adobe:ns:meta/'>GPS-HERE</x:xmpmeta>"u8.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WEBP"u8);
        if (!webp.AsSpan(12, 4).SequenceEqual("VP8X"u8))
        {
            writer.Write("VP8X"u8);
            writer.Write(10);
            writer.Write(new byte[] { 4, 0, 0, 0, 39, 0, 0, 29, 0, 0 });
        }
        writer.Write(webp.AsSpan(12));
        if (webp.AsSpan(12, 4).SequenceEqual("VP8X"u8))
        {
            stream.Position = 20;
            writer.Write((byte)(webp[20] | 4));
            stream.Position = stream.Length;
        }
        writer.Write("XMP "u8);
        writer.Write(xmp.Length);
        writer.Write(xmp);
        if (xmp.Length % 2 != 0) writer.Write((byte)0);
        stream.Position = 4;
        writer.Write((int)stream.Length - 8);
        return stream.ToArray();
    }

    [Fact]
    public void ARefusedCaptionSaysWhatToDo()
    {
        var (path, id) = Finalized("messages");

        var lines = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8), Caption: "a\nb"));

        Assert.Contains("caption is one line", lines.Refusal?.Message);
    }

    [Fact]
    public void ACaptionIsStoredWithThePictureAndListedWithIt()
    {
        var (path, id) = Finalized("caption");

        var captioned = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8), Caption: "  The chart  "));
        var plain = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8), Caption: "  "));
        var twoLines = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8), Caption: "one\ntwo"));
        var tooLong = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8),
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
        var picture = ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8))).Value!.PictureId;
        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);

        Assert.Equal("proposal.closed", ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8))).Refusal?.Code);
        Assert.Equal("proposal.closed", ProposalPictures.Remove(new RemovePictureRequest(path, Version, id, picture)).Refusal?.Code);
        Assert.Single(ProposalPictures.Show(new ShowPicturesRequest(path, Version, id)).Value!.Pictures);

        Assert.True(ProposalCommands.Reopen(new ReopenRequest(path, Version, "thaw", id)).Succeeded);
        Assert.True(ProposalPictures.Add(new AddPictureRequest(path, Version, id, Real(8, 8))).Succeeded);
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
