using System;

namespace SIL.TextEditing
{
    // Undo grouping (ADR 0023). The editor still reports one EditDelta per change, so the host's data stays current after
    // every keystroke (ADR 0003), but each delta now says which undo step it belongs to. Consecutive typing is one step;
    // everything else is a step of its own. The host turns a step into one undo task.
    public sealed partial class DocumentEditor
    {
        long _lastGroup;
        bool _groupOpen, _groupTyping, _typing, _batch;
        string? _groupWs;
        string _typingWs = string.Empty;
        DateTime _lastTyped;

        /// <summary>How long a pause in typing ends the undo step (ADR 0023: about one second). A pause is noticed when the
        /// next character arrives; the field also closes the step when the pause passes.</summary>
        public TimeSpan TypingPause { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>The clock the pause is measured with; tests replace it.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>True while an undo step is still taking more typing.</summary>
        public bool HasOpenUndoGroup => _groupOpen;

        /// <summary>Raised when an undo step is complete, with its <see cref="EditDelta.UndoGroup"/>. Every step starts with a
        /// delta whose <see cref="EditDelta.StartsUndoGroup"/> is true and ends with this event; the host opens an undo task
        /// at the first and closes it here. A typing step ends here when the caret moves, the writing system changes, a
        /// delete or any other command follows, the pause passes, or the host applies a change. A host must not rely on that last case to close a step: it ends its
        /// undo task here, so call <see cref="CloseUndoGroup"/> before undoing or redoing (the field does so before it raises
        /// its undo and redo requests), or the event fires in the middle of the undo.</summary>
        public event Action<long>? UndoGroupEnded;

        /// <summary>Ends the open undo step, if any: the next edit starts a new one. A host calls this before a command it
        /// runs itself (a format, say); the field calls it on focus loss and when the pause passes.</summary>
        public void CloseUndoGroup()
        {
            if (!_groupOpen) return;
            _groupOpen = false;
            _groupTyping = false;
            UndoGroupEnded?.Invoke(_lastGroup);
        }

        void AssignUndoGroup(EditDelta d)
        {
            var now = Clock();
            bool join = _batch ? _groupOpen : _groupOpen && _typing && _groupTyping && _groupWs == _typingWs && now - _lastTyped <= TypingPause;
            if (!join)
            {
                CloseUndoGroup();
                _lastGroup++;
                _groupOpen = true;
                _groupTyping = _typing;
                _groupWs = _typingWs;
                d.StartsUndoGroup = true;
            }
            d.UndoGroup = _lastGroup;
            if (_typing) _lastTyped = now;
        }

        void EndSingleEditGroup()
        {
            if (!_typing && !_batch) CloseUndoGroup();
        }

        /// <summary>Runs an insertion that is one undo step however many edits it makes: it ends any open step first, every
        /// edit inside joins the one new step, and the step is closed again at the end.</summary>
        void InOneUndoStep(Action insert)
        {
            CloseUndoGroup();
            _batch = true;
            try { insert(); }
            finally
            {
                _batch = false;
                CloseUndoGroup();
            }
        }
    }
}
