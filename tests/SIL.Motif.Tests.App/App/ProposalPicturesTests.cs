using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalPicturesTests(AvaloniaHeadlessFixture avalonia)
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    // Pictures are drawn at run time from seeded noise, never read from real data; noise keeps a PNG large.
    private static byte[] Noise(int width, int height, bool jpeg)
    {
        byte[]? result = null;
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using (var frame = bitmap.Lock())
        {
            var random = new Random(7);
            var row = new byte[frame.RowBytes];
            for (var y = 0; y < height; y++)
            {
                random.NextBytes(row);
                for (var x = 3; x < row.Length; x += 4) row[x] = 255;
                System.Runtime.InteropServices.Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, row.Length);
            }
        }
        using var stream = new MemoryStream();
        if (jpeg) bitmap.Save(stream, new JpegBitmapEncoderOptions { Quality = 100 });
        else bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        result = stream.ToArray();
        return result;
    }

    [Fact]
    public void AFiveMegabytePhotoIsScaledToFitTheStoreCap()
    {
        avalonia.Invoke(() =>
        {
            var png = Noise(1500, 1000, jpeg: false);
            var jpeg = Noise(2400, 1800, jpeg: true);
            Assert.True(png.Length > 4_000_000, $"png {png.Length}");

            foreach (var source in new[] { png, jpeg })
            {
                var scaled = PictureScaler.Scale(source);

                Assert.Equal(PictureIntakeStatus.Ready, scaled.Status);
                Assert.True(scaled.Bytes!.Length <= ProposalPictures.MaxBytes, $"{scaled.Bytes.Length} bytes");
                var sniffed = PictureSniffer.Read(scaled.Bytes);
                Assert.NotNull(sniffed);
                Assert.True(Math.Max(sniffed.Value.Width, sniffed.Value.Height) <= PictureScaler.LongestSide);
            }
            Assert.Equal("image/jpeg", PictureSniffer.Read(PictureScaler.Scale(jpeg).Bytes!)!.Value.MediaType);
        });
    }

    [Fact]
    public void ASmallPictureIsKeptByteForByteAndAnythingUnreadableIsReportedNotThrown()
    {
        avalonia.Invoke(() =>
        {
            var small = Noise(40, 30, jpeg: false);
            Assert.Equal(small, PictureScaler.Scale(small).Bytes);

            Assert.Equal(PictureIntakeStatus.Unsupported, PictureScaler.Scale("not a picture, just words"u8.ToArray()).Status);
            // A PNG header that claims a large picture, followed by nothing a decoder can read.
            var corrupt = new byte[small.Length];
            Array.Copy(small, corrupt, 33);
            corrupt[18] = 0x20;
            Assert.Equal(PictureIntakeStatus.Unreadable, PictureScaler.Scale(corrupt).Status);
        });
    }

    // A PNG header that declares a huge size over almost no bytes, made at run time.
    private static byte[] DeclaringPng(uint width, uint height)
    {
        var png = Noise(40, 30, jpeg: false);
        foreach (var (at, value) in new[] { (16, width), (20, height) })
            for (var i = 0; i < 4; i++) png[at + i] = (byte)(value >> (24 - 8 * i));
        return png;
    }

    [Fact]
    public void APictureDeclaringMoreThanAHundredMegapixelsIsRefusedBeforeItIsDecoded()
    {
        avalonia.Invoke(() =>
        {
            var huge = DeclaringPng(15000, 15000);
            Assert.True(huge.Length < 10_000);

            Assert.Equal(PictureIntakeStatus.TooLarge, PictureScaler.Scale(huge).Status);
            // Over the sniffer's side limit too, and a JPEG that declares the same.
            Assert.Equal(PictureIntakeStatus.TooLarge, PictureScaler.Scale(DeclaringPng(60000, 60000)).Status);
            var jpeg = Noise(40, 30, jpeg: true);
            var frame = Array.IndexOf(jpeg, (byte)0xC0, 2);
            while (jpeg[frame - 1] != 0xFF) frame = Array.IndexOf(jpeg, (byte)0xC0, frame + 1);
            jpeg[frame + 4] = jpeg[frame + 6] = 0x3A; // 0x3Axx rows and columns: about 218 megapixels
            Assert.Equal(PictureIntakeStatus.TooLarge, PictureScaler.Scale(jpeg).Status);
        });
    }

    // Puts an EXIF segment holding only an orientation tag right after the JPEG's start marker.
    private static byte[] WithOrientation(byte[] jpeg, int orientation, bool littleEndian)
    {
        byte[] Pair(int v) => littleEndian ? [(byte)v, (byte)(v >> 8)] : [(byte)(v >> 8), (byte)v];
        var segment = new List<byte> { 0xFF, 0xE1, 0, 34 };
        segment.AddRange("Exif\0\0"u8.ToArray());
        segment.AddRange(littleEndian ? "II"u8.ToArray() : "MM"u8.ToArray());
        segment.AddRange(Pair(42));
        segment.AddRange(littleEndian ? new byte[] { 8, 0, 0, 0 } : new byte[] { 0, 0, 0, 8 });
        segment.AddRange(Pair(1));
        segment.AddRange(Pair(0x0112));
        segment.AddRange(Pair(3));
        segment.AddRange(littleEndian ? new byte[] { 1, 0, 0, 0 } : new byte[] { 0, 0, 0, 1 });
        segment.AddRange(Pair(orientation));
        segment.AddRange(new byte[] { 0, 0 });
        segment.AddRange(new byte[4]);
        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }

    // A wide picture, red on the left half and blue on the right, as a JPEG.
    private static byte[] RedAndBlue(int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using (var frame = bitmap.Lock())
        {
            var row = new byte[frame.RowBytes];
            for (var x = 0; x < width; x++)
            {
                row[x * 4 + (x < width / 2 ? 2 : 0)] = 255; // BGRA: red or blue
                row[x * 4 + 3] = 255;
            }
            for (var y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, row.Length);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, new JpegBitmapEncoderOptions { Quality = 95 });
        return stream.ToArray();
    }

    // Samples the turned picture: the red half was the left of the wide original, the blue half the right.
    private static (bool Tall, bool StartIsRed, bool EndIsRed) Turned(byte[] bytes)
    {
        using var result = new Bitmap(new MemoryStream(bytes));
        using var copy = new WriteableBitmap(result.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var frame = copy.Lock();
        result.CopyPixels(new PixelRect(result.PixelSize), frame.Address, frame.RowBytes * result.PixelSize.Height, frame.RowBytes);
        bool Red(int x, int y) => System.Runtime.InteropServices.Marshal.ReadByte(frame.Address, y * frame.RowBytes + x * 4 + 2) > 200;
        var (w, h) = (result.PixelSize.Width, result.PixelSize.Height);
        return h > w ? (true, Red(w / 2, 20), Red(w / 2, h - 20)) : (false, Red(20, h / 2), Red(w - 20, h / 2));
    }

    [Theory]
    [InlineData(6, true, true, true, false)]
    [InlineData(6, false, true, true, false)]
    [InlineData(3, true, false, false, true)]
    [InlineData(8, true, true, false, true)]
    public void AScaledJpegIsTurnedUprightByItsExifOrientation(int orientation, bool littleEndian, bool tall,
        bool startIsRed, bool endIsRed)
    {
        avalonia.Invoke(() =>
        {
            var photo = WithOrientation(RedAndBlue(2400, 1200), orientation, littleEndian);
            Assert.Equal(orientation, PictureScaler.ExifOrientation(photo));

            var scaled = PictureScaler.Scale(photo);

            Assert.Equal(PictureIntakeStatus.Ready, scaled.Status);
            Assert.Equal((tall, startIsRed, endIsRed), Turned(scaled.Bytes!));
            Assert.Equal(1, PictureScaler.ExifOrientation(scaled.Bytes!));
        });
    }

    [Fact]
    public void ASmallTaggedJpegIsReEncodedUprightWithTheTagDropped()
    {
        avalonia.Invoke(() =>
        {
            var small = WithOrientation(RedAndBlue(400, 200), 6, littleEndian: true);

            var scaled = PictureScaler.Scale(small);

            Assert.Equal(PictureIntakeStatus.Ready, scaled.Status);
            Assert.NotEqual(small, scaled.Bytes);
            Assert.Equal(1, PictureScaler.ExifOrientation(scaled.Bytes!));
            Assert.Equal((true, true, false), Turned(scaled.Bytes!));
        });
    }

    [Fact]
    public void AJpegWithoutAnOrientationTagKeepsItsShape()
    {
        avalonia.Invoke(() =>
        {
            var plain = RedAndBlue(2400, 1200);
            Assert.Equal(1, PictureScaler.ExifOrientation(plain));

            var size = PictureSniffer.Read(PictureScaler.Scale(plain).Bytes!)!.Value;

            Assert.True(size.Width > size.Height);
        });
    }

    [Fact]
    public async Task AddingAReadyPictureStoresItAndShowsItAndRemovingItClearsIt()
    {
        var fake = new FakeCommandClient();
        var model = Model(fake, new FakeIntake(new PictureIntakeResult(PictureIntakeStatus.Ready, ProposalPicturesTests_Png())));
        await model.LoadAsync(ProjectPath, "p/one", canEdit: true);
        Assert.True(model.CanAdd);
        Assert.True(model.IsEmpty);

        await model.AddCommand.ExecuteAsync(null);

        var item = Assert.Single(model.Items);
        Assert.Equal(ProposalPicturesTests_Png(), item.Bytes);
        Assert.Equal("40 × 30 pixels, 1 KB", item.Description);
        Assert.Equal("p/one", Assert.Single(fake.AddPictureRequests).ProposalId);
        Assert.False(model.IsEmpty);

        await model.RemoveCommand.ExecuteAsync(item);

        Assert.Single(model.Items);
        Assert.Empty(fake.RemovePictureRequests);
        Assert.True(item.IsConfirmingRemoval);

        model.CancelRemoveCommand.Execute(null);
        Assert.False(item.IsConfirmingRemoval);
        Assert.Single(model.Items);

        await model.RemoveCommand.ExecuteAsync(item);
        await model.LoadAsync(ProjectPath, "p/other", canEdit: true);
        await model.ConfirmRemoveCommand.ExecuteAsync(item);
        Assert.Empty(fake.RemovePictureRequests);

        await model.LoadAsync(ProjectPath, "p/one", canEdit: true);
        item = Assert.Single(model.Items);
        await model.RemoveCommand.ExecuteAsync(item);
        await model.ConfirmRemoveCommand.ExecuteAsync(item);

        Assert.Empty(model.Items);
        Assert.True(model.IsEmpty);
    }

    [Theory]
    [InlineData(PictureIntakeStatus.Unreadable)]
    [InlineData(PictureIntakeStatus.Unsupported)]
    [InlineData(PictureIntakeStatus.TooLarge)]
    public async Task APictureThatCannotBeUsedShowsAProblemAndStoresNothing(PictureIntakeStatus status)
    {
        var fake = new FakeCommandClient();
        var model = Model(fake, new FakeIntake(new PictureIntakeResult(status)));
        await model.LoadAsync(ProjectPath, "p/one", canEdit: true);

        await model.AddCommand.ExecuteAsync(null);

        Assert.True(model.HasProblem);
        Assert.Equal(status switch
        {
            PictureIntakeStatus.Unsupported => MotifAppStrings.PictureNotSupported,
            PictureIntakeStatus.TooLarge => MotifAppStrings.PictureTooBig,
            _ => MotifAppStrings.PictureNotRead,
        }, model.Problem);
        Assert.Empty(fake.AddPictureRequests);
        Assert.Empty(model.Items);
    }

    [Fact]
    public async Task CancellingThePickerChangesNothingAndADecidedProposalOffersNoEditing()
    {
        var fake = new FakeCommandClient();
        var model = Model(fake, new FakeIntake(new PictureIntakeResult(PictureIntakeStatus.Cancelled)));
        await model.LoadAsync(ProjectPath, "p/one", canEdit: true);
        await model.AddCommand.ExecuteAsync(null);
        Assert.False(model.HasProblem);
        Assert.Empty(fake.AddPictureRequests);

        await model.LoadAsync(ProjectPath, "p/decided", canEdit: false);

        Assert.False(model.CanAdd);
        Assert.False(model.CanRemove);
        Assert.False(model.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARefusedAddShowsTheStoresRefusalAndAWindowWithoutAPickerOffersNoAdd()
    {
        var fake = new FakeCommandClient { PictureWriteRefusal = "proposal.picture-invalid" };
        var model = Model(fake, new FakeIntake(new PictureIntakeResult(PictureIntakeStatus.Ready, ProposalPicturesTests_Png())));
        await model.LoadAsync(ProjectPath, "p/one", canEdit: true);

        await model.AddCommand.ExecuteAsync(null);

        Assert.Equal("proposal.picture-invalid", model.ShownRefusal?.Code);
        Assert.Empty(model.Items);
        Assert.False(Model(fake, null).CanAdd);
    }

    [Fact]
    public async Task SelectingAProposalReadsItsPicturesAndADecidedOneIsFrozen()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
        [
            Card("p/open", "proposed"),
            Card("p/closed", "withdrawn"),
        ], 0));
        await fake.AddProposalPictureAsync(new SIL.Motif.Commands.Requests.AddPictureRequest(
            ProjectPath, "v", "p/closed", ProposalPicturesTests_Png()), CancellationToken.None);
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.PicturesLoading;
        Assert.Equal("p/open", page.SelectedProposal!.ProposalId);
        Assert.True(page.Pictures.CanEdit);
        Assert.Empty(page.Pictures.Items);

        page.SelectedProposal = page.Rows.OfType<ProposalCardViewModel>().Single(card => card.ProposalId == "p/closed");
        await page.PicturesLoading;

        Assert.Single(page.Pictures.Items);
        Assert.False(page.Pictures.CanEdit);
    }

    private static ProposalCard Card(string id, string status) =>
        new(id, id, status, ProposalGroup.WaitingForYou, false, [], TrialStanding.NotTried, null, []);

    // A header-only PNG: the fake store and the sniffer read its type and size and decode nothing.
    private static byte[] ProposalPicturesTests_Png()
    {
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 0, 40, 0, 0, 0, 30, 8, 2, 0, 0, 0];
        return [.. header, .. new byte[600]];
    }

    private static ProposalPicturesViewModel Model(FakeCommandClient fake, IPictureIntake? intake) => new(
        (request, token) => fake.ShowProposalPicturesAsync(request, token),
        (request, token) => fake.AddProposalPictureAsync(request, token),
        (request, token) => fake.RemoveProposalPictureAsync(request, token), intake);

    private sealed class FakeIntake(PictureIntakeResult result) : IPictureIntake
    {
        public Task<PictureIntakeResult> ChooseAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
