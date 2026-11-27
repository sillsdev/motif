using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

public sealed partial class DeletionSafetyTests
{
    [Fact]
    public async Task ImportRefusesResourcePathsThatDifferOnlyByCaseAndNamesBoth()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        SeededProject.Seed(cache);
        new FwDataProjectLoader().Save(cache);
        var pictures = Path.Combine(Path.GetDirectoryName(NewLangProjFixture.FwDataPath(root))!, "Pictures");
        Directory.CreateDirectory(pictures);
        File.WriteAllBytes(Path.Combine(pictures, "Photo.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(pictures, "photo.jpg"), [4, 5, 6]);
        if (Directory.EnumerateFiles(pictures).Count() != 2) return; // a case-insensitive file system cannot hold the pair
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            LcmRecordBridge.ImportAsync(cache, NewLangProjFixture.FwDataPath(root), Database));
        Assert.Contains("Pictures/Photo.jpg", failure.Message);
        Assert.Contains("Pictures/photo.jpg", failure.Message);
        Assert.False(File.Exists(Database));
    }
}
