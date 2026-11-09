using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.TextEditing.Markdown;

namespace SIL.TextEditing.Project
{
    /// <summary>What a project object is, in a few words, for a host that shows a link's target.</summary>
    public sealed class ProjectObjectDescription
    {
        public ProjectObjectDescription(string kind, string name, string description)
        {
            Kind = kind;
            Name = name;
            Description = description;
        }

        /// <summary>The kind of object in a person's words, such as "Lexical entry" or "List item".</summary>
        public string Kind { get; }

        /// <summary>The object's current name.</summary>
        public string Name { get; }

        /// <summary>A short description of the object; empty when it has none.</summary>
        public string Description { get; }
    }

    /// <summary>Resolves project paths against an open project: lexical entries (<c>/lexicon/palit(2)</c>), possibility lists and their items
    /// (<c>/lists/Grammar Design Records/GDR-1</c>) and any object by GUID (<c>/objects/&lt;guid&gt;</c>). An object with no path of its own
    /// has the <c>/objects</c> path, so a link to it is never taken for a missing one. It only reads.</summary>
    public sealed class ProjectPathResolver : IProjectPathResolver
    {
        const string Lexicon = "lexicon", Lists = "lists";
        readonly LcmCache _cache;

        public ProjectPathResolver(LcmCache cache) => _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        public Guid? Resolve(ProjectPath path, out IReadOnlyList<Guid> candidates)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            candidates = Array.Empty<Guid>();
            if (path.ObjectGuid is Guid known) return Exists(known) ? known : (Guid?)null;
            var matches = Matches(path);
            if (matches.Count == 1) return matches[0];
            candidates = matches;
            return null;
        }

        public ProjectPath? PathOf(Guid guid)
        {
            if (!_cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var item)) return null;
            switch (item)
            {
                case ILexEntry entry:
                    var name = EntryName(entry);
                    return name.Length == 0 ? ProjectPath.ForObject(guid)
                        : new ProjectPath(new[] { new ProjectPathSegment(Lexicon), new ProjectPathSegment(name, entry.HomographNumber > 0 ? entry.HomographNumber : (int?)null) });
                case ICmPossibilityList list:
                    return Text(list.Name).Length == 0 ? ProjectPath.ForObject(guid) : new ProjectPath(Lists, Text(list.Name));
                case ICmPossibility possibility:
                    var segments = new List<ProjectPathSegment>();
                    ICmObject? at = possibility;
                    for (; at is ICmPossibility step; at = step.Owner)
                    {
                        var stepName = Text(step.Name);
                        if (stepName.Length == 0) return ProjectPath.ForObject(guid);
                        segments.Insert(0, new ProjectPathSegment(stepName));
                    }
                    if (!(at is ICmPossibilityList owner) || Text(owner.Name).Length == 0) return ProjectPath.ForObject(guid);
                    segments.Insert(0, new ProjectPathSegment(Text(owner.Name)));
                    segments.Insert(0, new ProjectPathSegment(Lists));
                    return new ProjectPath(segments);
                default:
                    return ProjectPath.ForObject(guid);
            }
        }

        public string? ToolOf(Guid guid) =>
            _cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var item) && item is ILexEntry ? "lexiconEdit" : null;

        /// <summary>What the object is, or null when the project has no such object.</summary>
        public ProjectObjectDescription? Describe(Guid guid)
        {
            if (!_cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var item)) return null;
            switch (item)
            {
                case ILexEntry entry:
                    var sense = entry.SensesOS.FirstOrDefault();
                    return new ProjectObjectDescription("Lexical entry", EntryName(entry), sense == null ? string.Empty : Text(sense.Gloss));
                case ICmPossibilityList list:
                    return new ProjectObjectDescription("List", Text(list.Name), Text(list.Description));
                case ICmPossibility possibility:
                    return new ProjectObjectDescription("List item", Text(possibility.Name), Text(possibility.Description));
                default:
                    return new ProjectObjectDescription(item.ClassName, item.ShortName ?? string.Empty, string.Empty);
            }
        }

        bool Exists(Guid guid) => _cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out _);

        List<Guid> Matches(ProjectPath path)
        {
            var found = new List<Guid>();
            var segments = path.Segments;
            if (segments.Count == 2 && segments[0].Name == Lexicon)
            {
                foreach (var entry in _cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
                    if (Fits(segments[1], EntryName(entry), entry.HomographNumber > 0 ? entry.HomographNumber : (int?)null, entry.Guid)) found.Add(entry.Guid);
            }
            else if (segments.Count >= 2 && segments[0].Name == Lists)
            {
                foreach (var list in _cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances())
                {
                    if (!Fits(segments[1], Text(list.Name), null, list.Guid)) continue;
                    var level = new List<ICmPossibility>(list.PossibilitiesOS);
                    if (segments.Count == 2) found.Add(list.Guid);
                    for (int i = 2; i < segments.Count && level.Count > 0; i++)
                    {
                        var step = segments[i];
                        var hits = level.Where(item => Fits(step, Text(item.Name), null, item.Guid)).ToList();
                        if (i == segments.Count - 1) found.AddRange(hits.Select(item => item.Guid));
                        level = hits.SelectMany(item => item.SubPossibilitiesOS).ToList();
                    }
                }
            }
            return found;
        }

        static bool Fits(ProjectPathSegment segment, string name, int? homograph, Guid guid) =>
            new ProjectPathSegment(segment.Name, segment.Homograph).Equals(new ProjectPathSegment(name, homograph))
            && (segment.GuidSuffix == null || guid.ToString("N").StartsWith(segment.GuidSuffix, StringComparison.OrdinalIgnoreCase));

        string EntryName(ILexEntry entry)
        {
            var form = Text(entry.CitationForm);
            return form.Length > 0 ? form : entry.LexemeFormOA == null ? string.Empty : Text(entry.LexemeFormOA.Form);
        }

        string Text(IMultiAccessorBase text)
        {
            var order = new[] { _cache.DefaultVernWs, _cache.DefaultAnalWs }.Concat(text.AvailableWritingSystemIds);
            foreach (var ws in order)
            {
                var value = text.get_String(ws).Text;
                if (!string.IsNullOrEmpty(value)) return value!;
            }
            return string.Empty;
        }
    }
}
