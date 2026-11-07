# Read Trial evidence

Use `motif_difference` with a Proposal and an exact report id to read a Trial report. Select `summary`, `failures` or `full`; the full view pages all retained cases and returns a continuation when more remain. Select a case to inspect its retained readings and expectations, or request an explicit trace for a diagnostic on its recorded before or after grammar.

A stored report read does not run the parser. A trace may run a diagnostic and is labelled accordingly; it does not change the saved report. An incomplete search and unavailable comparisons remain separate from observed failures. The older Difference id and category form remains available for a retained legacy Difference.
