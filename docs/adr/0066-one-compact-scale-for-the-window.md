# ADR 0066: One compact scale for the window

Motif shows more words and grammar evidence in the same window by using compact spacing and type throughout. A single shared scale makes pages feel consistent and keeps the room needed for navigation and actions predictable.

Status: accepted. Complements ADR 0046 (pages, not stages) and ADR 0053 (word presentation owns layout and text metrics).

## Context

Large insets, rounded panels and repeated card borders consumed space before a page displayed any evidence. Changing individual pages would leave inconsistent scales and require the same correction in many places. Supporting both comfortable and compact modes would double the layout and capture combinations while leaving the original reachability problem available.

## Decision

1. Use one compact default scale. Ordinary UI text is 13 DIP; page titles are 16 DIP. Section headings have an explicit semibold weight. Spacing uses the shared 2, 4, 6, 8 and 12 DIP steps, with an 8 DIP page inset.
2. Structural panels have straight edges and no shadows. A section is a heading and content separated where needed by a divider. Editable controls, meaningful selection boundaries, opinion marks and outcome cues keep their own edges.
3. Small controls are 26 DIP high; default controls are 28 DIP high. Compactness must preserve focus visibility, readable labels and a visible selected state under the pointer or keyboard focus.
4. Keep the Primitive → Intent → Component token architecture. Primitive keys name values, Intent keys name shared meaning, and component keys name local roles. Views consume Intent or Component keys; theme-sensitive values remain dynamic.
5. Linguistic font sizes and interlinear metrics follow the writing-system resolver and the word-presentation module. The UI scale does not overwrite project language typography.
6. Popups and tooltips retain a visible boundary and their raised shadow where required for separation. Their content remains constrained to the available window area at the selected zoom.
7. Page content is either a virtualised list or a vertically scrolling body. Navigation, status and pinned actions remain outside that body. Nested scrolling is reserved for content with an independent viewport, such as a virtualised list.

## Consequences

Shared token changes affect many pages, so validation includes state cues and captures at several widths, heights, zoom levels and themes. A smaller control is acceptable only when its text, actions and keyboard focus remain usable.

Flat sections and shared scrolling rules require page migrations as well as token changes. This decision specifies the target; a token change alone does not establish that every page has reached it.

The density contracts are exercised by `DesignTokenTests`, `ComponentStyleTests` and `InteractionCueTests`. Page reachability and final screenshots remain separate evidence, including Windows caption insets and platform font metrics.
