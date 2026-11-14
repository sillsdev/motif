using SIL.LCModel;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Project;

namespace SIL.Motif.Host.RichText;

/// <summary>
/// The FieldWorks Markdown options Motif reads and writes project text with, wherever the text comes from: a project copy
/// open in a cache, or what a command returned about one. Motif's choices are made here once, so every reader and writer
/// of FieldWorks Markdown in Motif agrees: no front matter, Motif's table styles (<see cref="MotifMarkdownStyles"/>), and
/// a link to an object the project does not have is marked broken rather than refused. Only a path resolver can tell which objects the project has, so options made without one
/// take a link's GUID as written and never mark it broken.
/// </summary>
public static class MotifMarkdownOptions
{
    /// <summary>
    /// Options over <paramref name="writingSystems"/>, which name the writing system of unmarked text and say which
    /// tags exist; <paramref name="styles"/>, which say which style names exist (null accepts every name);
    /// <paramref name="paths"/>, which relate project paths and objects (null keeps paths as written and writes a link
    /// whose path is unknown as <c>/objects/&lt;guid&gt;</c>); and <paramref name="projectName"/>, the project a link
    /// names.
    /// </summary>
    public static MarkdownOptions Create(IWritingSystemMapper writingSystems, IStyleCatalog? styles = null,
        IProjectPathResolver? paths = null, string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(writingSystems);
        return new MarkdownOptions
        {
            WriteFrontMatter = false,
            UnknownGuids = UnknownGuidPolicy.MarkBroken,
            StyleNames = MotifMarkdownStyles.Names(),
            WritingSystems = writingSystems,
            Styles = styles,
            Paths = paths,
            ProjectName = projectName,
        };
    }

    /// <summary>Options over the project open in <paramref name="cache"/>, whose name is <paramref name="projectName"/>.</summary>
    public static MarkdownOptions For(LcmCache cache, string? projectName)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return Create(new ProjectWritingSystems(cache), new ProjectStyles(cache), new ProjectPathResolver(cache), projectName);
    }
}
