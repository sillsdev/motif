# Read a Parsimony view

`parsimony view` reads one page from a fixed, documented evidence view in a published bundle. The cursor is bound to the bundle, view, and filters; the command accepts no SQL and does not build missing artifacts. Supply the Report id when reading parser cases so Motif can restore the exact Assessment references.

Use `--report <reportId>` with `parsimony-active-findings`, `parsimony-suppressed` or `parsimony-suppression-history` to compare saved judgments with that exact Report and show evidence freshness. Active and suppressed finding rows include the current head IDs and `contentDigest` values; include every head for the same `judgmentId` when revising or retracting a conflict. Suppression-history rows include each revision's `contentDigest` as well, and every row names its judgment by `visibleId` (for example `MJ-7K3Q-9XDF`) and `recordTitle`, which match the Notebook record. `unresolved-judgment` rows list records that need a look: a `check` of `hash-failed` means "hash failed on this record entry" and the judgment does not suppress its finding, and a `state` of `warning` means an edited ID or sentence that still applies. Narrow those views with `--measure-id`, `--disposition`, `--state`, `--search` or `--subject-key`.

The `environment-excess` and `natural-class-excess` views cap each name list at 30 names when read whole, and list every name when you pass `--object-id`; each row's `totals` give the full count of every list.

## Example

```powershell
motif parsimony view allomorph-context --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --bundle <bundle-id> --object-id <allomorph-guid> --json
```

Read parser outcomes from the Report that named their Assessment:

```powershell
motif parsimony view parser-cases --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --bundle <bundle-id> --report <report-id> --json
```

Use `parsimony measures` to list view codes and their required inputs. A view with missing facts or Assessment material reports that capability as unavailable rather than returning an empty result.

## Related commands

- [List Parsimony measures](cmd:parsimony%20measures) lists registered checks and views.
- [Read a Parsimony Report](cmd:parsimony%20show) opens stored findings.
