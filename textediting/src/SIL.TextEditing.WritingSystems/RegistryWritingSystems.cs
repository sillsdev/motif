using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Reader;
using SIL.WritingSystems;

namespace SIL.TextEditing.WritingSystems
{
    /// <summary>
    /// Checks tags against a snapshot of the project's inventory, without consulting SLDR or validating BCP 47.
    /// Tags are compared ordinally and kept in caller order, with duplicates removed. Defaults do not add known tags.
    /// Construct a new instance after the inventory changes. The caller retains ownership of the repository.
    /// </summary>
    public sealed class RegistryWritingSystems : IWritingSystemMapper, IKnownWritingSystems, IWritingSystemChecker
    {
        readonly HashSet<string> _tags;

        /// <summary>Snapshots the repository's writing-system ids. No definitions are changed or saved.</summary>
        public RegistryWritingSystems(IWritingSystemRepository repository, string defaultAnalysis, string defaultVernacular)
            : this((repository ?? throw new ArgumentNullException(nameof(repository))).AllWritingSystems.Select(ws => ws.Id),
                defaultAnalysis, defaultVernacular)
        {
        }

        /// <summary>Snapshots the supplied tags. Null or empty tags are rejected; tags are never normalized.</summary>
        public RegistryWritingSystems(IEnumerable<string> tags, string defaultAnalysis, string defaultVernacular)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            DefaultAnalysis = defaultAnalysis ?? throw new ArgumentNullException(nameof(defaultAnalysis));
            DefaultVernacular = defaultVernacular ?? throw new ArgumentNullException(nameof(defaultVernacular));
            _tags = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag)) throw new ArgumentException("A writing-system tag cannot be null or empty.", nameof(tags));
                if (_tags.Add(tag)) ordered.Add(tag);
            }
            KnownTags = ordered.AsReadOnly();
        }

        /// <inheritdoc />
        public string DefaultAnalysis { get; }

        /// <inheritdoc />
        public string DefaultVernacular { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> KnownTags { get; }

        /// <inheritdoc />
        public bool IsKnown(string tag) => _tags.Contains(tag);

        /// <inheritdoc />
        public MarkdownProblem? CheckWritingSystem(string tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (IsKnown(tag)) return null;
            var nearest = ReaderContext.NearestNames(tag, KnownTags) ?? Array.Empty<string>();
            return new MarkdownProblem(MarkdownDiagnosticIds.WsUnknown, MarkdownSeverity.Error,
                "'" + tag + "' is not a writing system of this project."
                + (nearest.Count == 0 ? string.Empty : " Nearest: " + string.Join(", ", nearest) + "."))
            {
                Choices = KnownTags,
                Nearest = nearest
            };
        }
    }
}
