# Deviations from the FieldWorks UX guide

**In plain terms:** Motif's window follows the FieldWorks UX guide wherever it can. This table lists each place it
does not, why that is unavoidable, and what would end the difference. Anything not listed here is a defect
([ADR 0070](../adr/0070-the-window-follows-the-fieldworks-ux-guide.md)).

The guide is [FieldWorks pull request 1191](https://github.com/sillsdev/FieldWorks/pull/1191), `Docs/ux/README.md`,
still open at the time of writing. "Section" below means a section of that file. A deviation is necessary only when
FieldWorks' constraint cannot apply to Motif, Motif's job differs, the content is shared with the CLI or docs site, or
the guide is silent.

| Topic | FieldWorks rule | Motif | Necessary because | Revisit when |
| --- | --- | --- | --- | --- |
| Avalonia and Semi version | Avalonia 11.3.x, Semi 11.3.x, net48 in-process ([ADR 0071](../adr/0071-motif-is-ahead-on-avalonia-12-and-net10.md)) | Avalonia 12.1.2, Semi 12.1.0, net10.0: ahead on the same path, not a different direction | FieldWorks is on 11 only because of .NET Framework 4.8; it will move to Avalonia 12 and net10 before the codebases merge | FieldWorks completes its net10 move |
| Test framework | NUnit `[AvaloniaTest]` headless | xUnit with Avalonia.Headless 12 | Follows the runtime: FieldWorks' tests run on net48 | Hosting; a mechanical port |
| Help, command and Walkthrough text | Interface text in .resx (section "Localizable text") | JSON and Markdown catalogs ([ADR 0047](../adr/0047-generated-help-walkthroughs-and-docs-site.md)); window text itself moves to .resx ([ADR 0072](../adr/0072-window-text-lives-in-resx-help-content-stays-json.md)) | The CLI and the docs site read the same files; FieldWorks' help is not .resx either | Never, unless the CLI and site stop sharing them |
| Plural count sentences | Whole sentences with `{0}` placeholders only (section "Localizable text") | Plural pattern as the .resx value for count sentences | The guide is silent on plurals, and "1 change" against "N changes" needs them | FieldWorks adopts a plural rule |
| Token names | Flat FieldWorks names, no written naming rule | Layered Primitive, Intent and Component names ([ADR 0074](../adr/0074-tokens-map-onto-fwavaloniatheme-and-never-override-semi.md)); values follow FieldWorks for shared purposes | The guide is silent on names; renaming gains nothing | A FieldWorks token naming rule is written |
| Dark theme and high contrast | Silent; FieldWorks' dark theme is placeholders | Light and Dark token sets are ready; fuller dark and high-contrast work is not designed | The guide is silent, and the work waits for FieldWorks' net10 migration | After FieldWorks' net10 migration |
| Interface font and size | Silent on family; 11 px interface size marked "Check" | Open: pending owner decision. Not decided here | The guide sets no family and no settled size | Owner decision on research into FieldWorks' Avalonia font |
| Zoom | Silent | 80 to 150 percent | The guide is silent | FieldWorks adds zoom |
| Setup as an overlay | Dialogs are owned windows, centred on the owner (section "Dialog windows") | Setup is an in-window overlay | Standalone, the host window is Motif's own | Hosting |
| Menu bar | Main menu order File, Send/Receive, Edit, View, Data, Insert, Format, Tools, Window, Help; areas add no top-level menus (section "Menu structure") | Standalone: File, Edit, View, Tools, Help only; icon rail stands for the area and tool list | Motif has no Send/Receive, Data, Insert, Format or Window commands, and is a separate process | Hosting: Motif's commands join FieldWorks' menus |
| Motif-only shortcuts | Keep WinForms shortcuts; no new meaning for a key used in the area (section "Keyboard shortcuts") | Motif keeps its own staging keys | Motif is a separate area, and they collide with nothing in FieldWorks Tools | Hosting: check single-letter keys against interlinear |
| Apply is not a FieldWorks undo step | Detail views have one undo per field; Bulk Edit applies in one step | Apply is one-way by design; Undo exists only before Apply, within a Draft | Not a gap: Motif's Proposals, Dry Run and Trials exist to test before Apply, and FieldWorks' own undo covers only a subset of its commands ([ADR 0016](../adr/0016-scratch-cache-copy-not-undo.md), [ADR 0005](../adr/0005-schema-operations-non-undoable-uow.md)) | Not planned |
| Crowdin | FieldWorks' project translates its .resx | No Crowdin project; English only | Nothing to translate yet | The codebases merge |
| Language change | Restart | Restart | Matches (not a deviation); the earlier live-switching design is superseded | n/a |
