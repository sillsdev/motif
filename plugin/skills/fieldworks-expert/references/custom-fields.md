# Custom fields

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.

The shipped route is Tools → Configure → Custom Fields from the relevant area/tool. Choose the
location (such as Entry, Sense or the applicable Notebook/Text object), name and description, then
type and the writing-system or list options that type allows. Create a custom list before a
list-reference field that uses it. Close and reopen the dialog before adding another field, as the
help recommends. Check the resulting field in the correct owner/view, using Show Hidden Fields
if needed; dictionary publication configuration is a separate step.

Custom field names do not identify a single global property across owners. The definition combines
owning class, internal name, type/cardinality, writing-system selector and, for references, the
allowed list/target. A user label can differ from the internal name. Multi-writing-system text,
single text, formatted/multiparagraph text, numbers and list references need different storage
and controls. Query the actual definition before mapping a label to a value.

FLIDs are assigned in a cache and must never be used as portable identity. Do not create a field
because a normally hidden standard field looks missing. Do not assume adding a field teaches the
parser a new grammatical property: `HCLoader` consumes a defined surface, not arbitrary custom fields.

The help reports tests with at least 100 custom fields, with limits depending on location and writing
systems; this is not an unconditional maximum or unlimited-capacity promise. Need and linguistic
judgment belong to the consultant; storage changes belong to the workflow skill.

Primary help: [add a custom field](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Tools/Custom_Fields/add_a_custom_field.htm),
[limits](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Tools/Custom_Fields/custom_fields_limits.htm).
F08 identifies the official corpus; F05 defines model/storage vocabulary. Authored paraphrase.
