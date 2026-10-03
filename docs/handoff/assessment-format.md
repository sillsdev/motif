# Assessment format

An assessed AI Handoff keeps measured results beside any raw trace collected for a word. A Handoff started from **Try a Word** keeps the trace already on screen with the Baseline that produced it, without running a replacement Assessment or trace.

`parse-results.json` is a valid JSON array with one compact record per line. The file as a whole loads with `json.load`; a single grepped line carries the array's trailing comma, so remove it before parsing that line alone.

## Batch measurements

Each word in the Assessment Selection has one record from the batch pass:

| Field | What it holds |
|---|---|
| `word` | The parsed surface form. |
| `outcome` | The outcome recorded by PanGloss, such as `analysed`, `no-analysis`, `capped`, `timed-out`, or `skipped`. |
| `elapsedMs` | The batch time, when measured. |
| `signature` | PanGloss's analysis signature, when it recorded one. |

`capped` and `timed-out` record a stopped batch search; they do not establish that no analysis exists. `skipped` records that the word did not reach the parser. Read other outcomes as the result the batch pass recorded, without treating timing as a verdict.

## A trace in a one-word Handoff

There are two one-word trace routes. A fresh assessed Handoff runs an Assessment, traces the word against the grammar retained for that Assessment, writes the diagnostic to `traces/<encoded-word>.trace.json`, and adds a `trace` member to the matching `parse-results.json` record. This route can be used by command callers.

From **Try a Word**, **AI Handoff for this word** exports the diagnostic already displayed and opens the Baseline named in that trace's host capture. It writes the selected diagnostic unchanged under `traces/`, with no `parse-results.json`; it does not run an Assessment or a replacement trace. If that captured Baseline is no longer available, Motif refuses the Handoff rather than relabeling the trace with a newer Baseline.

The trace member has a Handoff-relative `file` path and a compact `summary`:

| Summary field | What it holds |
|---|---|
| `outcome` | `parsed`, `no-analysis-recorded`, or `invalid-shape`, as read from the diagnostic. |
| `parserSteps` | The parser's recorded step count, or JSON `null` when it was not recorded. |
| `completion` | `complete` when the diagnostic records a finished search; `incomplete` when it records a stopped trace; `not-run` when the word's shape prevented a search; `unknown` when no supported completion fact was recorded. Motif does not infer completion from a clean process exit. |
| `failureReasons` | Distinct reason codes present in the recorded tree, in ordinal order. They do not establish which neighboring event caused an attempt to fail. |
| `deepestRule` | The deepest named rule in the typed reading, or JSON `null` when none is recorded. |

For both routes, the raw file preserves PanGloss's complete `pangloss.trace-details.v3` envelope, including `search`, `result`, `categories`, and `trace`; the summary is additional and does not select or remove tree branches. Unknown producer fields survive. The assessed route adds Motif's recorded host capture. The Try a Word route preserves the full selected capture, including its existing host capture. See [Try a Word diagnostic JSON](trace-diagnostic-format.md) for the retained v3 fields and their meanings, and [PanGloss v0.6.0's trace format](https://github.com/sillsdev/PanGloss/blob/v0.6.0/docs/formats/trace-format.md) for the producer's diagnostic fields. Percent encoding in the filename keeps word punctuation out of the file name. `handoff.md` identifies the trace by that filename, which remains usable after a chat upload flattens folders.

For a saved trace, [load a trace](cmd:trace%20--load) reads the same diagnostic without opening a project or running PanGloss. [Trace a word](cmd:trace) runs one against a project's current Baseline.

## Reading a record

Use `word` to locate the batch record and, when present, `trace.file` to open its raw diagnostic:

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
