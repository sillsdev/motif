namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A project already exists at the chosen name and folder.</summary>
    public const string ProjectCreateExists = "project-create.exists";
    /// <summary>LibLCM could not create the project; what Motif wrote was removed.</summary>
    public const string ProjectCreateFailed = "project-create.failed";
    /// <summary>The parent folder for a new project does not exist.</summary>
    public const string ProjectCreateFolderNotFound = "project-create.folder-not-found";
    /// <summary>The parent folder for a new project is not a usable path.</summary>
    public const string ProjectCreateInvalidFolder = "project-create.invalid-folder";
    /// <summary>A new project's name is not allowed.</summary>
    public const string ProjectCreateInvalidName = "project-create.invalid-name";
    /// <summary>A word-forming character is not a single visible character.</summary>
    public const string ProjectCreateInvalidWordForming = "project-create.invalid-word-forming";
    /// <summary>A new project's writing system tag is not a valid BCP 47 tag.</summary>
    public const string ProjectCreateInvalidWritingSystem = "project-create.invalid-writing-system";
}
