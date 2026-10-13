using SIL.LCModel;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// A file-backed <see cref="IProjectIdentifier"/> for creating a <c>.fwdata</c> project, needed because LibLCM's
/// own implementations are <c>internal</c> and the one public one drags NUnit and Moq in with it.
/// </summary>
/// <remarks>
/// <see cref="ProjectFolder"/> is deliberately real, unlike <see cref="MemoryOnlyProjectId"/>'s: creating a project
/// writes its writing systems into the project's own <c>WritingSystemStore</c> only when the folder is non-empty.
/// </remarks>
public sealed class FileProjectId : IProjectIdentifier
{
    /// <param name="fwDataPath">The <c>.fwdata</c> file, inside a folder of the same name.</param>
    public FileProjectId(string fwDataPath)
    {
        Path = System.IO.Path.GetFullPath(fwDataPath);
        Name = System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string UiName => Name;

    /// <inheritdoc />
    public string Path { get; set; }

    /// <inheritdoc />
    public string Handle => Path;

    /// <inheritdoc />
    public string PipeHandle => Path;

    /// <inheritdoc />
    public string ProjectFolder => System.IO.Path.GetDirectoryName(Path)!;

    /// <inheritdoc />
    public BackendProviderType Type => BackendProviderType.kXML;
}
