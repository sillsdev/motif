# The Texts format — FLExText as JSON

`texts.json` is a JSON array with one `{key, document}` record per selected Text. The `key` identifies
that Text; `document` contains its interlinearised content in FLExText-like JSON, with sentences,
words, morphemes, glosses, and categories where analyses exist. The current Handoff writes JSON only:
it does not write XML siblings or offer a `--flextext` option.

## How JSON uses FLExText names

Inside each `document`, FLExText element names become JSON object keys, repeated elements become
arrays, and each `item` is represented as `{"type", "lang", "value"}`. This page describes the
JSON representation written by Handoff; it does not describe an XML export route.

## Shape

```text
[
{ "key": "text-title-guid", "document":
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
]
```

A record's `document` contains one `interlinear-text` for its selected Text. `word` carries no
`morphemes` object at all for an unanalysed word or for
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

## `analysisStatus`: Motif's JSON vocabulary

An analysed word's `morphemes` object carries `analysisStatus`, with exactly one of two values:
`"approved"` or `"unapproved"`. This says whether the project's human agent approved its chosen
analysis. An unanalysed word has no `morphemes` object and therefore no `analysisStatus` field in
`texts.json`. FLExText's own XSD defines a separate XML enumeration (`humanApproved`, `guess`,
`guessByHumanApproved`, `guessByStatisticalAnalysis`); those strings are not written by Handoff.

Before drawing any linguistic conclusion from a corpus of these files — "this suffix always
attaches to nouns," "this root never co-occurs with that affix" — restrict the reasoning to
words whose `analysisStatus` is `"approved"`. An `"unapproved"` analysis is only as reliable as
whatever guessed it, which is exactly the caveat FieldWorks' own tooling encodes by refusing to
promote a guess into an approved analysis without a person's confirmation.

## A synthetic worked example

Invented language, invented sentence — not real project data:

```json
[
{ "key": "toy-story-1-example-guid", "document": { "interlinear-text": [
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
]
```

Read against the tables above: the sentence's free translation is "She ran quickly." (the
phrase-level `gls`); the one word is fully analysed and approved, built from a root gloss "run"
and a past-tense suffix; the final word is punctuation, with no `guid` and no `morphemes`.
