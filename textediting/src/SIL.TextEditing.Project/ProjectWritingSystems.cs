using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.WritingSystems;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;

namespace SIL.TextEditing.Project
{
    /// <summary>
    /// Reads a project's writing systems for Markdown and ITsString conversion. The caller owns the cache and must
    /// serialize calls on its owning thread while it is alive. This adapter never changes or disposes the cache.
    /// Lists and descriptions are detached results; subsequent calls read the current project.
    /// </summary>
    public sealed class ProjectWritingSystems : IWritingSystemMapper, IWritingSystemDetails,
        IKnownWritingSystems, IWritingSystemHandles
    {
        readonly LcmCache _cache;
        readonly IWritingSystemHandles _handles;

        public ProjectWritingSystems(LcmCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _handles = WritingSystemHandles.FromFactory(cache.WritingSystemFactory);
        }

        /// <inheritdoc />
        public string DefaultAnalysis => _handles.GetId(_cache.DefaultAnalWs);

        /// <inheritdoc />
        public string DefaultVernacular => _handles.GetId(_cache.DefaultVernWs);

        /// <inheritdoc />
        public IReadOnlyList<string> KnownTags => ProjectSystems().Select(ws => ws.Id).Distinct(StringComparer.Ordinal).ToArray();

        IEnumerable<CoreWritingSystemDefinition> ProjectSystems() =>
            _cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Concat(
                _cache.ServiceLocator.WritingSystems.VernacularWritingSystems);

        /// <inheritdoc />
        public bool IsKnown(string tag) => ProjectSystems().Any(ws => string.Equals(ws.Id, tag, StringComparison.Ordinal));

        /// <inheritdoc />
        public WritingSystemInfo? DescribeWritingSystem(string id)
        {
            var ws = ProjectSystems().FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.Ordinal));
            return ws == null ? null : new WritingSystemInfo(ws.LanguageTag, ws.RightToLeftScript ? "rtl" : "ltr", ws.DefaultFontName);
        }

        /// <summary>Maps an id known to the cache's factory, including systems outside the analysis/vernacular choices.</summary>
        public int GetHandle(string wsId) => _handles.GetHandle(wsId);

        /// <inheritdoc />
        public string GetId(int handle) => _handles.GetId(handle);
    }
}
