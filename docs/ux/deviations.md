# Deviations from the FieldWorks UX guide

**In plain terms:** Motif's window follows the FieldWorks UX guide wherever it can. This table lists each place it
does not, why that is unavoidable, and what would end the difference. Anything not listed here is a defect
([ADR 0027](../adr/0027-the-window-follows-fieldworks.md)).

The guide is [FieldWorks pull request 1191](https://github.com/sillsdev/FieldWorks/pull/1191), `Docs/ux/README.md`,
still open at the time of writing; links below point at commit 8b57dd6f7. A deviation is necessary only when
FieldWorks' constraint cannot apply to Motif, Motif's job differs, the content is shared with the CLI or docs site, or
the guide is silent ("FW has no rule").


## Deviations

| Topic | FieldWorks rule | Motif | Necessary because | Revisit when |
| --- | --- | --- | --- | --- |
| Test framework | FW has no rule; its tests are NUnit `[AvaloniaTest]` on net48 (`Src/Common/FwAvaloniaDialogsTests`) | xUnit with Avalonia.Headless 12 | The test framework follows the runtime: FieldWorks' tests run on net48 | Motif is hosted in FieldWorks' solution; then port mechanically |
| Help, command and Walkthrough text | [Localizable text][G-loc]: all interface text comes from localizable resources | JSON and Markdown catalogs ([ADR 0029](../adr/0029-help-walkthroughs-and-the-docs-site-come-from-one-source.md)); window text itself moves to .resx ([ADR 0027](../adr/0027-the-window-follows-fieldworks.md)) | The CLI and the docs site read the same files, and FieldWorks' own help is not .resx either | The CLI and docs site stop reading the shared catalogs |
| Plural count sentences | [Localizable text][G-loc]: whole sentences with placeholders; no plural rule | Plural pattern as the .resx value for count sentences | FW has no rule on plurals, and "1 change" against "N changes" needs one | FieldWorks adopts a plural rule |
| Parse cost display | The FieldWorks UX guide has no rule on parser work, durations or plurals | Diagnostics shows recorded parser work by rule and keeps unattributed work; the health details show the recorded median only when available. Overall elapsed time is a duration, not parser work. Missing, incomplete or stale values are not presented as current measurements. | FieldWorks has no rule on these measures | FieldWorks adopts a rule for parser work and duration |
| Token names | [Theme values][G-theme]: named tokens, but FW has no naming rule | Layered Primitive, Intent and Component names ([ADR 0028](../adr/0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md)); values follow FieldWorks for shared purposes | FW has no naming rule, and renaming gains nothing | FieldWorks writes a token naming rule |
| Dark theme and high contrast | FW has no rule; its dark values are unreviewed placeholders (`Src/Common/FwAvaloniaTheme/Tokens/FwColorTokens.axaml`) | The token framework is theme-variant-ready; no fuller dark or high-contrast design yet | FW has no rule, and the work waits for FieldWorks' net10 migration | FieldWorks completes its net10 migration |
| Interface font | FW has no font rule: no UI family is set (platform default) | Source Sans 3 for interface text and Lora for display titles; linguistic text retains its writing-system fonts and fallback | FW has no rule to follow and project writing systems need their own fonts | FieldWorks adopts a font rule |
| Zoom | FW has no rule | 80 to 150 percent | FW has no rule | FieldWorks adds zoom or a zoom rule |
| Setup as an overlay | [Dialog windows][G-win]: owned windows, centred on the owner | Setup is an in-window overlay | Standalone, the host window is Motif's own | Motif is hosted in FieldWorks |
| Menu bar | [Menu structure][G-menu]: File, Send/Receive, Edit, View, Data, Insert, Format, Tools, Window, Help; areas add no top-level menus | Standalone: File (Select New, Open Recent, Configure, Exit), Edit (Undo, Redo, Copy), View (zoom), Tools (Options, Writing Systems and Fonts), Help (Help Topics, Keyboard Shortcuts, Report a Problem); the icon rail stands for the area and tool list; below 760 logical pixels the menus collapse behind a ≡ button on the same 34-DIP row, which keeps the project chip (status dot, name) and the ⟳ Refresh icon; the chip opens a project card holding the status, the texts to parse and the project commands | Motif has no Send/Receive, Data, Insert, Format or Window commands, and is a separate process | Motif is hosted: its commands join FieldWorks' menus |
| Motif-only shortcuts | [Keyboard shortcuts][G-keys]: keep WinForms keys; no new meaning for a key used in the area | Motif keeps its own staging keys | Motif is a separate area and they collide with nothing in FieldWorks Tools | Before hosting: check single-letter keys against interlinear |
| Problem report buttons | [Dialog buttons][G-btn]: buttons that act without closing go next to their content or bottom left; the closing group is confirming, alternatives, Cancel; Enter activates the confirming button | Copy Report sits bottom left. Open Issue Form stays in the bottom-right group beside Close, though the window stays open after it. Enter and Esc both map to Close; Open Issue Form is not the default button | Opening a browser on Enter would be too surprising, so the dialog's information-dialog form (Close on Enter and Esc) is used; Open Issue Form is the main action but must be chosen on purpose | The owner prefers the strict rule: then Open Issue Form moves to the bottom left too |
| Skip for Now | [Dialog buttons][G-btn]: only non-closing buttons sit bottom left | Skip for Now closes Setup (and records that it was skipped) yet sits bottom left, apart from Back, Next and Cancel | It is Motif's optional, persisted opt-out of a wizard step, not an alternative way to confirm; Cancel is the ordinary closing button and does not record a skip | FieldWorks writes a rule for optional wizard steps that finish early |
| Back before Next | [Dialog buttons][G-btn]: the confirming button comes first in the closing group | Setup shows Back, then Next or Finish, then Cancel | Back is wizard navigation, not a closing button, and the WinForms wizard convention puts it directly left of Next | FieldWorks writes a wizard button rule |
| More options expander | The guide asks for one "More options" expander that opens when a value is not its default and remembers whether it was open; the guide marks this rule as needing team consensus, and which settings count as advanced is not settled | Settings has several expanders, each named for what it holds and each remembering whether it was open: Details, All shortcuts and What a Report Includes. Setup has none | A single "More options" would hide unrelated things under one vague name, and without agreement on which settings are advanced Motif would be guessing; a name that says what is inside lets a person find it without opening it. Owner decision | FieldWorks settles which settings are advanced and ratifies the rule, or Setup gains choices that most people should not need |
| Crowdin | FW has no rule; its `crowdin.json` maps `Src/**/*.resx` | No Crowdin project; English only | Nothing to translate yet | The codebases merge |

## Not deviations

These differ from FieldWorks on the surface and are recorded so nobody reopens them.

| Topic | FieldWorks | Motif | Why it is not a deviation |
| --- | --- | --- | --- |
| Avalonia 12 and net10 | Avalonia 11.3.x, Semi 11.3.x on net48 (`Directory.Packages.props`, lines 183-184) | Avalonia 12.1.2, Semi 12.1.0, net10.0 | Same path, reached earlier: FieldWorks is on 11 only because of .NET Framework 4.8 and will move to Avalonia 12 and net10 before the codebases merge ([ADR 0027](../adr/0027-the-window-follows-fieldworks.md)) |
| Language change | [Localizable text][G-loc] and a restart to change language | Restart | Matches FieldWorks; the earlier live-switching design is superseded |
| Apply is one-way | [Saving and undo][G-undo]: one undo per field; [Bulk Edit][G-bulk]: preview, marked not saved, one named step | Apply is one-way by design: Motif has no unapply. Undo exists only before Apply | Proposals, Dry Run and Trials exist to test before Apply, and FieldWorks' own undo covers only a subset of its commands ([ADR 0017](../adr/0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md), [ADR 0017](../adr/0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md)) |
| Detail-view section commands | [Sections and expansion][G-sections]: section commands appear on the section header; the guide's decisions name a Field options link for detail views | Motif has no detail-view section commands | The rule has no commands to govern in Motif; apply it if such commands are added |

[G-loc]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L151
[G-btn]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L188
[G-theme]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L142
[G-win]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L201
[G-menu]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L75
[G-keys]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L89
[G-undo]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L431
[G-bulk]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L324
[G-sections]: https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md#L399
