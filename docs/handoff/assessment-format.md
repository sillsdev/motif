# The Assessment format — `assessment.json`

**Status: this describes a contract, not yet a shipped file.** `assessment.json` is defined by
[ADR 0045](../adr/0045-the-handoff-is-five-files-and-a-pasted-header.md); the writer that produces
it has not landed. Everything below is the ADR's decision, not an observed file — where the ADR
leaves a detail open, this document leaves it open too rather than guessing at it.

`assessment.json` is the record of one Assessment run: PanGloss actually parsing every word in the
Handoff's Selection, timed, plus the derivation traces for whichever words the person chose to
look closer at. Statistics alone can tell you a word failed; they cannot tell you why. This file
exists because "why didn't *xyz* parse" needs the parser's own trace, not just a pass/fail count.

## A Handoff can have no Assessment at all

`assessment.json` is not a mandatory file. When nobody has run one, it is simply absent, and
`handoff.md` says so. Motif is replacing an export that never ran a parser in the first place;
refusing to hand off anything until an Assessment exists would make the replacement worse than
what it replaces.

## What produces it: one invocation, two phases

An Assessment run is, from the person's side, one action. Internally it has two phases, in this
order:

1. **The batch pass.** Every word in the Selection is parsed once, merged (PanGloss's ordinary,
   collapsed search — see PanGloss's `docs/formats/trace-format.md` for what "merged" means and
   why it matters) and timed. This is where every word's statistics record and its measured timing
   come from.
2. **The traced pass.** After the batch pass has already produced its results — so the traced set
   can be chosen using what the batch pass found — a chosen subset of words is re-parsed with
   `pangloss parse <grammar> <word> --trace --trace-format=json`, one word per PanGloss invocation,
   because `batch` itself cannot trace. This pass runs unmerged, which means it explores more of
   the search space than the batch pass did for the same word; see `docs/formats/trace-format.md`
   for why that is deliberate.

Only the traced words pay the cost of the second phase. **Every word's timing in
`assessment.json`, traced or not, comes from the batch pass.** A traced word's time is not
re-measured during tracing, precisely so that the times in this file stay comparable to each other
regardless of which words happened to get traced.

## The JSON convention

Like every JSON file in the Handoff, `assessment.json` is valid JSON with **one record per line**:
pretty-printed to the record, compact within it. This means a `grep` for a word's surface form
returns that word's whole record on one line, and the file as a whole still loads with a plain
`json.load`. A single grepped line carries the array's trailing comma, so strip that comma before
`json.loads` on the line by itself — `parse_grammar_texts_assessment.py`'s loaders accept either. For example, to find everything
the Assessment recorded about the word *mirusi*:

```
grep '"mirusi"' assessment.json
```

## Per-word statistics

Every word in the Selection — whether or not it was chosen for tracing — gets one record from the
batch pass, with these fields:

| Field | What it holds |
|---|---|
| `word` | The surface form that was parsed. Always present; this is what a `grep` lands on. |
| `outcome` | One of `analysed`, `no-analysis`, `capped`, `timed-out`, `skipped`. |
| `elapsedMs` | How long the batch pass took on this word. Absent when it was not measured. |
| `signature` | PanGloss's own analysis signature, when it produced one. |

`outcome` is the field to read before trusting any claim about a word. `no-analysis` means the
parser ran to completion and found nothing, which is a real answer. `capped` and `timed-out` mean
it stopped early, so "this word does not parse" is **not** a conclusion you may draw from them —
the search was cut short, not exhausted. `skipped` means the word never reached the parser.

There is deliberately no step count here. Step counts compare across machines where milliseconds do
not, so one would be the better measure, but Motif does not yet record one for the batch pass and
this file states only what it actually holds.

## The Selection, including typed words

The Selection is what the Assessment actually ran against, and it admits a third kind of member
beside a Text and a lexicon entry: a word the person simply typed in, which need not appear
anywhere in the project at all. "Why didn't *xyz* parse" is usually asked about a word someone has
in their head, not one already sitting in a Text — so a typed word is parsed and measured exactly
like every other member of the Selection, and its statistics record in this file looks like any
other word's.

## Which words get traced

Not every word in the Selection is traced — tracing runs a second, larger search per word, and
running it for everything would multiply that cost across the whole Selection. The traced set is
chosen by the person, from three kinds of choice: the typed words, the *N* slowest words (by the
batch pass's own timing), and all the words in one or more chosen Texts. There is no fixed ceiling
on how many words may be traced; when a set does need to be narrowed, typed words are kept first,
then words that failed or hit a limit, then the slowest of what remains. Whichever words end up
traced, `assessment.json` records only the outcome of that choice — the choice itself is made at
run time, not written into the file.

## Traces, keyed by word

For every traced word, `assessment.json` carries two things together, keyed by the word:

- **The tree, verbatim.** Exactly what `pangloss parse <grammar> <word> --trace
  --trace-format=json` produced for that word. This document does not restate what the tree's
  fields mean or what its node types are — see PanGloss's own
  [`docs/formats/trace-format.md`](https://github.com/sillsdev/PanGloss/blob/v0.3.2/docs/formats/trace-format.md)
  for that. The tree travels unedited because deciding which branch of a derivation mattered is
  the judgement being handed to whoever — person or model — reads the Handoff; Motif does not
  prune it first.
- **A derived one-line summary.** Built from that same tree, so fifty traces can be triaged without
  opening fifty trees, and so a `grep` for a word lands on something readable immediately. It
  states: the outcome, the step count, whether the trace completed, the distinct failure reasons
  that appear anywhere in the tree, and the deepest rule the derivation reached.

## `traceComplete: false`

Tracing is not allowed to put the rest of the Assessment at risk. The batch pass's results are
published as the retained invocation as soon as that pass completes, before any tracing starts;
traces are added to it as they land, one at a time. If the run is cancelled during the traced
phase, the invocation is left standing, marked partial, with a count of how much tracing finished.

Because a traced parse has no PanGloss-side bound of its own — `parse --trace` carries no
`--step-cap` — Motif imposes its own timeout on the traced phase, shorter than the batch pass's.
A trace that runs out that cap or that timeout is not discarded: it is kept, marked
`traceComplete: false`, together with the reason it stopped. A half-finished derivation is still
evidence of what the parser was doing when it ran out of room, and the one-line summary for such a
trace is built from whatever the tree contains up to that point, not withheld until the tree is
complete.
