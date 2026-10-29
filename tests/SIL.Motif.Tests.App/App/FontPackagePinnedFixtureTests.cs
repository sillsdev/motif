using System.Security.Cryptography;
using SIL.Motif.App.Services.Fonts;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Optional on an unprovisioned developer machine; required mode never skips missing fixture evidence.</summary>
public sealed class FontPackageFixtureTheoryAttribute : TheoryAttribute
{
    public FontPackageFixtureTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_FONT_PACKAGE_FIXTURES_REQUIRED") != "1" &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MOTIF_FONT_PACKAGE_FIXTURES")))
            Skip = "Set MOTIF_FONT_PACKAGE_FIXTURES; acceptance also requires MOTIF_FONT_PACKAGE_FIXTURES_REQUIRED=1.";
    }
}

[Trait("MotifTestLevel", "System")]
public sealed class FontPackagePinnedFixtureTests
{
    [FontPackageFixtureTheory]
    [InlineData("Abyssinica SIL", "abyssinica-2.300.zip")]
    [InlineData("Andika Compact", "andika-compact-6.200.zip")]
    public void OfficialFixturesMatchEveryPinnedArchiveFontAndLicenseDigest(string family, string archiveName)
    {
        var root = Environment.GetEnvironmentVariable("MOTIF_FONT_PACKAGE_FIXTURES");
        Assert.False(string.IsNullOrWhiteSpace(root),
            "Pinned font fixtures are required; set MOTIF_FONT_PACKAGE_FIXTURES to their external read-only directory.");
        var path = Path.Combine(root!, archiveName);
        Assert.True(File.Exists(path), "The required original official font archive is absent.");
        var bytes = File.ReadAllBytes(path);
        Assert.True(FontPackageCatalog.TryFind(family, out var found));
        var descriptor = Assert.IsType<FontPackageDescriptor>(found);
        var result = FontPackageVerifier.Verify(descriptor, bytes);
        Assert.True(result.Succeeded, result.Failure?.ToString());
        var package = Assert.IsType<VerifiedFontPackage>(result.Package);
        Assert.Same(descriptor, package.Descriptor);
        Assert.Equal(72, descriptor.EntryCount);
        Assert.Equal(family, descriptor.Family);
        if (family == "Abyssinica SIL")
        {
            Assert.Equal("abyssinica-sil-2.300", descriptor.Id);
            Assert.Equal("2.300", descriptor.Version);
            Assert.Equal("https://software.sil.org/downloads/r/abyssinica/AbyssinicaSIL-2.300.zip", descriptor.Source.AbsoluteUri);
            Assert.Equal(1257280, descriptor.ArchiveBytes);
            Assert.Equal("725e6cd58be6495e0e53f367608db36d9f818498bccba739d742892c5a3d8151", descriptor.ArchiveSha256);
            Assert.Equal(descriptor.ArchiveBytes, bytes.Length);
            Assert.Equal(descriptor.ArchiveSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.Single(package.Faces);
            Face(package.Faces[0], "AbyssinicaSIL-2.300/AbyssinicaSIL-Regular.ttf", 268264,
                "f0e4fb92ee26967a3e6462342494956a3798b952345f917e1388913bb191cf2b",
                "Abyssinica SIL", "Version 2.300", "Regular", 400, false, 192);
            Member(package.License, "AbyssinicaSIL-2.300/OFL.txt", 4610,
                "a3668b2617181157065f100ec626a4ab8e350bf375df756b28406a575dbaf5cb");
        }
        else
        {
            Assert.Equal("andika-compact-6.200", descriptor.Id);
            Assert.Equal("6.200", descriptor.Version);
            Assert.Equal("https://typetunerweb.languagetechnology.org/ttw/fonts2go.cgi?family=Andika&pkg=Compact&ver=6.200", descriptor.Source.AbsoluteUri);
            Assert.Equal(2032664, descriptor.ArchiveBytes);
            Assert.Equal("1b29ec21568a246f53f35b571d9ee581a4a6794771f21ed84ba66da198ff2512", descriptor.ArchiveSha256);
            Assert.Equal(descriptor.ArchiveBytes, bytes.Length);
            Assert.Equal(descriptor.ArchiveSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.Equal(4, package.Faces.Count);
            Face(package.Faces[0], "AndikaCompact-6.200/AndikaCompact-BoldItalic.ttf", 773472,
                "cc08d6ef611a6241061d0efde9de7be041e6963eb9eb998aee831f6645641fa7",
                "Andika Compact", "Version 6.200 ; LnSpcTght", "Bold Italic", 700, true, 161);
            Face(package.Faces[1], "AndikaCompact-6.200/AndikaCompact-Regular.ttf", 726612,
                "be36f1547bcf5f50fe778f7a38060add040520fbc425f4ca939faa1545667843",
                "Andika Compact", "Version 6.200 ; LnSpcTght", "Regular", 400, false, 192);
            Face(package.Faces[2], "AndikaCompact-6.200/AndikaCompact-Italic.ttf", 761828,
                "c607c80289556d03ffbd04c1c5cfa1d7c4b7ad9f81500f1cb594ae74b28dbf69",
                "Andika Compact", "Version 6.200 ; LnSpcTght", "Italic", 400, true, 129);
            Face(package.Faces[3], "AndikaCompact-6.200/AndikaCompact-Bold.ttf", 736584,
                "78fc6edf60c329362a49e1e3497890a3b01cbae2244d0bd405dbda8e341ef826",
                "Andika Compact", "Version 6.200 ; LnSpcTght", "Bold", 700, false, 160);
            Member(package.License, "AndikaCompact-6.200/OFL.txt", 4412,
                "7d4d01dd13be30d67038af6c4783b9c186a72269d997b5c38d8b6e182d9c7d60");
        }
        using var license = package.License.OpenRead();
        using var text = new StreamReader(license);
        var original = text.ReadToEnd();
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", original, StringComparison.Ordinal);
        Assert.Contains("Reserved Font Name", original, StringComparison.Ordinal);
    }

    private static void Face(VerifiedFontFace face, string path, int size, string digest,
        string family, string version, string name, int weight, bool italic, int fsSelection)
    {
        Assert.Equal(family, face.Descriptor.Family);
        Assert.Equal(version, face.Descriptor.Version);
        Assert.Equal(name, face.Descriptor.Face);
        Assert.Equal(weight, face.Descriptor.Weight);
        Assert.Equal(italic, face.Descriptor.Italic);
        Assert.Equal(fsSelection, face.Descriptor.FsSelection);
        Member(face.File, path, size, digest);
    }

    private static void Member(VerifiedFontMember member, string path, int size, string digest)
    {
        Assert.Equal(path, member.Descriptor.Path);
        Assert.Equal(size, member.Descriptor.Bytes);
        Assert.Equal(digest, member.Descriptor.Sha256);
        using var bytes = member.OpenRead();
        Assert.Equal(size, bytes.Length);
        Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}
