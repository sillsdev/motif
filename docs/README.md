# Documentation map

## Start here

Users start with [Motif's Help](../src/SIL.Motif.Help/Content/en/guide/what-is-motif.md), shared by the command line, window and documentation site. The [plugin](../plugin/) guides connected assistants; [parser help](parser-help/README.md) explains diagnosis and FieldWorks modeling.

## Contracts

- Architecture and API: [architecture](architecture.md), [CLI API](cli-api.md), [generic record store](record-store.md), [model surface](api-surface-layer1.md), [affix processes](affix-process-contract.md), [sound authoring](sound-system-authoring.md), [parser surface](api-surface-hc.md), [custom fields](custom-fields.md), [writing systems](writing-system-display-contract.md).
- Proposal and Apply: [Proposal contract](proposal-contract.md), [lifecycle](proposal-lifecycle.md), [conflicts and rebase](conflicts-and-rebase.md), [applied log](applied-log.md), [review report](review-report-contract.md), [allomorph retirement](allomorph-retirement-contract.md), [human judgment](human-judgment-contract.md).
- Parser and grammar: [grammar map](hc-grammar-map.md), [feature coverage](feature-coverage.md), [Parsimony evidence](parsimony-evidence-contract.md), [Try a Word acceptance](try-a-word-acceptance.md), [corpus ingestion](corpus-ingestion.md), [parser help](parser-help/README.md), [handoff formats](handoff/).
- Testing and release: [changed-file tests](testing-changed-files.md), [slow tests](testing-slow-run.md), [agent plugin release smoke](agent-plugin-release-smoke.md), [release notes](release-notes/), [configuration](configuration.md).
- Window and media: [media boundary](media-boundary-spec.md) and the [window decisions](adr/0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md).

[ADRs](adr/README.md) hold decisions in force. [References](references/README.md) combines cited sources and condensed engineering findings in one folder; the prepared Oracle pins those records.

The `handoff/`, `release-notes/` and `references/` paths are stable integration, release and Oracle paths. Update their consumers when a change is unavoidable.

## Where new material goes

- Change a decision in its existing ADR; git keeps its history. Reserve a new number for a new topic, and do not create supersession chains.
- Track open work in a GitHub issue, not a repository plan or register.
- Put a reusable finding in a condensed file under `references/` with its source and limits.
- Never commit grammar or FieldWorks project data; [ADR 0002](adr/0002-grammar-data-never-belongs-in-motif.md) gives the rule and named exception.
- The [ADR crosswalk](adr/README.md) maps historical numbers to current decisions.
