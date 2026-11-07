# Trial review report contract

A Trial report is an immutable account of evidence captured for one Proposal revision, Baseline,
Trial job and declared review scope. A later report may bind later retained evidence, but it receives
its own identity and does not reinterpret an earlier report.

Summary, failures and full disclosure share the same facts. Summary records observed red flags and
separately describes incomplete searches and unavailable comparisons. No red flags means only “not
bad”, never “good”. Failures supplies bounded samples; full detail pages all retained cases, including
unchanged cases, with cursors bound to the report and selectors. Writing systems and distinct case
identities remain distinct. Named sets and their omissions are evidence, not a verdict.

Parser work is compared only for compatible complete evidence over the same scope and is presented
as a percentage change. Missing work or incompatible pins is unavailable, not zero. Diagnostic traces
are explicit requests for one case and side. A saved trace can be read without a parser; a new trace
requires the recorded grammar, parser and limits. It has its own identity and cannot change batch facts.

The CLI and MCP use the same typed report. MCP returns it under the successful result envelope and
exposes bounded resources for retained report pages and traces. The legacy Difference selector remains
separate. Report reads do not Apply or modify human judgments or the live project. Finalizing a Proposal
keeps review evidence; Defer or a terminal decision releases retained detail while preserving the summary.
