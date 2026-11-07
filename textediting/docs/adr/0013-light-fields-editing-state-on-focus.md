# Fields are light; editing state exists only while focused

A field that is not focused only lays out and draws. Its caret, input-method connection and undo grouping are
created when it takes focus and dropped when it loses focus. The host creates fields only as they scroll into view,
as FieldWorks creates slices now. Cost then grows with what is on screen, not with record size. The prototype held
500 fields in 126 MB.

**Considered options:** one shared editing engine per view, the browser model. Rejected because FieldWorks has no
such layer today and LibreOffice Calc gets the same economy by attaching its edit engine only to the active cell.

Owner decision, 2026-11-03.
