# Read lexicon

Searches Baseline entries and counts them by morph type and category. Filter by morphType, affix, category, headword or hasGloss. Concise results are one line per entry with its sense ids; detailed results include senses, allomorphs and their environments.

With detailed verbosity, each entry includes `note` and each sense includes `glossAlternatives`, `definition` and `notes`, with text keyed by writing-system tag. The note keys match `compose-edit-lexical-note`. An absent tag means empty text, which supplies the empty `expectedText` for a guarded edit. Read these alternatives before [editing a Definition](cmd:compose-edit-definition), [Gloss](cmd:compose-edit-gloss), or [lexical note](cmd:compose-edit-lexical-note).
