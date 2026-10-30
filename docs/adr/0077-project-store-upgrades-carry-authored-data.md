# 0077. Project store upgrades carry authored data

Opening a project with an older Motif store keeps its authored work while Motif rebuilds computed results. The
old store remains as a backup so the owner can recover it if an upgrade fails.

A project-store upgrade constructs the current schema in a separate file under the store's ownership and
exclusive-use locks. It copies authored data by column name, clears references to discarded evidence, verifies
the result, and replaces the original only after retaining the original as a versioned backup. Proposal
revisions that current code cannot parse remain intact and are identified in the upgrade report. A source/output
mode supports inspection without modifying the source. MCP requests cannot initiate an upgrade. Machine stores
continue to use their separate schema policy. This decision supersedes the pre-1.0 project-store refusal rule.
