using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SIL.Motif.App.Services.Fonts;

/// <summary>Checks bounded original ZIP bytes and reads only descriptor-selected files, without extraction.</summary>
internal static class FontPackageVerifier
{
    internal const int MaximumArchiveBytes = 4 * 1024 * 1024;
    internal const long MaximumExpandedBytes = 8 * 1024 * 1024;
    internal const int MaximumEntries = 128;

    /// <summary>Verifies all archive and selected-member pins before returning independent selected bytes.</summary>
    /// <param name="descriptor">Immutable expected bytes and metadata; catalogue selection remains explicit.</param>
    /// <param name="archive">The complete original package; its input buffer is never retained.</param>
    internal static FontPackageVerification Verify(FontPackageDescriptor descriptor, ReadOnlyMemory<byte> archive)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!ValidDescriptor(descriptor)) return Failed(FontPackageFailure.InvalidDescriptor);
        if (archive.Length > MaximumArchiveBytes) return Failed(FontPackageFailure.ArchiveTooLarge);
        if (archive.Length != descriptor.ArchiveBytes) return Failed(FontPackageFailure.ArchiveSizeMismatch);
        var snapshot = archive.ToArray();
        if (!Matches(snapshot, descriptor.ArchiveSha256)) return Failed(FontPackageFailure.ArchiveDigestMismatch);
        if (DirectoryFailure(snapshot) is { } directoryFailure) return Failed(directoryFailure);
        try
        {
            using var stream = new MemoryStream(snapshot, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaximumEntries) return Failed(FontPackageFailure.TooManyEntries);
            if (zip.Entries.Count != descriptor.EntryCount) return Failed(FontPackageFailure.UnexpectedEntryCount);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > MaximumExpandedBytes - total)
                    return Failed(FontPackageFailure.UncompressedLimitExceeded);
                total += entry.Length;
            }
            var selected = new Dictionary<string, VerifiedFontMember>(StringComparer.Ordinal);
            foreach (var member in descriptor.Faces.Select(face => face.File).Append(descriptor.License))
            {
                var matches = zip.Entries.Where(entry =>
                    string.Equals(entry.FullName, member.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 1) return Failed(FontPackageFailure.DuplicateMember);
                if (matches.Length == 0 || matches[0].FullName != member.Path)
                    return Failed(FontPackageFailure.MissingMember);
                var entry = matches[0];
                var unixKind = (entry.ExternalAttributes >> 16) & 0xf000;
                if (entry.FullName.EndsWith('/') || (entry.ExternalAttributes & 0x10) != 0 ||
                    unixKind is not (0 or 0x8000))
                    return Failed(FontPackageFailure.UnsafeMember);
                if (entry.Length != member.Bytes) return Failed(FontPackageFailure.MemberSizeMismatch);
                using var input = entry.Open();
                var bytes = new byte[member.Bytes];
                input.ReadExactly(bytes);
                if (input.ReadByte() != -1) return Failed(FontPackageFailure.MemberSizeMismatch);
                if (!Matches(bytes, member.Sha256)) return Failed(FontPackageFailure.MemberDigestMismatch);
                selected.Add(member.Path, new VerifiedFontMember(member, bytes));
            }
            return new(new VerifiedFontPackage(descriptor,
                descriptor.Faces.Select(face => new VerifiedFontFace(face, selected[face.File.Path])),
                selected[descriptor.License.Path]), null);
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException or NotSupportedException)
        {
            return Failed(FontPackageFailure.InvalidArchive);
        }
    }

    private static bool ValidDescriptor(FontPackageDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id) || string.IsNullOrWhiteSpace(descriptor.Family) ||
            !descriptor.Source.IsAbsoluteUri || descriptor.Source.Scheme != Uri.UriSchemeHttps || descriptor.ArchiveBytes <= 0 ||
            descriptor.ArchiveBytes > MaximumArchiveBytes || !Digest(descriptor.ArchiveSha256) ||
            descriptor.EntryCount <= 0 || descriptor.Faces.Count is < 1 or > 4) return false;
        var members = descriptor.Faces.Select(face => face.File).Append(descriptor.License).ToArray();
        return members.All(member => SafePath(member.Path) && member.Bytes > 0 &&
                member.Bytes <= MaximumExpandedBytes && Digest(member.Sha256)) &&
            members.Select(member => member.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == members.Length;
    }

    private static bool SafePath(string path) => !string.IsNullOrEmpty(path) &&
        !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl) &&
        path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    private static bool Digest(string text) => text.Length == 64 && text.All(char.IsAsciiHexDigit);

    private static bool Matches(byte[] bytes, string expected) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expected, StringComparison.OrdinalIgnoreCase);

    private static FontPackageVerification Failed(FontPackageFailure failure) => new(null, failure);

    // Validate the bounded directory before ZipArchive materializes any entry objects.
    private static FontPackageFailure? DirectoryFailure(byte[] archive)
    {
        var bytes = archive.AsSpan();
        for (var offset = bytes.Length - 22; offset >= Math.Max(0, bytes.Length - 65557); offset--)
        {
            var end = bytes[offset..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(end) != 0x06054b50 ||
                offset + 22 + BinaryPrimitives.ReadUInt16LittleEndian(end[20..]) != bytes.Length) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(end[4..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(end[6..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(end[8..]) != BinaryPrimitives.ReadUInt16LittleEndian(end[10..]))
                return FontPackageFailure.InvalidArchive;
            if (BinaryPrimitives.ReadUInt16LittleEndian(end[10..]) > MaximumEntries)
                return FontPackageFailure.TooManyEntries;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(end[12..]);
            var start = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
            return (long)start + size == offset ? null : FontPackageFailure.InvalidArchive;
        }
        return FontPackageFailure.InvalidArchive;
    }
}
