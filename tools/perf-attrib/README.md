# View-switch perf harness and attribution tools

Opt-in measurement of page and Texts-tab switches. Nothing here runs in the normal gate: the facts use
`PerfFact`, which skips unless `MOTIF_PERF=1`, and they assert no wall-clock figures.

Tests (project `SIL.Motif.Tests.App`):

- `ViewSwitchPerfTests`: sample project, cold then warm, two window sizes.
- `LargeProjectViewSwitchPerfTests`: the 3000-word large fixture with 200 pending changes and the Texts reader primed.

Run (Linux, from the repo root):

    MOTIF_PERF=1 MOTIF_PERF_N=3 MOTIF_PERF_TRACE=/tmp/large.jsonl \
      pwsh ./test.ps1 -Project SIL.Motif.Tests.App -Filter LargeProjectViewSwitchPerfTests

`MOTIF_PERF_N` is the warm pass count; `MOTIF_PERF_TRACE` is the JSON-lines trace. Close other test runs first and record
`uptime` with the numbers, because wall-clock figures move with machine load.

Each switch writes extra spans: `attrib.frame-split` (render ticks versus Input-priority jobs, in ms), `attrib.tree`
(visuals created and removed, by owning view and by type) and `attrib.settle` (the Background work after the first
frame). The harness pumps only Input priority and above to the first frame, so work deferred to Background is not
counted as frame wait.

Summaries (Python 3 standard library only):

    python3 tools/perf-attrib/split.py TRACE "warm large"   # frame split per view, medians
    python3 tools/perf-attrib/large.py TRACE "warm large"   # loop, created controls, owners, settle per view
    python3 tools/perf-attrib/agg3.py SPEEDSCOPE.json FILTER N [incl|self]   # needs a speedscope trace of a harness run

`tools/Summarize-MotifTrace.ps1` makes the table for the sample-project run.
