# Read this first

**This folder contains real linguistic data about a real language project — grammar rules,
lexicon entries, and real corpus sentences.** Uploading it to a chat model sends that data to
whoever runs it (OpenAI, Anthropic, or another provider). Check the project's own
data-sensitivity policy before sharing an unpublished, restricted, or community-sensitive
project this way, independently of anything else in this folder.

**Everything in this folder is as of FieldWorks' last save** — the moment the project was last
written to disk, not necessarily the moment this folder was written. If FieldWorks was holding
the project open when this Handoff was captured, whatever the linguist has typed since that last
save is not here.

## What each file is

| File | What it is |
| --- | --- |
| `grammar.json` | The grammar: every rule, part of speech, phoneme, and lexicon entry the parser knows about. See `reference/grammar-format.md` |
| `texts/*.flextext.json` | One file per chosen Text — a real interlinearised sentence, word by word, morpheme by morpheme. See `reference/flextext-json-format.md` |
| `selection.txt` | The exact list of words a parse run was asked to attempt, and where each one came from |
| `statistics.md` | The parser's own summary of that run — coverage, timing, what completed and what did not |
| `statistics/<group>.jsonl` | One file per statistics group — `word`, `object`, `allomorph`, `morpheme`, `group`, `never-fires` — for a question the summary alone cannot answer |
| `starter-prompt.md` | A ready-to-use prompt that gives an AI agent the folder's purpose, constraints, and first reading steps |
| `read_handoff.py` | One script, Python standard library only, that loads and validates the files above, indexes the grammar by GUID, and answers the common questions in `recipes.md` without you writing the plumbing yourself |
| `recipes.md` | Worked examples: how to ask for each of the above, with the call and the question it answers |
| `reference/` | Three reference documents this repository maintains and copies into every Handoff: the grammar format, the Texts format, and how the parser itself behaves |

## How to answer

The person reading your answer is a field linguist with a real language project. They know
their language; they do not necessarily know how a rule-based parser works internally, and they
did not ask for a tour of it.

- **Be accurate before being simple.** An answer that misstates what the grammar or the parser
  actually does is worse than no answer. When the honest answer is complicated, give the short
  version first and the detail underneath.
- **Define a technical term the first time you use it**, in ordinary words — never leave an
  abbreviation bare.
- **Lead with what to check or change**, then explain the mechanism behind it for whoever wants
  it.
- **Show a worked example** with a real form from the data over a paragraph of theory.
- **Flag every guess.** Name what you are unsure about and what would settle it. An unmarked
  guess reads exactly like a fact to someone who cannot see your reasoning.
- **Trust `analysisStatus`, not silence.** A word's analysis being present is not the same as it
  being confirmed — see `reference/flextext-json-format.md` before treating any morpheme
  breakdown as established fact rather than a guess.

## Getting the newest copy of a reference file

The three files under `reference/` and this Handoff's `read_handoff.py` are maintained in a
public repository and copied into every Handoff folder at the moment it is written. If a
question turns on a detail that seems to have changed since, the current version is always at:

```
https://raw.githubusercontent.com/sillsdev/motif/main/docs/handoff/grammar-format.md
https://raw.githubusercontent.com/sillsdev/motif/main/docs/handoff/flextext-json-format.md
https://raw.githubusercontent.com/sillsdev/motif/main/docs/handoff/hc-mechanics.md
https://raw.githubusercontent.com/sillsdev/motif/main/src/SIL.Motif.Commands/Handoff/Assets/read_handoff.py
```

Use the raw URL, not the ordinary GitHub page — it returns plain text with no site chrome, which
a model can fetch or read without JavaScript rendering.

## A note on trusting the answers

This is AI. It can be very wrong, and it can be wrong while sounding certain. Treat any claim
about the grammar or the data as a hypothesis to check against the files themselves, not as a
finding — especially anything that would change what you author in FieldWorks.
