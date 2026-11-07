# Assessment format

An assessed AI Handoff keeps measured results beside a cost-free trace projection collected for a word. A Handoff started from **Try a Word** keeps the trace already on screen with the Baseline that produced it, without running a replacement Assessment or trace.

`parse-results.json` is a valid JSON array with one compact record per line. The file as a whole loads with `json.load`; a single grepped line carries the array's trailing comma, so remove it before parsing that line alone.

## Batch measurements

Each word in the Assessment Selection has one record from the batch pass:

| Field | What it holds |
|---|---|
| `word` | The parsed surface form in NFC (composed Unicode). |
| `outcome` | The outcome recorded by PanGloss, such as `analysed`, `no-analysis`, `capped`, `timed-out`, or `refused`. |
| `signature` | PanGloss's opaque analysis signature, unchanged when it recorded one. |
| `refusal` | For a refused word, the parser refusal `code` and its `reason`. |
| `analysisCount` | The number of authoritative morphology records returned, when available. |

`capped` (stopped at the work limit) and `timed-out` (stopped at the time safety net, which depends on how fast this computer is) record a stopped batch search; they do not establish that no analysis exists. `refused` means PanGloss received the word and declined it. An `invalidShape` code records that the word could not be read with the project’s phonemes; missing character names are not inferred. Read other outcomes as the result the batch pass recorded, without treating work or timing as a verdict.

## A trace in a one-word Handoff

There are two one-word trace routes. A fresh assessed Handoff runs an Assessment, traces the word against the grammar retained for that Assessment, writes the trace projection to `traces/<encoded-word>.trace.json`, and adds a `trace` member to the matching `parse-results.json` record. This route can be used by command callers.

From **Try a Word**, **AI Handoff for this word** exports the diagnostic already displayed and opens the Baseline named in that trace's host capture. It writes a projection of the selected diagnostic under `traces/`, with no `parse-results.json`; it does not run an Assessment or a replacement trace. If that captured Baseline is no longer available, Motif refuses the Handoff rather than relabeling the trace with a newer Baseline.

The trace member has a Handoff-relative `file` path and a compact `summary`:

| Summary field | What it holds |
|---|---|
| `outcome` | `parsed`, `no-analysis-recorded`, or `invalid-shape`, as read from the diagnostic. |
| `completion` | `complete` when the diagnostic records a finished search; `incomplete` when it records a stopped trace; `not-run` when the word's shape prevented a search; `unknown` when no supported completion fact was recorded. Motif does not infer completion from a clean process exit. |
| `failureReasons` | Distinct reason codes present in the recorded tree, in ordinal order. They do not establish which neighboring event caused an attempt to fail. |
| `deepestRule` | The deepest named rule in the typed reading, or JSON `null` when none is recorded. |

Both routes export the same bounded trace projection used by standalone trace output. It retains search completion, analyses, morphs, stopping reasons, references and attempt nodes. `nodesComplete` says whether the whole recorded tree fit the bound. Raw diagnostics, unpaired parser work and elapsed times are excluded. Percent encoding in the filename keeps word punctuation out of the file name. `handoff.md` identifies the trace by that filename, which remains usable after a chat upload flattens folders.

For a saved trace, [load a trace](cmd:trace%20--load) reads the diagnostic without opening a project or running PanGloss. [Trace a word](cmd:trace) runs one against a project's current Baseline.


## Reading a record

Use `word` to locate the batch record and, when present, `trace.file` to open its trace projection:

```
grep '"mirusi"' parse-results.json
```

A parsed JSON reader can follow the recorded path directly. If a chat upload flattened the folder, it can fall back to the same file name beside `parse-results.json`:

```python
import json
from pathlib import Path

records = json.load(open("parse-results.json", encoding="utf-8"))
record = next(item for item in records if item["word"] == "mirusi")
trace_path = Path(record["trace"]["file"])
if not trace_path.is_file():
    trace_path = Path(trace_path.name)
trace = json.load(open(trace_path, encoding="utf-8"))
```

The embedded `read_results.py` helper reads the Handoff's files and documents its commands with `--help`.
