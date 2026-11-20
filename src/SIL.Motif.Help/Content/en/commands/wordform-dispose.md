# Give word types a disposition

`wordform dispose` records that a corpus word type is not a word to analyse: a name, a loan, a word of another language, an abbreviation, a numeral, a non-word, or a misspelling of another word. Each disposition carries the evidence for it, so Coverage can count the type as accounted for instead of leaving it open. It writes Motif's own store beside the project and never the FieldWorks project, so it needs no Proposal and no Apply.

## Example

```text
motif wordform dispose Nairobi Mombasa Kisumu --project <fwdata> --as name --evidence "place names in the Gazetteer"
motif wordform dispose recieve --project <fwdata> --as misspelling --intended receive --evidence "typo for receive in the 1998 print"
motif wordform dispose Kenya --project <fwdata> --as loan --evidence "English toponym" --replace
```

`--as` is one of `name`, `loan`, `other-language`, `abbreviation`, `numeral`, `non-word` or `misspelling` (`artifact` is accepted for `non-word`). `--evidence` is required. A `misspelling` also needs `--intended <form>`, and no other category accepts it.

- A disposition belongs to a word type, not to one occurrence: it holds for every use of the word in the corpus. Spellings that differ only in Unicode composition are one type. Case is not folded, so give the form as the corpus word list spells it.
- A type that already stands with the same category and intended form is left alone and reported as `already-recorded`.
- A type that stands with a different category or intended form is refused with `disposition.contradiction`, and nothing in the call is written. Add `--replace` to end the standing disposition and record the new one, or [clear it](cmd:wordform%20undispose) first. Either way the earlier disposition stays in the type's history.
- A call writes every form or none. `--by <name>` records who made the call; it defaults to the signed-in user.
- Only the command line writes dispositions. Assistants can read them with [disposition show](cmd:disposition%20show) and [disposition list](cmd:disposition%20list).

## Related commands

- [Clear word type dispositions](cmd:wordform%20undispose) removes one.
- [Show a word type's disposition](cmd:disposition%20show) shows what stands and what stood before.
- [List word type dispositions](cmd:disposition%20list) pages through everything recorded.
