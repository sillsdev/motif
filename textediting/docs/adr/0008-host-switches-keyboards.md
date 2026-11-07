# The field announces writing-system changes; the host switches keyboards

Text arrives only through Avalonia's input method client, so system IMEs and Keyman behave the same as in any other
Avalonia control. When the caret moves into a run of another writing system, the field raises an event. The host
then switches the OS keyboard or Keyman, as FieldWorks does now. The field never touches keyboards itself.

**Consequences:** the field stays free of Windows, Keyman and libpalaso keyboarding code, and the same field works
under FieldWorks and Motif.

Owner decision, 2026-11-03.
