# The caret correction layer applies on both Avalonia versions, keyed on the line's shape

The field uses Avalonia's caret, hit testing and character navigation where they are correct. On lines where Avalonia
is wrong, a small correction layer reads the caret x, hit testing, caret stops and selection boxes from the glyph runs
as drawn. The layer is keyed on the line's shape (two or more drawn runs, at least one right to left, every run with
glyphs), not on the paragraph direction and not on the Avalonia version. Each fault is pinned by a test that names the
Avalonia version or versions it fails on. A fault's workaround is removed only when its pin passes on both versions.

ADR 0015 expected the faults to be Avalonia 11.3's alone, to be deleted when FieldWorks moves to Avalonia 12. They
are not: Avalonia 12 also gives wrong hits in lines that mix directions, and numbers the clusters of trailing spaces
after an Arabic run backwards. So the layer stays for as long as either version needs it, and some of its corrections
are for Avalonia 12 only.

**Consequences:** the README's list of per-version workarounds names each fault and the versions it applies to. The
11.3-only faults (hit testing inside one right-to-left run) still go when FieldWorks moves to Avalonia 12.

Owner decision, 2026-11-04. Supersedes the "11.3 only" scope of ADR 0015.
