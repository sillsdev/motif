# ADR 0055: Editing shortcuts act on focused text or staged changes

Motif's standard editing keys work in text fields and on words, while undo and redo take back changes staged in the current window session. The window uses its existing staging commands for those reversals, so Apply remains the only action that writes the reviewed changes to FieldWorks.

## Decision

The window supports the platform's primary modifier for Copy, Undo and Redo: Ctrl on Windows and Linux, and ⌘ on macOS. Redo also accepts Ctrl+Shift+Z or ⌘⇧Z. Text controls keep their native editing behavior for cut, copy, paste, undo and redo. Window handlers must leave those key events available to the focused text control. Paste outside a text control has no window action.

Outside text controls, Copy places the selected copyable text on the clipboard when there is a selection; otherwise, when a word is focused, it places that word's exact form on the clipboard as plain text. It has no effect when neither applies.

Outside text controls, Undo and Redo operate on an in-memory history owned by the open window. The history records each successful staging action, including opinion changes, added analyses, individual Undo and Undo all. Undo applies the inverse through the same staging command paths used by the visible controls; Redo reapplies the recorded action through those paths. These shortcuts never write to the Motif store directly.

A new staging action clears Redo. Apply, Refresh, project close and project switch clear both histories. Each history entry is valid only while the pending changes still match the state it recorded. If another Motif window or the CLI changes or removes one of those changes, Undo or Redo is refused with a short message in the window's status area. The message names the change in the window's words.

## Consequences

The Help shortcut catalog and Keyboard shortcuts screen list Copy, Undo and Redo for the window, with platform-specific gestures. Text controls continue to display and use their native editing commands; the global history applies only outside text entry.

Shortcut dispatch tests cover platform modifiers, focused text, selected text, and the four word lists. Staging history tests cover each recorded action, redo invalidation, Apply and Refresh clearing, external changes, and command-path reversals.
