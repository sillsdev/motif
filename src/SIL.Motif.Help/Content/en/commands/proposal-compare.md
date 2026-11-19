# Compare Proposals

Run `motif proposal compare <proposalId> <proposalId> [...] --project <fwdata> --json` to see two to eight Proposals side by side. The MCP tool `motif_proposal_compare` takes `project`, `proposalIds`, `at` and `page`.

This compares each Proposal's own Trial before and after. It is not measured against approved analyses. Each Proposal uses its newest completed Trial and the evidence that Trial retained, so the call measures nothing, starts no Trial and applies nothing.

The summary names each Proposal and counts of forms that gained a Reading, lost a Reading, were unchanged, were not measured and stopped early. A form a Trial did not measure on both sides is counted as not measured, never as unchanged, and an incomplete Trial makes the comparison incomplete with a typed limitation. Use `--page 1` for the form rows, 100 to a page, with diverging forms first. Each row has one cell per Proposal in request order, with the Reading IDs gained and lost. Forms are joined on writing system, form key and Reading ID.

All Trials must have been measured on one Baseline. Different Baselines refuse with `proposal.compare-baseline-mismatch` and name each Baseline. `--at <digest>` requires each Trial to match that Baseline digest or its own Proposal revision, else `proposal.compare-at-mismatch`. A Proposal with no completed Trial refuses with `proposal.compare-trial-missing`.
