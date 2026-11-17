# Lexicon import

Notes on bringing lexicon data into a project from another source. The command that imports a lexicon is not written yet; this page starts with the part that already works, how you handle attribution, and the rest of the page will grow around it.

## Attribution

Data brought in from another source usually carries a licence, such as GPL-3.0 or CC BY-SA, that must stay with the data. Motif keeps that **Attribution** in the project itself, in two places that FieldWorks already has, and adds no field:

1. **Per entry:** a short tag `<source>@<revision> <licence>`, such as `lang-sme@9ba7be7 GPL-3.0`, in the entry's Bibliography, written in an analysis writing system. FieldWorks exports the Bibliography as a bibliography note, so the tag survives a `.fwdata` save and a LIFT round trip.
2. **Per project:** one notice in the project's Description, `Attribution: <source>@<revision> <licence>`, once for each source and revision.

Both are Proposal changes, so each goes through a Draft, a Dry Run and a person's Apply like any other change:

- [compose-set-entry-attribution](cmd:compose-set-entry-attribution) tags many entries at once from a list of entry ids or GUIDs and the tag.
- [compose-set-project-attribution](cmd:compose-set-project-attribution) states the project notice.

To attribute data that is already in the project, for example entries applied earlier without a tag:

1. Write the ids of those entries, one per line, to a file.
2. Stage `compose-set-entry-attribution --entries-file <file> --tag "<source>@<revision> <licence>"` in a Draft, then `compose-set-project-attribution` with the same tag in the same Draft.
3. Dry Run, review the Difference, and let a person Apply.

Both commands are idempotent and keep other text. Running them again stages nothing for an entry or project that already carries the tag, and the text the Bibliography or Description already held stays. The same source and revision under a different licence is refused rather than stated twice, because a person decides which licence applies.

Give the tag the licence as the source states it, with the source name and revision from the source's own repository, so anyone reading the project can find the exact data. Write the full citation only in the project notice if you need one; the per-entry tag stays short.
