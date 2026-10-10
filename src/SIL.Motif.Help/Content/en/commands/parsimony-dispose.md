# Stage a finding disposition

```sh
motif parsimony dispose --project language.fwdata --report <reportId> --finding <findingId> \
  --disposition keep|fix|ask|defer --record-type <portableId> --draft <name> \
  [--reason <text>] [--question <text>] [--json]
```

This command reads one finding from a stored Parsimony Report and derives its exact subject, evidence digest, evidence contract, and readable captions. The Report must belong to the project's current Baseline, and `--record-type` must be a portable ID from `parsimony record-types`. Supply `--question` only with `--disposition ask`.

The command appends the existing Notebook judgment operations to a Draft Proposal. Review them with Dry Run and Preflight, then a person uses `apply` to write the judgment to FieldWorks. When the bound Dry Run shows only these Notebook effects, Readiness does not require a Correctness Assessment because the judgment cannot change parsing. A mixed Proposal keeps the normal Assessment requirement. A pending keep does not suppress a current finding.

The same decision is never staged twice. Two dispositions are the same decision when they agree on the finding's subject, measure and evidence, the disposition, the reason and the question; the record type, and the Report and the Draft they came from, do not count.

- If the saved project already holds this decision as its current judgment, the command succeeds and stages nothing. It prints `This decision is already recorded; nothing was staged in draft '<name>'.`, and with `--json` the response's `outcome` is `already-recorded` and its operation list is empty.
- If the Draft already stages this decision, the command is refused with `parsimony.decision-already-staged`: `This decision is already staged in Draft '<name>'.`
- If the finding ID names more than one finding in the Report, the command is refused with `parsimony.finding-ambiguous` and stages nothing, because no single finding can be disposed.
