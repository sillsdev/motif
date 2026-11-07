using System;

namespace SIL.TextEditing.Avalonia
{
    // A host that changes the document itself, not through the editor (issue 101): the field hears of every change to a paragraph it shows
    // (Paragraph.ChangeStamp moves), lays that paragraph out again at the next measure and checks it again, and tells automation when the
    // editor's read-only flag changes however it was set. What it cannot hear is a change to the list of paragraphs or to the styles: RefreshFromDocument.
    public partial class RichTextField
    {
        /// <summary>What a paragraph tells: it holds a weak link to the field, so a paragraph (and the document, which a host keeps for longer than a
        /// field) does not keep a field that was dropped alive.</summary>
        sealed class ParagraphWatcher
        {
            readonly WeakReference<RichTextField> _field;
            public readonly Action<Paragraph> Handler;

            public ParagraphWatcher(RichTextField field)
            {
                _field = new WeakReference<RichTextField>(field);
                Handler = Notify;
            }

            void Notify(Paragraph paragraph)
            {
                if (_field.TryGetTarget(out var field)) field.OnParagraphTouched();
                else paragraph.Unwatch(Handler);
            }
        }

        ParagraphWatcher? _watcher;
        bool _hostEditNoticed;

        /// <summary>Set by any change to a paragraph the field shows (on any thread): the next question about a paragraph first compares every
        /// paragraph's stamp, so a change to an earlier paragraph (its bullet scheme moves the numbers after it) is seen by a direct
        /// <see cref="ParagraphLayoutFor"/>, not only by the measure that visits the paragraphs in order.</summary>
        volatile bool _stampsStale;

        /// <summary>Counts the changes to the shown paragraphs, so what was worked out from the text (the find highlight) can tell it is out of date.</summary>
        int _docVersion;

        void SweepStamps()
        {
            _stampsStale = false;   // a change made while the sweep runs sets it again
            for (int i = 0; i < _info.Count; i++) Current(i);
        }

        ParagraphWatcher Watcher => _watcher ??= new ParagraphWatcher(this);

        void Watch(ParaInfo info, Paragraph paragraph)
        {
            if (ReferenceEquals(info.Watched, paragraph)) return;
            Unwatch(info);
            info.Watched = paragraph;
            paragraph.Watch(Watcher.Handler);
        }

        void Unwatch(ParaInfo info)
        {
            info.Watched?.Unwatch(Watcher.Handler);
            info.Watched = null;
        }

        void WatchAll()
        {
            var paragraphs = Editor.Document.Paragraphs;
            for (int i = 0; i < _info.Count && i < paragraphs.Count; i++) Watch(_info[i], paragraphs[i]);
        }

        void UnwatchAll()
        {
            foreach (var info in _info) Unwatch(info);
        }

        /// <summary>A paragraph the field shows changed. It may be the editor's own edit (which the field is told of anyway) or a host's, on any
        /// thread: the field's own thread then measures and draws again, and checks the spelling again, so the screen follows the model. Many changes in a
        /// row, a paragraph of thousands of runs built, cost one flag each until the next measure.</summary>
        void OnParagraphTouched()
        {
            // Every change counts, even those the guard below lets pass without a second invalidation: the stamps are swept again before the next
            // layout question, and what the find highlight was worked out from is dropped when it is next used (issue 117).
            _stampsStale = true;
            System.Threading.Interlocked.Increment(ref _docVersion);
            if (_hostEditNoticed) return;
            _hostEditNoticed = true;
            if (CheckAccess()) NoticeHostEdit();
            else OwnDispatcher.Post(NoticeHostEdit, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void NoticeHostEdit()
        {
            FindDocumentChanged();   // the highlight and the current match were worked out from the old text
            InvalidateMeasure();
            InvalidateVisual();
            RequestSpellingIdle();
            ScheduleLanguageIfWanted();   // the language service's answer was for the old text (LiveLanguage knows by the stamp)
        }

        bool _reportedReadOnly;

        /// <summary>The editor's read-only flag changed, by the field's <see cref="IsReadOnly"/> or by the editor directly, on whatever thread: on the
        /// field's own thread, an input method stops composing, automation hears of it, and spell checking starts over (read-only text is not checked).</summary>
        void OnEditorReadOnlyChanged()
        {
            if (CheckAccess()) ReadOnlyChanged();
            else OwnDispatcher.Post(ReadOnlyChanged, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void ReadOnlyChanged()
        {
            bool now = Editor.IsReadOnly;
            if (now && _preedit != null) SetPreedit(null, null);
            if (now) CloseCompletion();
            if (now != _reportedReadOnly)
            {
                _reportedReadOnly = now;
                _peer?.RaiseReadOnlyChanged(!now, now);
            }
            RestartSpelling();
        }
    }
}
