# One field control for every text field kind

One `RichTextField` edits a single string, one writing-system row of a multi-string and a multi-paragraph text. In
single-paragraph mode it passes Enter to the host. A multi-writing-system field is the host stacking one field per
writing-system row. FieldWorks (one Views engine for every kind of slice), Avalonia (`TextBox` with
`AcceptsReturn`) and LibreOffice (one edit engine for cells and text boxes) all make the same choice.

Owner decision, 2026-11-03.
