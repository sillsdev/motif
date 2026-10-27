using System.Diagnostics.CodeAnalysis;

namespace SIL.Motif.App.Services.Fonts;

/// <summary>The exact package bytes and selected faces for a supplied family; no acquisition policy.</summary>
internal sealed class FontPackageDescriptor
{
    internal FontPackageDescriptor(string id, string family, string version, Uri source,
        int archiveBytes, string archiveSha256, int entryCount,
        IEnumerable<FontFaceDescriptor> faces, FontPackageMember license)
    {
        Id = id;
        Family = family;
        Version = version;
        Source = source;
        ArchiveBytes = archiveBytes;
        ArchiveSha256 = archiveSha256;
        EntryCount = entryCount;
        Faces = Array.AsReadOnly(faces.ToArray());
        License = license;
    }

    internal string Id { get; }
    internal string Family { get; }
    internal string Version { get; }
    internal Uri Source { get; }
    internal int ArchiveBytes { get; }
    internal string ArchiveSha256 { get; }
    internal int EntryCount { get; }
    internal IReadOnlyList<FontFaceDescriptor> Faces { get; }
    internal FontPackageMember License { get; }
}

internal sealed record FontPackageMember(string Path, int Bytes, string Sha256);

internal sealed record FontFaceDescriptor(FontPackageMember File, string Family, string Version,
    string Face, int Weight, bool Italic, int FsSelection);

/// <summary>Immutable original packages for exact requested families; no renderer aliases or URL search.</summary>
internal static class FontPackageCatalog
{
    private static readonly FontPackageDescriptor[] Packages =
    [
        new("abyssinica-sil-2.300", "Abyssinica SIL", "2.300",
            new Uri("https://software.sil.org/downloads/r/abyssinica/AbyssinicaSIL-2.300.zip"),
            1257280, "725e6cd58be6495e0e53f367608db36d9f818498bccba739d742892c5a3d8151", 72,
            [
                new(new("AbyssinicaSIL-2.300/AbyssinicaSIL-Regular.ttf", 268264,
                    "f0e4fb92ee26967a3e6462342494956a3798b952345f917e1388913bb191cf2b"),
                    "Abyssinica SIL", "Version 2.300", "Regular", 400, false, 192),
            ], new("AbyssinicaSIL-2.300/OFL.txt", 4610,
                "a3668b2617181157065f100ec626a4ab8e350bf375df756b28406a575dbaf5cb")),
        new("andika-compact-6.200", "Andika Compact", "6.200",
            new Uri("https://typetunerweb.languagetechnology.org/ttw/fonts2go.cgi?family=Andika&pkg=Compact&ver=6.200"),
            2032664, "1b29ec21568a246f53f35b571d9ee581a4a6794771f21ed84ba66da198ff2512", 72,
            [
                new(new("AndikaCompact-6.200/AndikaCompact-BoldItalic.ttf", 773472,
                    "cc08d6ef611a6241061d0efde9de7be041e6963eb9eb998aee831f6645641fa7"),
                    "Andika Compact", "Version 6.200 ; LnSpcTght", "Bold Italic", 700, true, 161),
                new(new("AndikaCompact-6.200/AndikaCompact-Regular.ttf", 726612,
                    "be36f1547bcf5f50fe778f7a38060add040520fbc425f4ca939faa1545667843"),
                    "Andika Compact", "Version 6.200 ; LnSpcTght", "Regular", 400, false, 192),
                new(new("AndikaCompact-6.200/AndikaCompact-Italic.ttf", 761828,
                    "c607c80289556d03ffbd04c1c5cfa1d7c4b7ad9f81500f1cb594ae74b28dbf69"),
                    "Andika Compact", "Version 6.200 ; LnSpcTght", "Italic", 400, true, 129),
                new(new("AndikaCompact-6.200/AndikaCompact-Bold.ttf", 736584,
                    "78fc6edf60c329362a49e1e3497890a3b01cbae2244d0bd405dbda8e341ef826"),
                    "Andika Compact", "Version 6.200 ; LnSpcTght", "Bold", 700, false, 160),
            ], new("AndikaCompact-6.200/OFL.txt", 4412,
                "7d4d01dd13be30d67038af6c4783b9c186a72269d997b5c38d8b6e182d9c7d60")),
    ];

    /// <summary>Finds only a complete canonical family, ignoring case and preserving the captured request.</summary>
    /// <param name="requestedFamily">The original family name, without renderer aliases or URI syntax.</param>
    /// <param name="descriptor">The pinned package when the complete family matches.</param>
    internal static bool TryFind(string? requestedFamily,
        [NotNullWhen(true)] out FontPackageDescriptor? descriptor)
    {
        descriptor = Packages.FirstOrDefault(package =>
            string.Equals(package.Family, requestedFamily, StringComparison.OrdinalIgnoreCase));
        return descriptor is not null;
    }
}
