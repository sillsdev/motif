using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Sources;
using SIL.Motif.Scripture;

namespace SIL.Motif.Worker.Sources;

/// <summary>
/// What one kind of Source contributes to the shared index: how its Passages are named, where its material lives, and
/// which tokenizer and anchor policy versions its generation is bound to. Reads ask the provider; they never test a kind.
/// </summary>
internal interface ISourceProvider
{
    /// <summary>The kind stored in <c>SourceIndexState</c>.</summary>
    string Kind { get; }

    string TokenizerVersion { get; }

    string AnchorVersion { get; }

    /// <summary>Normalizes a caller's Passage reference to the stored Passage id; false when it cannot name one here.</summary>
    bool TryPassageId(string? reference, out string passageId);

    /// <summary>The folder this machine keeps the Source's material in, or null when the Source has none.</summary>
    string? Folder(string workspaceKey, string sourceId);

    /// <summary>Why the Source's material cannot be read on this machine, or null when it can.</summary>
    Refusal? Unavailable(string? folder);
}

/// <summary>Finds the provider for a stored Source kind.</summary>
internal static class SourceProviders
{
    public static ISourceProvider Paratext { get; } = new ParatextSourceProvider();
    public static ISourceProvider FieldWorksTexts { get; } = new FieldWorksTextsSourceProvider();

    /// <summary>The provider for a kind; null for a kind that is recorded but not indexed, such as a corpus.</summary>
    public static ISourceProvider? For(string kind) =>
        kind == Paratext.Kind ? Paratext : kind == FieldWorksTexts.Kind ? FieldWorksTexts : null;

    /// <summary>The versions a stored row of any kind is compared with; an unindexed kind is bound like Paratext.</summary>
    public static ISourceProvider Versions(string kind) => For(kind) ?? Paratext;
}

internal sealed class ParatextSourceProvider : ISourceProvider
{
    public string Kind => nameof(SourceKind.Paratext);
    public string TokenizerVersion => ParatextPassages.TokenizerVersion;
    public string AnchorVersion => ParatextPassages.AnchorVersion;

    public bool TryPassageId(string? reference, out string passageId)
    {
        passageId = "";
        if (!ChapterRef.TryParse(reference, out var chapter)) return false;
        passageId = chapter.ToString();
        return true;
    }

    public string? Folder(string workspaceKey, string sourceId) =>
        ScriptureLocalStore.ForInstallation().ReadExisting(workspaceKey).SourcePaths.GetValueOrDefault(sourceId);

    public Refusal? Unavailable(string? folder) =>
        folder is null || !Directory.Exists(folder) || !File.Exists(Path.Combine(folder, "Settings.xml"))
            ? new Refusal(RefusalCodes.SourceFolderMissing, FailureReason.NotFound, "The Source folder is unavailable on this machine.")
            : null;
}

/// <summary>The FieldWorks Texts of the saved project: one Passage per Text, named by its GUID, with no folder.</summary>
internal sealed class FieldWorksTextsSourceProvider : ISourceProvider
{
    public string Kind => FieldWorksTextsIndex.Kind;
    public string TokenizerVersion => FieldWorksTextsIndex.TokenizerVersion;
    public string AnchorVersion => FieldWorksTextsIndex.AnchorVersion;

    public bool TryPassageId(string? reference, out string passageId)
    {
        passageId = "";
        if (!Guid.TryParse(reference?.Trim(), out var id) || id == Guid.Empty) return false;
        passageId = id.ToString("D");
        return true;
    }

    public string? Folder(string workspaceKey, string sourceId) => null;
    public Refusal? Unavailable(string? folder) => null;
}
