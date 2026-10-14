using System.Text.RegularExpressions;

namespace SIL.Motif.Projection;

/// <summary>
/// A project could not be opened because one of its writing systems could not be loaded, or because its folder lacks
/// the <c>WritingSystemStore</c> a FieldWorks project keeps beside its <c>.fwdata</c>. This is a configuration
/// problem with a remedy the message states, never damage to Motif's stored data.
/// </summary>
public sealed class ProjectWritingSystemsUnavailableException : Exception
{
    /// <summary>The environment variable that selects the shared writing-system repository.</summary>
    public const string RepositorySetting = "MOTIF_WRITING_SYSTEM_REPOSITORY_PATH";

    private static readonly Regex LibLcmMissing =
        new(@"^The writing system (?<tag>.+) was not found in this manager\.?$", RegexOptions.CultureInvariant);

    private ProjectWritingSystemsUnavailableException(string message, string? writingSystem, string projectStore,
        string? repository, Exception? inner)
        : base(message, inner)
    {
        WritingSystem = writingSystem;
        ProjectStore = projectStore;
        Repository = repository;
    }

    /// <summary>The tag that could not be loaded, or <see langword="null"/> when the whole store is missing.</summary>
    public string? WritingSystem { get; }

    /// <summary>The project's own <c>WritingSystemStore</c> folder.</summary>
    public string ProjectStore { get; }

    /// <summary>The shared writing-system repository Motif searched, when known.</summary>
    public string? Repository { get; }

    /// <summary>
    /// Recognises LibLCM's "not found in this manager" failure from opening <paramref name="fwDataPath"/> and
    /// returns the explained exception, or <see langword="null"/> for any other failure.
    /// </summary>
    public static ProjectWritingSystemsUnavailableException? FromLoadFailure(
        KeyNotFoundException failure, string fwDataPath, string? repository)
    {
        var match = LibLcmMissing.Match(failure.Message);
        return match.Success ? Missing(match.Groups["tag"].Value, fwDataPath, repository, failure) : null;
    }

    /// <summary>
    /// The same failure explained for <paramref name="fwDataPath"/>, the project the caller named, when the open that
    /// failed was of a copy Motif made of it.
    /// </summary>
    public ProjectWritingSystemsUnavailableException ForProject(string fwDataPath) => WritingSystem is { } tag
        ? Missing(tag, fwDataPath, Repository, InnerException)
        : MissingStore(fwDataPath);

    private static ProjectWritingSystemsUnavailableException Missing(string tag, string fwDataPath, string? repository,
        Exception? failure)
    {
        var store = StoreOf(fwDataPath);
        var configured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RepositorySetting));
        var where = repository is null ? "the shared writing-system repository"
            : configured ? $"the shared writing-system repository at {repository}, which {RepositorySetting} selects"
            : $"the shared writing-system repository at {repository}";
        var remedy = configured
            ? $"Unset {RepositorySetting} so Motif uses the repository FieldWorks keeps, or copy the project's " +
              $"{store}{Path.DirectorySeparatorChar}*.ldml files into {repository ?? "that folder"}."
            : $"Open the project once in FieldWorks so it writes its writing systems, or copy the project's " +
              $"{store}{Path.DirectorySeparatorChar}*.ldml files into {repository ?? "that repository"}.";
        return new ProjectWritingSystemsUnavailableException(
            $"This project uses the writing system '{tag}', which could not be loaded from the project's " +
            $"WritingSystemStore ({store}) or from {where}. {remedy}", tag, store, repository, failure);
    }

    /// <summary>Explains a project folder that has no <c>WritingSystemStore</c> with any <c>.ldml</c> file in it.</summary>
    public static ProjectWritingSystemsUnavailableException MissingStore(string fwDataPath)
    {
        var store = StoreOf(fwDataPath);
        return new ProjectWritingSystemsUnavailableException(
            $"The project folder has no WritingSystemStore with .ldml files ({store}). A FieldWorks project is the " +
            $"whole folder: copy its WritingSystemStore folder along with {Path.GetFileName(fwDataPath)}, or open the " +
            "project once in FieldWorks to write it.", null, store, null, null);
    }

    private static string StoreOf(string fwDataPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fwDataPath))!, "WritingSystemStore");
}
