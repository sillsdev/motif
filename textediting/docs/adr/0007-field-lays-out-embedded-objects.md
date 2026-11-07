# The field lays out embedded objects itself in right-to-left text

FieldWorks puts pictures, links and other objects inline as one placeholder character. The field treats each as one
caret stop of fixed size and draws it with a renderer supplied by the host.

Avalonia 11.3 and 12.1 both place inline objects wrongly in right-to-left text. In those paragraphs the field lays
the object out as a fixed-width space and draws the object over it. This is a workaround for an Avalonia bug, the
same kind of thing FieldWorks does over its own text engine.

**Consequences:** a test named for the Avalonia fault pins it. When the test starts failing because Avalonia has
fixed the bug, delete the workaround and keep the test.

Owner decision, 2026-11-03.
