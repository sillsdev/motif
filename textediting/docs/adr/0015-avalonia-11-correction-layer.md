# Use Avalonia's caret and hit-testing, with a correction layer for 11.3 only

The field uses Avalonia's own caret positions, hit-testing and character navigation. On Avalonia 11.3 only, a small
correction layer fixes the known faults, such as right-to-left paragraph hit-testing. A test named after each
fault pins it. When FieldWorks moves to Avalonia 12 (ADR 0009), the layer and its tests are deleted.

**Considered options:**
- Porting Avalonia 12's fixes into the 11.3 build. Rejected because the fixes sit deep in Avalonia's text engine,
  19 of 29 patches conflicted, and we would carry a fork of its text layout.
- The field computing every caret position itself on both versions. Rejected because it duplicates what Avalonia
  12 already does correctly, and the code would outlive its reason.

Owner decision, 2026-11-03. Supersedes the earlier proposal that the field own visual caret movement.

Superseded in part by ADR 0027 (2026-11-04): Avalonia 12 has some of the same faults, so the layer is keyed on the
line's shape and applies on both versions.
