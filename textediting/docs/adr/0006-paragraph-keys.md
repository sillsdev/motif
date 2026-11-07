# Paragraphs carry host keys with FieldWorks' split and join rules

Each paragraph carries an opaque key from the host, which in FieldWorks is the StTxtPara GUID. Interlinear segments
and analyses, Chorus Send/Receive merges and links all hang off that key. If edits gave paragraphs new keys, that
data would be lost.

The rules match FieldWorks' observed behaviour:
- a new paragraph has no key until the host assigns one;
- a split keeps the key on the left part;
- a split at offset 0 keeps the key on the right part, which still holds the text;
- a join keeps the key of the non-empty survivor.

**Consequences:** the rules live in the UI-free model and are pinned by tests, so no surface can break them.

Owner decision, 2026-11-03.
