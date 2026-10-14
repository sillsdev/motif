# ADR 0063: A fixed grid adapts to window width

Motif keeps navigation visible and gives each panel a predictable place. On narrow screens the side panel and Inspector step aside, and their visible buttons let people reopen them when needed. When details need the page’s space, Back to page returns to the same work.

Status: accepted. Builds on ADR 0046 (pages, not stages) and ADR 0066 (one compact scale).

## Decision

1. Use an Avalonia Grid for the H frame: icon rail, optional page-owned side panel, working page and Inspector. A slim title bar and status bar remain outside the page. Columns have fixed widths from compact component tokens; there are no splitters, movable panes or floating windows.
2. The rail opens one page at a time. Pages supply optional side-panel content, Inspector content and a pinned Inspector footer through `IWorkspacePaneProvider`. The shell owns placement; the page owns its controls and their meaning. Controls detach on navigation so each has one visual parent.
3. Side-panel and Inspector visibility follows the zoom-adjusted window width. A person may hide or reopen a panel for this session; crossing that panel's width threshold clears the override and applies the width rule again. Resizing within the same range preserves the temporary choice. Both panes fit only when their fixed widths leave at least 320 DIP for the working page, as defined by `Component.Shell.WorkingPageMinWidth`. Otherwise, reopening one temporarily closes the other. A resize that makes an open pair too wide hides the side panel without discarding its override; widening within the range restores it. When the rail, Inspector and minimum working-page width cannot fit together, explicitly opening the Inspector uses the working area and temporarily hides the page. Its header shows Back to page; the page controls and model remain attached. Resizing to a width that fits restores the side-by-side layout. Navigating to another page reveals that destination while retaining the inspected subject; the Inspector strip can reopen it.
4. Pane layout is neither configurable nor persisted. There is no window-layout store or Reset layout command. Existing window bounds and display preferences keep their own contracts.
5. At 640 DIP and 100% zoom the collapsed frame leaves at least 560 DIP for the page. At 150% zoom every control must remain reachable by reflow or scrolling at the actual available width, verified by the size-matrix check.
6. A page that supplies only pinned actions retains the shared object Inspector. Hiding the Inspector retains its subject; its labelled edge strip reopens it. Pinned actions move below the page when the Inspector collapses so they remain visible. Closing an object opened from a narrow working page restores the preceding pane choices before focus returns to its origin. An object opened from page details returns to those details and the same footer.

## Rejected approaches

Dock 12.1.0.6 overflowed Avalonia's DescendantSelector in the headless window probe with both DockFluentTheme and DockSimpleTheme, even with empty pane contents and Motif's Dock styles removed. Removing the Dock theme let the window show but left it without a document template. H gives panes fixed places and does not require movement; Motif adds no Dock dependency.

Resizable and remembered split layouts add choices and storage without helping people find their way. The owner chose fixed widths and automatic collapse as the simpler implementation of H's intent: nobody gets lost, and everything needed is visible, simply.

## Validation

`HFrameTests` pins title, rail and status geometry, pane detach/hide/reopen, fixed widths and threshold crossings. `PaneWidthBudgetTests` pins explicit reopening at intermediate physical widths and both zoom levels. `FocusedInspectorFrameTests` pins the full working-area view, native return action and preceding pane choice at narrow widths. Frame captures cover light and dark themes. The size-matrix checks cover narrow windows and zoom; native Windows caption and DPI behavior require Windows validation.
