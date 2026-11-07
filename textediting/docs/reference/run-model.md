# The run model

`src/SIL.TextEditing` holds the model. It has no Avalonia and no liblcm reference.

## Runs are shaped like ITsString

A `Run` is shaped like one run of FieldWorks' `ITsString`:

- text;
- a writing-system id;
- an opaque property bag (`PropertyBag`) keyed by FieldWorks property numbers;
- object data (`ObjData`) for inline objects.

Ws and ObjData are fields of `Run`, not bag entries (ITsString holds them as properties 1 and 6). The Ws is a language-tag string, not
an integer handle. The [LCModel adapter](lcmodel.md) maps both. The editor never interprets a bag; it carries it through every edit.

The other model types are `Paragraph`, `Document` and `PropertyBag`. A paragraph's key (`Paragraph.Key`) is opaque and null until the host
assigns one (ADR 0006).

## The host owns undo

The editor keeps no undo stack. The host owns it, as FieldWorks' LCModel does (ADR 0003). The playground uses a stand-in,
`SimpleUndoStack`; no LCModel undo glue exists yet.

## `RunList`

`Paragraph.Runs` is a `RunList`, not a `List<Run>`.

- It tells its paragraph whenever a run is added, removed or replaced, and each run tells it when its text changes.
- So `Paragraph.Length`, `RunBefore`, `RunAfter` and `Spans` are answered from run starts the paragraph keeps current (a binary search, not
  a walk over the runs), however a host changes the runs.
- A host may edit `Run.Text` or the list directly.
- `RunList` has `Add`, `Insert`, `RemoveAt`, `RemoveRange`, `AddRange`, `Clear`, an indexer and `IEnumerable<Run>`. Use LINQ for
  `ConvertAll`, `FindIndex` and the like.
- To replace the runs, assign a `new RunList(runs)` to `Paragraph.Runs`.
- `Paragraph.Runs = list` also takes a `List<Run>` directly. The paragraph adopts a new `RunList` over the same runs, not the list itself:
  the `Run` objects are shared, but a later `list.Add` is not seen by the paragraph.

## Changes in place

A change to a run's `PropertyBag` in place (`SetInt`, `SetStr`, or a write to `IntProps` or `StrProps`) is seen too. The bag tells the
paragraphs that hold it, so `ChangeStamp` moves and a field shows the change. See
[what a host calls after a change](spelling-and-language-service.md#what-a-host-calls-after-a-change) for the rest.

## Rules

- `Run.Text = null` throws `ArgumentNullException` (use an empty string). So do `Run.Props = null` and `Paragraph.Props = null`.
- Looping over `Paragraph.Runs` while the list is added to, removed from or has a run replaced throws `InvalidOperationException`, as it
  does for a `List<Run>`. Changing a run's text or properties meanwhile is fine.
- `Paragraph.Text` is safe to read from any thread while nothing changes the paragraph. The model itself is edited on the UI thread only
  (ADR 0024).

## Work counters

The work counters tests use (`TextReads`, `ElementReads`) exist in a build that defines `TEXTEDITING_COUNTERS` (every configuration but
Release) and cost nothing in a Release build.
