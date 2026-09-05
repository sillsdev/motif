# The Texts format — FLExText as JSON

Each file under `texts/` is one interlinearised document from the language project — a real
sentence, broken into words, each word broken into morphemes, each with its own gloss and
category where an analysis exists for it. It is the same information FieldWorks would export as
a `.flextext` XML file, carried over into JSON with exactly the same names, so nothing here is a
new vocabulary to learn if you already know FLExText — and nothing is lost if you don't.

## Why JSON, and what stays identical to FLExText

FLExText's element structure becomes JSON with the elements as keys, repeated elements as
arrays, and every `item` collapsed to `{"type", "lang", "value"}`. Motif's own FLExText XML
writer and this JSON writer both serialize the same in-memory reading of the project, so the two
can never drift from each other — the mapping below applies to either. JSON is the default
because every file dropped into a chat model is read as tokens, and JSON costs measurably fewer
of them than XML for the same content; the XML sibling is written only when explicitly asked
for, for a FieldWorks round trip this Handoff is not part of.

## Shape

```text
{ "document":
  { "interlinear-text": [
      { "guid": "...", "item": [ {type, lang, value}, ... ],
        "paragraphs": { "paragraph": [
            { "guid": "...", "phrases": { "phrase": [
                { "guid": "...", "item": [ {type, lang, value}, ... ],
                  "words": { "word": [
                      { "guid": "...", "item": [ {type, lang, value}, ... ],
                        "morphemes": { "analysisStatus": "...", "morph": [
                            { "guid": "...", "item": [ {type, lang, value}, ... ] }
                        ] } }
                  ] } }
            ] } }
        ] } }
  ] } }
```

A `document` may hold more than one `interlinear-text`, one per Text the person chose when they
ran the Handoff. `word` carries no `morphemes` object at all for an unanalysed word or for
punctuation — punctuation words also carry no `guid`, because the underlying FieldWorks
punctuation object is not addressed the way an analysed word is.

## The item type vocabulary Motif actually writes

FLExText's own schema allows any string as an `item`'s `type`, so a reader cannot rely on a fixed
enumeration in general. What follows is not FLExText's full vocabulary — it is the exact set
Motif's own reader emits, each traced to the LibLCM property it came from:

| Type | Appears on | LibLCM source |
| --- | --- | --- |
| `title` | the Text itself | `IText.Name` |
| `gls` | a phrase, **and separately** a morpheme | the phrase's `FreeTranslation`; a morpheme's sense `Gloss`. These are unrelated facts that happen to share a type string — see below |
| `lit` | a phrase | `LiteralTranslation` |
| `txt` | a word, **and separately** a morpheme | the word's `WfiWordform.Form`; a morpheme's `MorphRA.Form`, falling back to the morph bundle's own `Form` when the allomorph reference itself is unset |
| `cf` | a morpheme | the morpheme's sense's entry's citation form |
| `msa` | a morpheme | the morph bundle's MSA interlinear abbreviation, written in the project's default analysis writing system |
| `pos` | a word | the chosen analysis's category `Abbreviation`, falling back to its `Name` when no abbreviation is set |
| `punct` | a word | `IPunctuationForm.Form` |

**The same type string means different things at different nesting levels — this is the single
most common mistake in reading this format.** `gls` at the phrase level is a sentence's free
translation; `gls` on a morpheme is that one morpheme's sense gloss. `txt` on a word is the
surface wordform; `txt` on a morpheme is that morpheme's own surface form. A query for "every
`gls` in the document" without scoping to a level conflates two unrelated facts. Always match on
the containing element (`phrase`, `word`, or `morph`), not on the item type alone.

This is a **deliberately smaller vocabulary than full FLExText**: Motif's reader does not emit
`segnum`, `note`, `hn`, `variantTypes`, `source`, `comment`, `description`, `title-abbreviation`,
or `text-is-translation`, all of which a `.flextext` file exported directly from FieldWorks may
carry. A file here that lacks a segment number or an annotator's note is not missing data Motif
dropped — Motif's projection never reads those fields to begin with.

## `analysisStatus`: Motif's vocabulary, not FLExText's XML enumeration

Every analysed word's `morphemes` object carries `analysisStatus`. In this JSON mirror, its
value is one of exactly three strings — `"unanalysed"`, `"approved"`, or `"unapproved"` — Motif's
own vocabulary for what LibLCM actually distinguishes: whether an analysis is chosen at all, and
if so, whether the project's human agent has approved it. **This is not the same enumeration
FLExText's own XSD defines for the XML format** (`humanApproved`, `guess`,
`guessByHumanApproved`, `guessByStatisticalAnalysis`) — only the `--flextext` XML sibling
translates into that enumeration, mapping `approved` to `humanApproved` and `unapproved` to
`guess`. A reader working only from the JSON should not expect to see FLExText's XML status
strings verbatim; they are the JSON's own three values throughout.

Before drawing any linguistic conclusion from a corpus of these files — "this suffix always
attaches to nouns," "this root never co-occurs with that affix" — restrict the reasoning to
words whose `analysisStatus` is `"approved"`. An `"unapproved"` analysis is only as reliable as
whatever guessed it, which is exactly the caveat FieldWorks' own tooling encodes by refusing to
promote a guess into an approved analysis without a person's confirmation.

## A synthetic worked example

Invented language, invented sentence — not real project data:

```json
{ "document": { "interlinear-text": [
  { "guid": "00000000-0000-0000-0000-000000000001",
    "item": [ {"type": "title", "lang": "en", "value": "Toy Story 1"} ],
    "paragraphs": { "paragraph": [
      { "guid": "00000000-0000-0000-0000-000000000002",
        "phrases": { "phrase": [
          { "guid": "00000000-0000-0000-0000-000000000003",
            "item": [
              {"type": "gls", "lang": "en", "value": "She ran quickly."},
              {"type": "lit", "lang": "en", "value": "Ran quickly."}
            ],
            "words": { "word": [
              { "guid": "00000000-0000-0000-0000-000000000004",
                "item": [
                  {"type": "txt", "lang": "inv", "value": "Mirusi"},
                  {"type": "pos", "lang": "en", "value": "v"}
                ],
                "morphemes": { "analysisStatus": "approved", "morph": [
                  { "guid": "00000000-0000-0000-0000-000000000005",
                    "item": [
                      {"type": "txt", "lang": "inv", "value": "miru"},
                      {"type": "cf", "lang": "inv", "value": "miru"},
                      {"type": "gls", "lang": "en", "value": "run"},
                      {"type": "msa", "lang": "en", "value": "v"}
                    ] },
                  { "guid": "00000000-0000-0000-0000-000000000006",
                    "item": [
                      {"type": "txt", "lang": "inv", "value": "-si"},
                      {"type": "gls", "lang": "en", "value": "3sg.pst"},
                      {"type": "msa", "lang": "en", "value": "infl"}
                    ] }
                ] } },
              { "item": [ {"type": "punct", "lang": "inv", "value": "."} ] }
            ] } }
        ] } }
    ] } }
] } }
```

Read against the tables above: the sentence's free translation is "She ran quickly." (the
phrase-level `gls`); the one word is fully analysed and approved, built from a root gloss "run"
and a past-tense suffix; the final word is punctuation, with no `guid` and no `morphemes`.
