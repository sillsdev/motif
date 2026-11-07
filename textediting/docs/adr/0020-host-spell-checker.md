# The host supplies spell checking; the field draws it

FieldWorks owns the dictionaries, one per writing system, through Hunspell. The host gives the field a checker for
each writing system. The field checks visible paragraphs when idle and draws the red squiggles. It applies FieldWorks'
skip rules: the spell-check property, embedded objects, and text that may not be edited. It hands suggestions to the
context menu (ADR 0021).

Owner decision, 2026-11-03.
