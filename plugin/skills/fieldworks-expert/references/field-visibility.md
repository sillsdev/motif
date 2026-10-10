# Three visibility mechanisms

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.

## Detail fields

| Layout attribute | Show Hidden Fields off | Show Hidden Fields on |
|---|---|---|
| `always` (also the default) | Process the part | Process the part |
| `ifdata` | Process conditionally on data | Process without that empty-data suppression |
| `never` | Skip the part | Process the part |

The released `DataTree` starts with `visibility = "always"` and reads the part's attribute only
when `m_fShowAllFields` is false. Thus `never` is not an absolute ban. The control still resolves
the object's actual class and inherited part; other conditions and absent owning children can
prevent a field from appearing. Show Hidden Fields does not expand collapsed groups. The setting
is recorded per tool, so changing tools can change what is shown.

For a person: View → Show Hidden Fields, or the information-bar checkbox. For an individual field,
open its label menu (also available by right-click), then Field Visibility. Labels correspond to
normally visible, normally hidden unless non-empty, and normally hidden. Consult the topic for
exact localized wording; its English “Normally visibility” text is a help typo, not a new mode.

## Browse columns

A column's `visibility="menu"` controls its availability through column configuration; it is not
`DataTree`'s `ifdata`. Configure Columns on a grid selects columns for that tool. A field can be
present in the entry pane yet absent from the grid, or a grid cell can be read-only while the entry
field is editable. Follow the specific column's layout and edit configuration.

## Publication display

Dictionary configuration and publication selection determine what the reader sees in output.
They are separate from an editor field's visibility and grid-column selection. “Publish In” is
project data, not an instruction to erase a sense. A hidden editor field can still hold content
that a dictionary configuration prints; check both mechanisms for “why does this appear in output?”

Do not equate any of these with parser reach. The parser may ignore a visible description and
consume an allomorph restriction that is currently hidden.

Primary evidence: [DataTree part processing](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/Common/Controls/DetailControls/DataTree.cs)
(`ProcessPartRef` area, attribute handling near lines 2432–2467),
[Show Hidden Fields help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Basic_Tasks/Showing_and_hiding_fields/Show_Hidden_Fields.htm),
[field visibility help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Basic_Tasks/Showing_and_hiding_fields/change_the_visibility_of_fields.htm),
and [released Lexicon Browse columns](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/DistFiles/Language%20Explorer/Configuration/Lexicon/Browse/toolConfiguration.xml).
Source records F07/F08; paraphrased here.
