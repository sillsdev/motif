using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Playground
{
    /// <summary>A stand-in for the host's dictionaries: a short word list for English and French, one checker each, and no dictionary
    /// for any other writing system. A host such as FieldWorks puts Hunspell behind <see cref="ISpellChecker"/> instead.</summary>
    public sealed class SampleSpelling : SpellCheckHostBase
    {
        static readonly string[] English =
        {
            "the", "quick", "brown", "fox", "jumped", "over", "lazy", "dog", "and", "cat", "sat", "don't", "panic", "well", "known", "words",
            "like", "are", "checked", "as", "dictionary", "decides", "a", "word", "in", "glued", "not", "locked", "wrong", "forced", "read",
            "only", "only", "here", "is", "this", "text", "with", "an", "object", "inside", "type", "something", "to", "see", "squiggles",
            "but", "blue", "more", "come", "go", "of", "french", "unknown", "jumper", "jumps", "ten", "than", "them", "then", "tea", "seat", "sit", "set", "mat",
        };

        static readonly string[] French = { "voici", "un", "exemple", "français", "mots", "le", "chat", "est", "sur", "tapis" };

        readonly Dictionary<string, SampleChecker> _checkers;
        readonly Action<string> _log;

        public SampleSpelling(Action<string>? log = null)
        {
            _log = log ?? (_ => { });
            _checkers = new Dictionary<string, SampleChecker>
            {
                ["en"] = new SampleChecker("en", English, _log, Changed),
                ["fr"] = new SampleChecker("fr", French, _log, Changed),
            };
        }

        void Changed() => RaiseDictionariesChanged();

        readonly Dictionary<string, BackgroundChecker> _background = new Dictionary<string, BackgroundChecker>();
        readonly List<int> _checkThreads = new List<int>();

        /// <summary>True: the checkers are <see cref="IBackgroundSpellChecker"/>s, so the field asks them on a worker thread (issue 103) and a slow dictionary
        /// does not hold up typing. False (the default): the plain checkers, asked on the UI thread. A host that changes it raises
        /// <see cref="SpellCheckHostBase.RaiseDictionariesChanged"/> so the fields ask again.</summary>
        public bool OffUiThread { get; set; }

        /// <summary>How long each word takes to look up when <see cref="OffUiThread"/> is on, in milliseconds: a stand-in for a dictionary that is slow.</summary>
        public int Delay { get; set; }

        /// <summary>The managed thread of each question the off-thread checkers were asked, so a person or a test can see they were not the UI thread's.</summary>
        public IReadOnlyList<int> CheckThreads { get { lock (_checkThreads) return _checkThreads.ToList(); } }

        public override ISpellChecker? GetChecker(string ws)
        {
            if (!_checkers.TryGetValue(ws, out var c)) return null;
            if (!OffUiThread) return c;
            if (!_background.TryGetValue(ws, out var b)) _background[ws] = b = new BackgroundChecker(c, this);
            return b;
        }

        /// <summary>The same dictionary, safe to ask from a worker thread: it takes a moment per word and notes the thread that asked.</summary>
        sealed class BackgroundChecker : IBackgroundSpellChecker, ISpellIgnorer
        {
            readonly SampleChecker _inner;
            readonly SampleSpelling _host;

            public BackgroundChecker(SampleChecker inner, SampleSpelling host)
            {
                _inner = inner;
                _host = host;
            }

            public bool Check(string word)
            {
                lock (_host._checkThreads) _host._checkThreads.Add(Environment.CurrentManagedThreadId);
                if (_host.Delay > 0) System.Threading.Thread.Sleep(_host.Delay);
                return _inner.Check(word);
            }

            public IReadOnlyList<string> Suggest(string word) => _inner.Suggest(word);
            public void Add(string word) => _inner.Add(word);
            public void Ignore(string word) => _inner.Ignore(word);
        }

        /// <summary>What a person can see of the dictionaries: the words added or ignored.</summary>
        public IEnumerable<string> Added => _checkers.Values.SelectMany(c => c.Added);

        sealed class SampleChecker : ISpellChecker, ISpellIgnorer
        {
            readonly string _ws;
            readonly HashSet<string> _words;
            readonly Action<string> _log;
            readonly Action _changed;

            public SampleChecker(string ws, IEnumerable<string> words, Action<string> log, Action changed)
            {
                _changed = changed;
                _ws = ws;
                _words = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
                _log = log;
            }

            public List<string> Added { get; } = new List<string>();

            // The word list is read on a worker thread when the checkers are off-thread ones, so it is guarded.
            public bool Check(string word)
            {
                lock (_words) return _words.Contains(word);
            }

            // The words within two edits, the nearest first: enough to show a menu of several suggestions.
            public IReadOnlyList<string> Suggest(string word)
            {
                string[] words;
                lock (_words) words = _words.ToArray();
                return words.Select(w => (Word: w, Distance: Distance(w.ToLowerInvariant(), word.ToLowerInvariant()))).Where(x => x.Distance <= 2).OrderBy(x => x.Distance).ThenBy(x => x.Word, StringComparer.Ordinal).Select(x => x.Word).ToList();
            }

            public void Add(string word)
            {
                lock (_words) _words.Add(word);
                Added.Add(word);
                _log("dictionary " + _ws + ": added '" + word + "' (the host records an undo)");
                _changed();
            }

            public void Ignore(string word)
            {
                lock (_words) _words.Add(word);
                _log("dictionary " + _ws + ": ignoring '" + word + "'");
                _changed();
            }

            static int Distance(string a, string b)
            {
                var d = new int[a.Length + 1, b.Length + 1];
                for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
                for (int j = 0; j <= b.Length; j++) d[0, j] = j;
                for (int i = 1; i <= a.Length; i++)
                    for (int j = 1; j <= b.Length; j++)
                        d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                return d[a.Length, b.Length];
            }
        }
    }
}
