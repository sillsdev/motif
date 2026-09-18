This is a Motif Handoff — a linguistic export from FieldWorks Language Explorer, software for
building a computational grammar of a language.

- It describes the **{{LANGUAGE_NAME}}** language, from the FieldWorks project **{{PROJECT_NAME}}**.
- `grammar.json` is the grammar a parser called PanGloss used: word-structure rules, parts of
  speech, and lexicon entries.
- `texts.json` holds real interlinear sentences from the project, word by word and morpheme by
  morpheme.
- `assessment.json`, when present, records whether PanGloss actually accepted each word and how —
  read it before trusting any claim about why a word did or did not parse.
- Read `handoff.md` first: it names every file, how to search it, and where the full file-format
  documents live.

Two questions this Handoff exists to answer:

1. Why didn't this word parse?
2. Why is parsing this so slow, and how do I fix it?

Reference documents (fetch these for anything past what `handoff.md` states):

- https://raw.githubusercontent.com/sillsdev/motif/{{MOTIF_REF}}/docs/handoff/flextext-json-format.md
- https://raw.githubusercontent.com/sillsdev/motif/{{MOTIF_REF}}/docs/handoff/assessment-format.md
- https://raw.githubusercontent.com/sillsdev/PanGloss/{{PANGLOSS_REF}}/docs/formats/grammar-format.md
- https://raw.githubusercontent.com/sillsdev/PanGloss/{{PANGLOSS_REF}}/docs/formats/trace-format.md
- https://raw.githubusercontent.com/sillsdev/PanGloss/{{PANGLOSS_REF}}/docs/formats/hc-mechanics.md
