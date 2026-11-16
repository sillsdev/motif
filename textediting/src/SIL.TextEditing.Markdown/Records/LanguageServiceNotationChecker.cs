using System;
using System.Collections.Generic;
using System.Threading;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>Checks record notations with the same language services used for editing their text.
    /// Services are selected by an ordinal notation name; unregistered notations have no diagnostics.</summary>
    /// <remarks>Every AnalyzeAsync call must return an already completed task, such as the default implementation on
    /// <see cref="TextLanguageService"/>. Completion is checked for the actual text on every call; no probe text is sent
    /// at construction. Pending tasks are refused without waiting. Service failures and cancellation propagate to the caller.
    /// The record field path does not change the text or diagnostic ranges supplied by the service.</remarks>
    public sealed class LanguageServiceNotationChecker : INotationChecker
    {
        readonly Dictionary<string, ITextLanguageService> _services;

        /// <summary>Copies the mapping from notation names to services. A service must answer synchronously on every check.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
        /// <exception cref="ArgumentException">A name is empty or duplicated, or a service is null.</exception>
        public LanguageServiceNotationChecker(IEnumerable<KeyValuePair<string, ITextLanguageService>> services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            _services = new Dictionary<string, ITextLanguageService>(StringComparer.Ordinal);
            foreach (var pair in services)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                    throw new ArgumentException("Each notation has a name and a language service.", nameof(services));
                _services.Add(pair.Key, pair.Value);
            }
        }

        /// <summary>Returns the selected service's diagnostics unchanged, or an empty list for an unregistered notation.</summary>
        /// <exception cref="ArgumentException">The service returns a pending task instead of answering synchronously.</exception>
        public IReadOnlyList<LanguageDiagnostic> Check(string notation, string text, string path)
        {
            if (!_services.TryGetValue(notation, out var service)) return Array.Empty<LanguageDiagnostic>();
            var analysis = service.AnalyzeAsync(text, CancellationToken.None);
            if (!analysis.IsCompleted)
                throw new ArgumentException("The language service for " + notation + " must answer synchronously.", nameof(notation));
            return analysis.GetAwaiter().GetResult().Diagnostics;
        }
    }
}
