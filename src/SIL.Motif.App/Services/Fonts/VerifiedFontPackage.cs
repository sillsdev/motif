namespace SIL.Motif.App.Services.Fonts;

internal enum FontPackageFailure
{
    InvalidDescriptor,
    ArchiveTooLarge,
    ArchiveSizeMismatch,
    ArchiveDigestMismatch,
    InvalidArchive,
    TooManyEntries,
    UncompressedLimitExceeded,
    UnexpectedEntryCount,
    MissingMember,
    DuplicateMember,
    UnsafeMember,
    MemberSizeMismatch,
    MemberDigestMismatch,
}

internal sealed record FontPackageVerification(VerifiedFontPackage? Package, FontPackageFailure? Failure)
{
    internal bool Succeeded => Package is not null && Failure is null;
}

/// <summary>Owns immutable selected bytes matching the supplied descriptor, without a live typeface.</summary>
internal sealed class VerifiedFontMember
{
    private readonly byte[] _bytes;

    internal VerifiedFontMember(FontPackageMember descriptor, byte[] bytes)
    {
        Descriptor = descriptor;
        _bytes = bytes.ToArray();
    }

    internal FontPackageMember Descriptor { get; }

    /// <summary>Opens an independent read-only position over bytes that cannot expose their backing array.</summary>
    internal Stream OpenRead() => new MemoryStream(_bytes, writable: false);
}

internal sealed record VerifiedFontFace(FontFaceDescriptor Descriptor, VerifiedFontMember File);

/// <summary>Selected original faces and complete license bytes, verified against one immutable descriptor.</summary>
internal sealed class VerifiedFontPackage
{
    internal VerifiedFontPackage(FontPackageDescriptor descriptor, IEnumerable<VerifiedFontFace> faces,
        VerifiedFontMember license)
    {
        Descriptor = descriptor;
        Faces = Array.AsReadOnly(faces.ToArray());
        License = license;
    }

    internal FontPackageDescriptor Descriptor { get; }
    internal IReadOnlyList<VerifiedFontFace> Faces { get; }
    internal VerifiedFontMember License { get; }
}
