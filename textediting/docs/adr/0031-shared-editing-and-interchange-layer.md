# SIL.TextEditing is the shared editing and interchange layer of FieldWorks and Motif

SIL.TextEditing owns every piece of code that both FieldWorks and Motif can use. It is no longer only a text field. It
also carries the Markdown interchange format, record export and import, and the language services for notation boxes.

**The rule:** the test for a piece of code is "would both hosts call it?", not "is it UI" or "does it need liblcm". Shared
code is split into projects by dependency, so each host takes only what it needs:

| Project | Holds | References |
|---|---|---|
| `SIL.TextEditing` | The run model, the editor, the spelling scanner and the language-service contracts. UI-free. | Nothing |
| `SIL.TextEditing.Avalonia` | `RichTextField` and everything that touches Avalonia. | Avalonia, libpalaso |
| `SIL.TextEditing.Markdown` | FieldWorks Markdown, the YAML record frame, and schema-driven diff and shape checks. The schema is data. | Markdig; no liblcm; AOT-clean |
| `SIL.TextEditing.LCModel` | The `ITsString` adapter (ADR 0016). | `SIL.LCModel.Core` |
| `SIL.TextEditing.Project` (planned) | Project paths, the record schema built from liblcm metadata at run time, field notations, record export and import to a neutral change list, and liblcm-backed language services. | `SIL.LCModel` |

**Stays in the hosts:** the host shell and workflow. In FieldWorks that is the DataTree, windows and the unit of work. In
Motif it is Proposals, MCP, the GDR template and checker, and the AI-authoring profile.

**Consequences:**
- This widens ADR 0001: the library is the shared layer of both programs, not only FieldWorks' field.
- This supersedes ADR 0011's split, which kept everything that needs a project in Motif. Project-aware code now lives in
  `SIL.TextEditing.Project`. ADR 0011's Markdown core and ADR 0028's format stand.
- `SIL.LCModel` may be referenced by `SIL.TextEditing.Project` at FieldWorks' pin; hosts may float up. AGENTS.md and
  ADR 0016 say so.
- `SIL.TextEditing.Project` is not trimmable or AOT-marked, like the LCModel adapter. This blocks nothing: neither host
  publishes trimmed or native AOT, and Motif's GUI already loads liblcm.
- The project is not created by this ADR.
- The library stays MIT and clean-room until the repositories merge (ADR 0010).

**Owner decisions** (2026-11-05, from the editing-surfaces scoping report):
- **Q51′:** the library owns everything both hosts would use, split by dependency.
- **Q53:** it is the shared editing and interchange layer; this ADR widens ADR 0001 and supersedes ADR 0011's split. Work
  runs in two streams: records, and boxes (text, then text with a language service, then chips, then cells).
- **Q54′:** the split is by dependency, unchanged by Motif already loading liblcm.
- **Q55′:** environments and other notations stay text boxes with a language service and decoration chips; rule formulas
  become cells whose chips are GUID references.
- **Q56:** rule vocabulary is liblcm's `lcm-grammar`. All checking of model validity lives here; only the GDR checker and
  the AI-authoring profile stay in Motif.
- **Q57:** rule boxes show every stored shape read-only from day one; the bounded subset is editable first.
- **Q58:** one language-service interface, shaped like the spell-check host with LSP-compatible data, serves the box, a
  later VS Code adapter and agents' findings (ADR 0030).
- **Q59′:** composite boxes expose a group peer with named cells and a linear reading of the whole box.
- **Q60:** MIT and clean-room until the repositories merge.
- **Q61:** the first prototype is the environment box; record export runs in parallel.
- **Q62:** agents write partial files by default; whole records are allowed.
- **Q63:** the diff model (GUID plus field path, word-level in rich text) lives here; Motif renders it.
- **Q64:** agents' JSON change sets retire after a transition; the owner leans to one YAML/Markdown format everywhere,
  a Motif decision for later.
- **R-1:** `SIL.TextEditing.Project` may reference `SIL.LCModel` at FieldWorks' pin; hosts may float up.

**Planned additions** (owner decisions of 2026-11-05; the designs are pending, so these are follow-ups, not yet rules):
- **R-3:** Word-compatible HTML and RTF on the clipboard. This reverses ADR 0029's "No HTML or RTF"; TsString stays first
  on paste between FieldWorks fields. Design pending in research report 20.
- **R-4:** real tables in the field, stored so the core data model does not change and older FieldWorks can still read
  the text. Design pending in research report 19.

Owner decision, 2026-11-05.

Amended by ADR 0034 (2026-11-06): from the move into Motif, `SIL.LCModel` (R-1 and the consequences above) follows Motif's
`SilVersions.props`, not FieldWorks' pin. Motif is MIT, so that move is not the "repositories merge" of Q60: the library
stays MIT and clean-room (ADR 0010).
