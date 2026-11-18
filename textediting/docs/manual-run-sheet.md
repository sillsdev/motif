# Manual run sheet

One run on Windows, recorded by a person. Steps, expected results and the reason each needs a person are in [manual-testing.md](manual-testing.md); this
sheet only records the outcome. Leave a cell blank until you have done the step: **PASS**, **FAIL** (file an issue and put its number in Notes) or **SKIP** (say why).

Hosts: **D10** `run desktop10 -InteractiveDesktop` (Avalonia 12, .NET 10), **D48** `run desktop48 -InteractiveDesktop` (Avalonia 11.3.17, .NET Framework 4.8), **W48** `run winforms48 -InteractiveDesktop` (WinForms 4.8 form, Avalonia 11.3.17). `n/a` cells are not run.

**Machine** column: **Windows** means a real Windows machine is required (Windows IME, Keyman, Narrator, Notepad or Word, Win+Space, Windows themes, WinForms, or .NET Framework 4.8).
**Any** means the step is about the Avalonia window itself and D10 can also be run on Linux or macOS, but D48 and W48 still need Windows.

| | |
|---|---|
| Tester | |
| Date | |
| Windows version | |
| Commit (`git rev-parse --short HEAD`) | |
| Input methods and keyboards installed (Japanese IME, Chinese Pinyin, Keyman keyboard) | |
| Display scale | |
| Screen reader and version | |

| # | Machine | What | D10 | D48 | W48 | Date | Tester | Notes |
|---|---|---|---|---|---|---|---|---|
| 1.2 | Any | Pick each scenario in turn. |  |  |  | | | |
| 2.2 | Windows | Change the Windows layout with Win+Space while the caret is in an `fr` run. |  |  |  | | | |
| 3.1 | Windows | Type a sentence of English, French accents (AltGr or a French layout) and punctuation. |  |  |  | | | |
| 3.2 | Windows | Japanese IME: in the Empty scenario type `nihongo`. |  |  |  | | | |
| 3.4 | Windows | Chinese Pinyin: type `zhongwen`. |  |  |  | | | |
| 3.5 | Windows | Compose while the caret is inside Arabic text, then in the RTL paragraph. |  |  |  | | | |
| 3.7 | Windows | Move the window while a composition is open. |  |  |  | | | |
| 3.8 | Windows | In each host, with the field focused, type through the real input path (a test script,... |  |  |  | | | |
| 4.1 | Windows | Select the Keyman keyboard, type a sequence in the Empty scenario. |  |  |  | | | |
| 4.2 | Windows | Type a sequence in which a later key rewrites earlier characters (reorder, or replace... |  |  |  | | | |
| 4.3 | Windows | Move the caret into the middle of existing text and type the same sequence. |  |  |  | | | |
| 5.3 | Any | Click in the middle of an Arabic word. |  |  |  | | | |
| 5.4 | Any | Click and drag across the boundary between Arabic and English text. |  |  |  | | | |
| 5.6 | Any | Scenario Inline picture: arrow across the picture in the Arabic paragraph. |  |  |  | | | |
| 5.7 | Any | Scenario Devanagari, Tamil, Thai: arrow and Backspace through conjuncts and stacked... |  |  |  | | | |
| 5a.5 | Windows | With NFD set, copy `é` and paste it into Notepad, then check its form (for instance by... |  |  |  | | | |
| 6.4 | Windows | In the field press Left, Right, Up, Down, Home, End, Delete, Ctrl+C, Ctrl+X, Ctrl+V,... | n/a | n/a |  | | | |
| 6.5 | Windows | Click a toolbar button (Bold), then type. |  |  |  | | | |
| 6.6 | Windows | In the Many fields scenario click different fields, then Tab. | n/a | n/a |  | | | |
| 6.7 | Windows | Alt+Tab to another program and back. |  |  |  | | | |
| 6.8 | Any | D10 and D48: press Tab in the field. |  |  | n/a | | | |
| 6.9 | Windows | W48: press Tab and Shift+Tab through the two text boxes and the field and type at each... | n/a | n/a |  | | | |
| 7.1 | Windows | Start Narrator (Win+Ctrl+Enter), focus the field. |  |  |  | | | |
| 7.2 | Windows | Navigate to the toolbar buttons and the writing-system buttons. |  |  |  | | | |
| 7.3 | Windows | W48 only: look for any Avalonia content in the Narrator tree. | n/a | n/a |  | | | |
| 8.1 | Windows | Select text in Latin and Arabic that crosses the English/Arabic boundary, Copy, paste... |  |  |  | | | |
| 8.2 | Windows | Same, paste into Word. |  |  |  | | | |
| 8.3 | Windows | Type text in Notepad and in Word (with some Arabic), copy it, Paste in the field. |  |  |  | | | |
| 8.6 | Windows | Copy in one host window, paste in another (run `run desktop10 -InteractiveDesktop` and... |  |  |  | | | |
| 8.8 | Windows | From a FieldWorks build, if you have one: copy styled text out of a FieldWorks field,... |  |  |  | | | |
| 8.9 | Windows | Copy bytes that are not text (the Runs form without plain text), then Arabic text,... |  |  |  | | | |
| 9.1 | Any | Drag-resize the window quickly; scroll the Long document with the wheel and the... |  |  |  | | | |
| 9.2 | Windows | Move the window to a monitor at 150 percent (or change the scale), or set the scale to... |  |  |  | | | |
| 9.3 | Any | Long document: press Ctrl+End, type, press Ctrl+Home. |  |  |  | | | |
| 9.4 | Any | Many fields with 1000 fields (Fields picker, then pick the scenario again): scroll and... |  |  |  | | | |
| 9.5 | Any | Select all (Ctrl+A) in the Long document and press B. |  |  |  | | | |
| 9.6 | Windows | Switch Windows between light, dark and a high contrast theme (Settings, Accessibility,... |  |  |  | | | |
| 10.4 | Any | Drag from the middle of one paragraph to the middle of a later one, then drag back up... |  |  |  | | | |
| 10.5 | Any | Double-click an English word, an Arabic word, a space and a comma. |  |  |  | | | |
| 10.6 | Any | Double-click a word and, holding the second press, drag over two more words. |  |  |  | | | |
| 10.7 | Any | Triple-click in an English paragraph, an Arabic paragraph, and a wrapped one. |  |  |  | | | |
| 11.11 | Any | Drag from the middle of the field to well below its bottom edge, and above its top edge. |  |  |  | | | |
| 11.13 | Any | In Spell checking tick Slow dictionary, off the UI thread and type in the last... |  |  |  | | | |
| 12.1 | Any | Load the scenario and look at `/ [V] _ #`. |  |  |  | | | |
| 12.2 | Any | Type `/ [X] _`. |  |  |  | | | |
| 12.3 | Any | Rest the pointer on the class `[V]`, then on the red squiggle. |  |  |  | | | |
| 12.11 | Windows | Switch to dark: in Windows settings, or by running `Playground.Desktop` with the theme... |  |  |  | | | |
| 12.12 | Windows | With a Keyman keyboard or an input method active, type in the box and press Ctrl+Space. |  |  |  | | | |
| 12.13 | Windows | In the WinForms host (W48) open the popup near the bottom edge of the form. |  |  |  | | | |
| 12.14 | Windows | With a screen reader on, open the popup and move through it, and rest on a problem. |  |  |  | | | |
