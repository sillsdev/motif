using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SIL.Motif.App.Services.Fonts;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class FontPackageVerifierTests
{
    private static readonly byte[] FaceBytes = [1, 3, 5, 7];
    private static readonly byte[] LicenseBytes = Encoding.UTF8.GetBytes("Generic fixture license\n");
    private const string FacePath = "fonts/regular.ttf";
    private const string LicensePath = "fonts/license.txt";

    [Theory]
    [InlineData("Andika Compact", true)]
    [InlineData("andika compact", true)]
    [InlineData("Abyssinica SIL", true)]
    [InlineData("ABYSSINICA SIL", true)]
    [InlineData("Andika", false)]
    [InlineData("Andika Compact New", false)]
    [InlineData("Abyssinica", false)]
    [InlineData("Microsoft Sans Serif", false)]
    [InlineData("Charis SIL", false)]
    [InlineData("Doulos SIL", false)]
    [InlineData("Gentium Plus", false)]
    [InlineData("fonts:example#Andika Compact", false)]
    [InlineData("Andika Compact,Andika", false)]
    [InlineData("Andika Compact#Regular", false)]
    [InlineData(" Andika Compact", false)]
    [InlineData(null, false)]
    public void OnlyExactCatalogFamiliesMatch(string? family, bool expected)
    {
        Assert.Equal(expected, FontPackageCatalog.TryFind(family, out var descriptor));
        Assert.Equal(expected, descriptor is not null);
    }

    [Fact]
    public void SelectedBytesAreIndependentReadOnlyAndDoNotRequireOtherEntriesToOpen()
    {
        var archive = Create(new(FacePath, FaceBytes), new(LicensePath, LicenseBytes),
            new("docs/unused.bin", [9, 9]));
        SetCentral(archive, "docs/unused.bin", 10, 99, word: true);
        var descriptor = Describe(archive, 3);
        var result = FontPackageVerifier.Verify(descriptor, archive);
        Assert.True(result.Succeeded, result.Failure?.ToString());
        var package = Assert.IsType<VerifiedFontPackage>(result.Package);
        var face = Assert.Single(package.Faces);
        Assert.Equal(descriptor.Faces[0], face.Descriptor);
        Array.Fill(archive, (byte)0);
        using var first = face.File.OpenRead();
        Assert.False(first.CanWrite);
        Assert.Throws<NotSupportedException>(() => first.WriteByte(0));
        Assert.Equal(Hash(FaceBytes), Convert.ToHexString(SHA256.HashData(first)));
        using var second = face.File.OpenRead();
        Assert.Equal(0, second.Position);
        Assert.Equal(FaceBytes[0], second.ReadByte());
        using var license = package.License.OpenRead();
        Assert.Equal(Hash(LicenseBytes), Convert.ToHexString(SHA256.HashData(license)));
    }

    [Fact]
    public void ChangedArchiveIsRejectedBeforeZipParsingAndTruncationIsDistinct()
    {
        var archive = Create(new(FacePath, FaceBytes), new(LicensePath, LicenseBytes));
        var descriptor = Describe(archive, 2);
        var changed = archive.ToArray();
        changed[0] ^= 0xff;
        Fails(descriptor, changed, FontPackageFailure.ArchiveDigestMismatch);
        Fails(descriptor, archive[..^1], FontPackageFailure.ArchiveSizeMismatch);
        Fails(descriptor, new byte[4 * 1024 * 1024 + 1], FontPackageFailure.ArchiveTooLarge);
    }

    [Theory]
    [InlineData("fonts/regular.ttf")]
    [InlineData("FONTS/REGULAR.TTF")]
    public void DuplicateSelectedMemberCannotBePublished(string duplicate)
    {
        var archive = Create(new(FacePath, FaceBytes), new(LicensePath, LicenseBytes), new(duplicate, FaceBytes));
        Fails(Describe(archive, 3), archive, FontPackageFailure.DuplicateMember);
    }

    [Fact]
    public void MissingOrUnexpectedSelectedMemberCannotBeSubstituted()
    {
        var archive = Create(new("fonts/other.ttf", FaceBytes), new(LicensePath, LicenseBytes));
        Fails(Describe(archive, 2), archive, FontPackageFailure.MissingMember);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(unchecked((int)0xa1ff0000))]
    [InlineData(unchecked((int)0x41ed0000))]
    public void SelectedDirectorySymlinkOrSpecialEntryIsRejected(int attributes)
    {
        var archive = Create(new(FacePath, FaceBytes, attributes), new(LicensePath, LicenseBytes));
        Fails(Describe(archive, 2), archive, FontPackageFailure.UnsafeMember);
    }

    [Theory]
    [InlineData("../regular.ttf")]
    [InlineData("/regular.ttf")]
    [InlineData("fonts\\regular.ttf")]
    [InlineData("C:/regular.ttf")]
    [InlineData("fonts/./regular.ttf")]
    [InlineData("fonts//regular.ttf")]
    [InlineData("fonts/regular.ttf/")]
    public void UnsafeSelectedPathsAreNotAcceptedAsDescriptorTargets(string path)
    {
        var archive = Create(new(path, FaceBytes), new(LicensePath, LicenseBytes));
        Fails(Describe(archive, 2, new(path, FaceBytes.Length, Hash(FaceBytes))), archive,
            FontPackageFailure.InvalidDescriptor);
    }

    [Fact]
    public void EverySelectedFontAndTheLicenseNeedTheirExactSizeAndDigest()
    {
        var archive = Create(new(FacePath, FaceBytes), new(LicensePath, LicenseBytes));
        Fails(Describe(archive, 2, new(FacePath, FaceBytes.Length + 1, Hash(FaceBytes))), archive,
            FontPackageFailure.MemberSizeMismatch);
        Fails(Describe(archive, 2, new(FacePath, FaceBytes.Length, new string('0', 64))), archive,
            FontPackageFailure.MemberDigestMismatch);
        Fails(Describe(archive, 2, license: new(LicensePath, LicenseBytes.Length + 1, Hash(LicenseBytes))), archive,
            FontPackageFailure.MemberSizeMismatch);
        Fails(Describe(archive, 2, license: new(LicensePath, LicenseBytes.Length, new string('0', 64))), archive,
            FontPackageFailure.MemberDigestMismatch);
    }

    [Fact]
    public void EntryCountAndTotalDeclaredExpansionAreBoundedBeforeSelectedReads()
    {
        var entries = Enumerable.Range(0, 127).Select(index => new Entry($"docs/{index}.txt", [0]))
            .Prepend(new(LicensePath, LicenseBytes)).Prepend(new(FacePath, FaceBytes)).ToArray();
        var crowded = Create(entries);
        Fails(Describe(crowded, entries.Length), crowded, FontPackageFailure.TooManyEntries);
        var expanded = Create(new(FacePath, FaceBytes), new(LicensePath, LicenseBytes));
        SetCentral(expanded, FacePath, 24, 8 * 1024 * 1024 + 1);
        Fails(Describe(expanded, 2), expanded, FontPackageFailure.UncompressedLimitExceeded);
    }

    [Fact]
    public void AuthenticatedMalformedArchiveStillReturnsATypedFailure()
    {
        byte[] malformed = [1, 2, 3, 4];
        Fails(Describe(malformed, 2), malformed, FontPackageFailure.InvalidArchive);
    }

    private static void Fails(FontPackageDescriptor descriptor, byte[] archive, FontPackageFailure failure)
    {
        var result = FontPackageVerifier.Verify(descriptor, archive);
        Assert.False(result.Succeeded);
        Assert.Null(result.Package);
        Assert.Equal(failure, result.Failure);
    }

    private static FontPackageDescriptor Describe(byte[] archive, int count,
        FontPackageMember? face = null, FontPackageMember? license = null) =>
        new("generic-fixture", "Fixture", "1", new Uri("https://example.invalid/font.zip"),
            archive.Length, Hash(archive), count,
            [new(face ?? new(FacePath, FaceBytes.Length, Hash(FaceBytes)), "Fixture", "Fixture 1", "Regular", 400, false, 0)],
            license ?? new(LicensePath, LicenseBytes.Length, Hash(LicenseBytes)));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record Entry(string Name, byte[] Bytes, int Attributes = 0);

    private static byte[] Create(params Entry[] entries)
    {
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var item in entries)
            {
                var entry = zip.CreateEntry(item.Name);
                entry.ExternalAttributes = item.Attributes;
                using var output = entry.Open();
                output.Write(item.Bytes);
            }
        return result.ToArray();
    }

    private static void SetCentral(byte[] archive, string name, int fieldOffset, uint value, bool word = false)
    {
        for (var offset = 0; offset <= archive.Length - 46; offset++)
        {
            var header = archive.AsSpan(offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50) continue;
            var size = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            if (offset + 46 + size > archive.Length || Encoding.UTF8.GetString(header.Slice(46, size)) != name) continue;
            if (word) BinaryPrimitives.WriteUInt16LittleEndian(header[fieldOffset..], checked((ushort)value));
            else BinaryPrimitives.WriteUInt32LittleEndian(header[fieldOffset..], value);
            return;
        }
        throw new InvalidOperationException("The generated archive does not have the requested central entry.");
    }
}
