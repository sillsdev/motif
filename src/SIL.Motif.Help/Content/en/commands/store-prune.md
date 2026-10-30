# Prune the project store

`store prune` removes older standalone Assessments that are not needed by retained evidence and reclaims available store space.

Motif keeps the newest 20 standalone Assessments of each kind by default, along with pinned, current, and cited evidence. Set `standalone-assessments-per-kind` in the `[retention]` section of the project configuration to change the count. Motif also prunes after a successful Assessment.

```powershell
motif store prune --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

The result reports the number of Assessments and invocation directories removed and the store file size before and after reclamation. Directories whose ownership cannot be proven are kept; the result reports their count, total size and parent path so they can be inspected. Active readers can postpone reclaiming some space; run the command again after they finish.
