using System;
using System.Collections.Generic;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>Turns the destination and attributes of a link or picture into the record of spec 4.1, whichever of the five spellings it was written in,
    /// and asks the host's <see cref="IProjectPathResolver"/> about the path and the GUID (spec 4.8).</summary>
    internal sealed class ObjectBuilder
    {
        readonly ReaderContext _context;
        readonly MarkdownOptions _options;
        readonly int _start, _end;

        public ObjectBuilder(ReaderContext context, int start, int end)
        {
            _context = context;
            _options = context.Options;
            _start = context.Base + start;
            _end = context.Base + end + 1;
        }

        void Report(string id, MarkdownSeverity severity, string message, string? replacement = null, IReadOnlyList<string>? choices = null, IReadOnlyList<string>? nearest = null)
            => _context.Report(id, severity, message, _start, _end, replacement, choices, nearest);

        public MarkdownObject? BuildPicture(string dest, Guid? guid, string? caption, string? urlAttr, bool urlGiven, string? tool)
        {
            var picture = new MarkdownObject { Kind = MarkdownObjectKind.Picture, Caption = caption, Guid = guid, Path = dest };
            var d = LinkSyntax.Classify(dest, _options);
            if (d.Kind == DestKind.RootPath && d.Guid != null)
            {
                if (guid != null && guid != d.Guid)
                {
                    Report(MarkdownDiagnosticIds.LinkGuidMismatch, MarkdownSeverity.Error, "The path names " + d.Guid + " and guid= names " + guid + ". Keep the one you mean.");
                    return null;
                }
                picture.Guid = d.Guid;
                picture.Path = null;
            }
            return picture;
        }

        public MarkdownObject? BuildLink(string dest, Guid? attrGuid, string? urlAttr, bool urlGiven, string? tool, bool name, bool example, bool broken)
        {
            var d = LinkSyntax.Classify(dest, _options);
            if (d.Kind == DestKind.Relative && d.NoBase)
            {
                Report(MarkdownDiagnosticIds.LinkRelativeWithoutBase, MarkdownSeverity.Error,
                    "'" + dest + "' is relative to the file, and there is no file location to resolve it against. Write it from the root of the project, such as /lists/GDR/GDR-46.");
                return null;
            }
            if ((d.Kind == DestKind.Silfw || d.Kind == DestKind.Motif) && d.Guid == null && d.Path == null)
            {
                Report(MarkdownDiagnosticIds.LinkNotFound, MarkdownSeverity.Error, "'" + dest + "' names no object: it has neither a GUID nor a path.");
                return null;
            }
            if (d.GuidConflict)
            {
                Report(MarkdownDiagnosticIds.LinkGuidMismatch, MarkdownSeverity.Error, "The path names one GUID and ?guid= another. Keep the one you mean.");
                return null;
            }
            if (d.Kind == DestKind.External && attrGuid != null)
            {
                Report(MarkdownDiagnosticIds.LinkGuidMismatch, MarkdownSeverity.Error, "'" + dest + "' is an external address, and guid= names an object of the project. Write the path of the object, or drop guid=.");
                return null;
            }
            bool internalLink = attrGuid != null || name || example || broken || d.Kind == DestKind.Silfw || d.Kind == DestKind.Motif
                || (d.Kind == DestKind.RootPath && d.Path != null && d.Path.HasKnownTopFolder && d.Path.TopFolder != "files")
                || (d.Kind == DestKind.Relative && d.Path != null);
            if (!internalLink)
                return new MarkdownObject { Kind = MarkdownObjectKind.ExternalLink, Url = urlGiven ? urlAttr : dest };

            if (urlGiven && d.Kind != DestKind.Silfw && !(SilfwUrl.TryParse(urlAttr ?? string.Empty, out var givenUrl) && givenUrl.Guid != null))
            {
                Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, "url= of a link to an object of the project is the silfw:// URL FieldWorks stores for it. Drop it, or make the link an external one.");
                return null;
            }
            var link = new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, FollowsName = name, Broken = broken, Example = example, Tool = tool, Project = d.Project };
            bool aliasOrBackLink = d.Kind == DestKind.Silfw || d.Kind == DestKind.Motif || (d.Kind == DestKind.RootPath && d.Guid != null);
            if (attrGuid != null && d.Guid != null && attrGuid != d.Guid && aliasOrBackLink)
            {
                Report(MarkdownDiagnosticIds.LinkGuidMismatch, MarkdownSeverity.Error, "The link names " + d.Guid + " and guid= names " + attrGuid + ". Keep the one you mean.");
                return null;
            }
            link.Guid = attrGuid ?? d.Guid;
            ProjectPath? path = d.Path != null && d.Path.ObjectGuid != null ? null : d.Path;
            if (d.Kind == DestKind.Silfw)
            {
                link.Url = dest;
                link.Tool = d.Silfw!.Tool;
                string tag = d.Silfw.Tag ?? string.Empty;
                if (tag.Length > 0 && !IsAllDigits(tag))
                {
                    int dot = tag.IndexOf('.');
                    link.Field = dot < 0 ? tag : tag.Substring(0, dot);
                    if (dot >= 0) link.FieldWritingSystem = tag.Substring(dot + 1);
                }
            }
            else
            {
                link.Url = urlGiven ? urlAttr : null;
                if (d.Path != null)
                {
                    link.Field = d.Path.Field;
                    link.FieldWritingSystem = d.Path.FieldWritingSystem;
                    link.Path = d.Path.ToString();
                }
                // A stored URL that names another GUID than the link does is out of date: rebuild it (spec 4.5).
                if (link.Url != null && attrGuid != null && SilfwUrl.TryParse(link.Url, out var stored) && stored.Guid != null && stored.Guid != attrGuid)
                {
                    link.Url = null;
                    Report(MarkdownDiagnosticIds.LinkUrlRebuilt, MarkdownSeverity.Suggestion, "guid= names another object than url= does, so the stored URL was rebuilt from the GUID.");
                }
            }
            // A stored URL that names another project makes this a cross-project link too, however it is spelled here.
            if (link.Project == null && link.Url != null && SilfwUrl.TryParse(link.Url, out var storedUrl) && storedUrl.Database != null
                && storedUrl.Database != SilfwUrl.CurrentProject && storedUrl.Database != _options.SilfwDatabase) link.Project = storedUrl.Database;
            if (broken) Report(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "This link's target was deleted. The text and the last path are kept.");

            if (link.Project != null)
            {
                if (link.Url == null)
                {
                    if (link.Guid == null)
                    {
                        Report(MarkdownDiagnosticIds.LinkNotFound, MarkdownSeverity.Error, "A path in another project (" + link.Project + ") needs guid=: this project cannot find it.");
                        return null;
                    }
                    // The project is kept as the stored URL names it; the core never turns it into this one.
                    link.Url = SilfwUrl.Build(link.Guid.Value, link.Tool ?? (path != null ? PathTools.ToolOf(path) : null), link.Field == null ? null : link.FieldWritingSystem == null ? link.Field : link.Field + "." + link.FieldWritingSystem, link.Project);
                }
                return link; // a link into another project: no path here, never rebound, never broken
            }

            var paths = _options.Paths;
            if (paths == null)
            {
                if (link.Guid == null)
                {
                    Report(MarkdownDiagnosticIds.LinkNotFound, MarkdownSeverity.Error, "'" + dest + "' is a path, and no project is available to find the object it names. Add guid=.");
                    return null;
                }
                if (link.Tool == null && path != null) link.Tool = PathTools.ToolOf(path);
                return link;
            }
            if (link.Guid == null)
            {
                var found = paths.Resolve(path!, out var candidates);
                if (found == null)
                {
                    var names = new List<string>();
                    foreach (var g in candidates) names.Add(Describe(paths, g));
                    if (candidates.Count > 1)
                        Report(MarkdownDiagnosticIds.LinkAmbiguous, MarkdownSeverity.Error, "'" + dest + "' matches " + candidates.Count + " objects. Choose one.", null, names);
                    else
                        Report(MarkdownDiagnosticIds.LinkNotFound, MarkdownSeverity.Error, "'" + dest + "' matches no object of this project.", null, null, names.Count > 0 ? names : null);
                    return null;
                }
                link.Guid = found;
            }
            else
            {
                var current = paths.PathOf(link.Guid.Value);
                if (current == null)
                {
                    Guid? rebound = null;
                    if (_options.UnknownGuids == UnknownGuidPolicy.RebindByPath && path != null) rebound = paths.Resolve(path, out _);
                    if (rebound != null)
                    {
                        link.Guid = rebound;
                        if (link.Url != null) link.Url = null;
                        Report(MarkdownDiagnosticIds.LinkRebound, MarkdownSeverity.Suggestion, "The GUID is not in this project; the link was bound to the one object at '" + dest + "'.");
                    }
                    else if (!broken)
                    {
                        link.Broken = true;
                        Report(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "The GUID " + link.Guid + " is not an object of this project, so the link is marked broken. The text is kept.");
                    }
                }
                else if (path != null && !SamePlace(current, path))
                {
                    Report(MarkdownDiagnosticIds.LinkStalePath, MarkdownSeverity.Suggestion, "The GUID resolves, but the object is now at '" + new ProjectPath(current.Segments) + "'. The next export writes the new path.");
                }
            }
            if (link.Tool == null && link.Guid != null) link.Tool = paths.ToolOf(link.Guid.Value) ?? (path != null ? PathTools.ToolOf(path) : null);
            return link;
        }

        static bool SamePlace(ProjectPath a, ProjectPath b) => new ProjectPath(a.Segments).Equals(new ProjectPath(b.Segments));

        static string Describe(IProjectPathResolver paths, Guid guid)
        {
            var p = paths.PathOf(guid);
            return (p == null ? ProjectPath.ForObject(guid).ToString() : new ProjectPath(p.Segments).ToString()) + " (" + guid + ")";
        }

        static bool IsAllDigits(string s) { foreach (char c in s) if (c < '0' || c > '9') return false; return s.Length > 0; }
    }

    /// <summary>The FLEx tool that edits what a path names, from the first segments of the path (spec 4.2). A host that knows better says so through
    /// <see cref="IProjectPathResolver.ToolOf"/>.</summary>
    internal static class PathTools
    {
        public static string? ToolOf(ProjectPath path)
        {
            var s = path.Segments;
            if (s.Count == 0) return null;
            switch (s[0].Name)
            {
                case "lexicon": return "lexiconEdit";
                case "texts": return "interlinearEdit";
                case "notebook": return "notebookEdit";
                case "reversals": return s.Count >= 3 && s[2].Name == "categories" ? "reversalToolReversalIndexPOS" : "reversalToolEditComplete";
                case "grammar":
                    if (s.Count < 2) return null;
                    switch (s[1].Name)
                    {
                        case "categories": case "affix-templates": case "affix-slots": case "inflection-classes": case "stem-names": return "posEdit";
                        case "phonological-rules": return "PhonologicalRuleEdit";
                        case "compound-rules": return "compoundRuleAdvancedEdit";
                        case "adhoc-rules": return "AdhocCoprohibEdit";
                        case "exception-features": return "ProdRestrictEdit";
                        case "inflection-features": return "featuresAdvancedEdit";
                        case "phonological-features": return "phonologicalFeaturesAdvancedEdit";
                        case "phonemes": return "phonemeEdit";
                        case "natural-classes": return "naturalClassedit";
                        case "environments": return "EnvironmentEdit";
                    }
                    return null;
            }
            return null;
        }
    }
}
