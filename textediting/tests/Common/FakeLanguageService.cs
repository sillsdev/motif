using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SIL.TextEditing;

namespace SIL.TextEditing.Tests
{
    /// <summary>A language service a test controls: what it answers is a function of the text, it records what it was asked, and with <see cref="Manual"/> set each
    /// analysis waits for the test to complete it, so a test can change the text while the service is "working".</summary>
    public sealed class FakeLanguageService : ITextLanguageService
    {
        public Func<string, LanguageAnalysis> Analysis = t => LanguageAnalysis.Empty;
        public Func<string, LanguagePosition, CompletionTriggerKind, IReadOnlyList<CompletionItem>> Completions = (t, p, k) => new CompletionItem[0];
        public Func<string, LanguagePosition, LanguageHover?> HoverOf = (t, p) => null;
        public Func<string, LanguageRange, string, bool> CanInsertOf = (t, r, k) => false;
        public char[] Triggers = new char[0];

        /// <summary>When true an analysis or completion is not answered until its source is completed (see <see cref="PendingAnalyses"/>, <see cref="PendingCompletions"/>).</summary>
        public bool Manual;
        public Exception? Throws;
        public bool ThrowsSynchronously;
        /// <summary>When true an analysis is answered with a cancelled task, as a service that gives up does.</summary>
        public bool CancelsItself;
        public readonly List<CancellationToken> CompletionTokens = new List<CancellationToken>();
        public readonly List<CancellationToken> HoverTokens = new List<CancellationToken>();

        public readonly List<string> AnalyzedTexts = new List<string>();
        public readonly List<CompletionTriggerKind> CompletionKinds = new List<CompletionTriggerKind>();
        public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
        public readonly List<(string Text, TaskCompletionSource<LanguageAnalysis> Source)> PendingAnalyses = new List<(string, TaskCompletionSource<LanguageAnalysis>)>();
        public readonly List<(string Text, TaskCompletionSource<IReadOnlyList<CompletionItem>> Source)> PendingCompletions = new List<(string, TaskCompletionSource<IReadOnlyList<CompletionItem>>)>();

        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

        public IReadOnlyList<char> CompletionTriggerCharacters => Triggers;

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public Task<LanguageAnalysis> AnalyzeAsync(string text, CancellationToken cancellation)
        {
            AnalyzedTexts.Add(text);
            Tokens.Add(cancellation);
            if (CancelsItself) return Task.FromCanceled<LanguageAnalysis>(new CancellationToken(true));
            if (ThrowsSynchronously && Throws != null) throw Throws;
            if (Throws != null) return Task.FromException<LanguageAnalysis>(Throws);
            if (Manual)
            {
                var source = new TaskCompletionSource<LanguageAnalysis>();
                PendingAnalyses.Add((text, source));
                return source.Task;
            }
            return Task.FromResult(Analysis(text));
        }

        public Task<IReadOnlyList<CompletionItem>> CompleteAsync(string text, LanguagePosition position, CompletionTriggerKind trigger, CancellationToken cancellation)
        {
            CompletionKinds.Add(trigger);
            CompletionTokens.Add(cancellation);
            if (Manual)
            {
                var source = new TaskCompletionSource<IReadOnlyList<CompletionItem>>();
                PendingCompletions.Add((text, source));
                return source.Task;
            }
            return Task.FromResult(Completions(text, position, trigger));
        }

        public Task<LanguageHover?> HoverAsync(string text, LanguagePosition position, CancellationToken cancellation)
        {
            HoverTokens.Add(cancellation);
            if (HoverThrows != null) return Task.FromException<LanguageHover?>(HoverThrows);
            return Task.FromResult(HoverOf(text, position));
        }

        public Exception? HoverThrows;

        public bool CanInsert(string text, LanguageRange selection, string kind) => CanInsertOf(text, selection, kind);

        /// <summary>The offsets of every occurrence of <paramref name="word"/> as a token of the given kind, and a diagnostic of that severity where a word is "bad".</summary>
        public static Func<string, LanguageAnalysis> Words(string kind, string bad = "bad", DiagnosticSeverity severity = DiagnosticSeverity.Error, string? fixTo = null)
        {
            return text =>
            {
                var tokens = new List<LanguageToken>();
                var diagnostics = new List<LanguageDiagnostic>();
                int i = 0;
                while (i < text.Length)
                {
                    if (text[i] == ' ') { i++; continue; }
                    int s = i;
                    while (i < text.Length && text[i] != ' ') i++;
                    var range = new LanguageRange(s, i);
                    string word = text.Substring(s, i - s);
                    tokens.Add(new LanguageToken(range, word == bad ? "unknown" : kind));
                    if (word == bad)
                        diagnostics.Add(new LanguageDiagnostic(range, severity, "'" + word + "' is bad", "test.bad", "fake",
                            fixTo == null ? null : new LanguageFix("Change to " + fixTo, new[] { new LanguageTextEdit(range, fixTo) })));
                }
                return new LanguageAnalysis(tokens, diagnostics);
            };
        }
    }
}
