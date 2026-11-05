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
