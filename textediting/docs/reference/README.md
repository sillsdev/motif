# Reference

How SIL.TextEditing works, for developers who host it or change it. The repository began as the native-editor prototype; what it still lacks is in [Known limitations](limitations.md) and the README's [Roadmap](../../README.md#roadmap). The formats it reads and writes are in
[docs/formats](../formats/README.md); the decisions behind it are in [docs/adr](../adr).

| Page | What it covers |
|---|---|
| [Projects and tests](projects.md) | Every project in detail, the test projects, the opt-in real-clipboard tests, the build environment. |
| [The run model](run-model.md) | `Run`, `Paragraph`, `RunList`, the property bag, change notices, threading, work counters. |
| [Editing, keyboards and host hooks](editing.md) | FieldWorks editing semantics, selection and the word rule, block features, keyboard switching, find, context menu, scrolling. |
| [Clipboard in the field](clipboard.md) | The field's side of the clipboard forms and the FieldWorks interop hooks. |
| [Spell checking and the language service](spelling-and-language-service.md) | Dictionaries, squiggles, background checking, what a host calls after a change, the notation box, completion. |
| [Rendering](rendering.md) | The properties drawn, justification, line limits, trailing spaces, very long paragraphs, graphemes, colours and themes. |
| [Two Avalonia versions](avalonia-versions.md) | Avalonia 11.3.17 and 12.1.2 from one source tree, the per-version differences, the Avalonia faults the layout works around. |
| [The LCModel adapter](lcmodel.md) | `TsStringConversion`, the TsString XML form, FieldWorks' own TsString wire format. |
| [The Markdown library](markdown.md) | Integrating `MarkdownConverter` and record files: host interfaces, robustness, AOT, net48 binding redirects, tests. |
| [Known limitations](limitations.md) | Accessibility, input, undo. |
