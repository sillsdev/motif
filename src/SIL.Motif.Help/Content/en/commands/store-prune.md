# Prune the project store

`store prune` removes older Assessments that are not needed by retained evidence and reclaims available store space.

Motif keeps the newest 20 standalone Assessments of each kind by default (5 for Object Timing, whose per-object tables are large), along with pinned, current, and cited evidence. Set `standalone-assessments-per-kind` in the `[retention]` section of the project configuration to change the count. Unneeded standalone and decided Proposal Assessments older than 90 days are removed, as are all but the newest 30 Assessments of each kind on a decided Proposal. The store targets 100 MB: when it holds more, the oldest evidence that nothing needs goes next, including evidence of Proposals already decided, and never evidence of an open Proposal. Authored and protected evidence may keep a store above the target. A decided Proposal keeps its summary and loses its word detail. When deleted rows leave a large share of the file free, this command rewrites the file to give the space back; the worker does the same once a day for the stores it has open. Motif also prunes after a successful Assessment, without the rewrite.

```powershell
motif store prune --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

The result reports the number of Assessments and invocation directories removed and the store file size before and after reclamation. Directories whose ownership cannot be proven are kept; the result reports their count, total size and parent path so they can be inspected. Active readers can postpone reclaiming some space; run the command again after they finish.
