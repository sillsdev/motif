# Find, context menus, leaving a field and paging go through host hooks

These cross the line between one text and the window around it, so the host owns the user interface and the field
exposes hooks:
- **Find and replace:** the host's dialog asks the field to search, select and highlight the current match.
- **Context menu:** the host builds the menu from what the field reports: the clicked word, its writing system,
  spelling suggestions, and any link or embedded object.
- **Leaving the field:** Tab, Shift+Tab, Ctrl+Tab, and arrows at the field's edge raise an event with a direction; Tab and its variants do so only when they cannot move to another editable place in the field (Ctrl+Tab goes to the last place, Ctrl+Shift+Tab to the first). The
  host moves focus, as DataTree does now.
- **Paging and scrolling:** PageUp, PageDown and auto-scroll while dragging work inside the field. Scrolling the whole
  view belongs to the host.

Owner decision, 2026-11-03.
