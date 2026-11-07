# Screen readers get what FieldWorks gives them today

Legacy FieldWorks Views exposes only MSAA: each element's name, role, value, location and navigation. Its UI
Automation text provider is commented out (FWR-2874). The field matches that. Through Avalonia's automation peers it
reports a name set by the host, an edit role, its text as the value, its read-only state and its bounds. FieldWorks'
WinForms host needs its own small fix so that the Avalonia accessibility tree is visible at all.

A full UI Automation text pattern (caret, selection, ranges and attributes for Narrator and NVDA) is deferred
indefinitely in its own issue. Avalonia has no extension point for it. The working route is an invisible native child
window that answers screen readers, about 2-3 weeks; see the spike report in that issue.

Owner decision, 2026-11-03. Supersedes the earlier version of this ADR, which made the text pattern a gate for FieldWorks.
