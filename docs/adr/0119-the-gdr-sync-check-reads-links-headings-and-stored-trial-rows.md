# 0119. The GDR sync check reads links, headings and stored Trial rows

**In plain terms:** Grammar Design Records are the page a linguist reads, so they must say what the grammar does today.
`gdr check` compares them with the grammar and warns about three things: a rule nobody wrote about, a section that points
at something deleted, and an example word that no longer parses the way its section says. It uses the Trial results that
are already stored and never runs the parser.

**Status:** accepted, 2026-11-13. Implements the check of motif#178. Builds on
[ADR 0083](0083-grammar-design-records-live-in-the-fieldworks-project.md) (GDR text lives in the project),
[ADR 0117](0117-the-hypothesis-ledger-lives-in-the-motif-store-and-the-cli-writes-it.md) (the ledger links a hypothesis to a
GDR section) and [ADR 0076](0076-grammar-data-never-belongs-in-motif.md). It adds no storage format: it reads what 0083
and the FieldWorks Markdown spec already store.

## Context

The owner decided that GDRs are always in sync with the FieldWorks rules, and that a Proposal changing a rule changes its
GDR in the same Proposal. The `grammar-discovery-loop` skill enforces that by hand. Nothing computed it.

Today the project holds the records as the "Grammar Design Records" possibility list (name, abbreviation, abstract, an
StText Discussion). `gdr list` and `gdr show` read them from the Baseline. Agents write them through the generic rich-text
operations (ADR 0095); there is no GDR-specific operation. FieldWorks Markdown names a link to any project object by GUID,
so a record's references to the grammar are link runs in its paragraphs. A section is a `Heading` paragraph and what
follows it. The example table is tab-separated row paragraphs.

## Decision

1. **The check runs on the current Baseline's project copy** (as `gdr list` does), with the Motif store beside it. It is a
   read: no unit of work, no parse. Verb: `gdr check --project <fwdata> [--json]`, Developer surface, read-only.
2. **A citation is a link.** Only link runs count as a GDR naming an object; prose that spells a rule's name does not. The
   spec already says a GDR is linked to objects by the references in its body.
3. **Which grammar objects need a section** (one counted object each; grouped in the report by kind):

   | Kind | Why it needs a section |
   |---|---|
   | Phonological rule | a rule with ordering and exceptions (GDR topics 8, 9) |
   | Compound rule | topic 21 |
   | Ad hoc rule | co-occurrence prohibitions (topic 17) |
   | Exception feature | exceptions (topics 9, 22) |
   | Affix template | affix positions (topic 17); its slots are covered by it |
   | Stem name | allomorph selection (topic 18) |
   | Inflection class | topic 12 |
   | Affix process | non-linear forms (topic 19) |

   **Not counted:** phonemes, natural classes, environments, features, categories, strata, morph types, and lexical entries
   with ordinary allomorphs. They are the inventory the rules use, described together by topics 6, 10 and 11, and a section
   per phoneme would bury the sections that matter. A kind a project does not hold costs nothing.
4. **One section covers many objects.** An object is covered when any record links it or links an object that owns it: an
   entry covers its affix processes, a category covers its templates, stem names and inflection classes. Several sections
   may cover the same object. The report is per kind, not per object, with up to ten names.
5. **(b) A cited object that is gone** is a link whose GUID is no longer in the project. It is reported with the record and
   section heading. A link to another project is not looked up.
6. **(c) Examples.** Under a heading that starts with `Examples`, every vernacular-writing-system word is an example: a
   token between tabs and spaces, with surrounding punctuation trimmed, ignoring tokens that start or end with a hyphen or
   equals sign (a morpheme, not a word). Under `False parses` or `Does not parse`, the same words are claimed *not* to
   parse. Analysis-language text such as a gloss is never an example. The claims checked are:
   - a parsing example has at least one analysis;
   - the morphemes (entries, forms, morphosyntactic analyses) linked in the same paragraph appear in some analysis of the
     word, by their own GUID or their entry's;
   - a false parse has no analysis.

   Phonological rules, categories and templates linked beside an example are not claims the stored rows can test, because an
   analysis records morphemes, not rules.
7. **The evidence is stored rows.** The newest conclusive outcome (`analysed` or `no-analysis`) of each word among the
   newest 25 word Assessments of kind ParseTime, Correctness or ObjectTiming, which is what a Trial or Assessment writes. A
   timed-out, capped or skipped word is not conclusive, so an older row may answer. A word with no conclusive row is
   reported as unverified, which asks the next Trial to include it. Nothing is parsed, so the check never adds a parse.
8. **The ledger.** A *passed* hypothesis that names a GDR and section (ADR 0117) must find that GDR (by `GDR-n`) and a
   heading of that name, or the GDR's own title, or `Abstract`. Otherwise it is reported.
9. **Every finding is a warning.** `InSync` is true only with no finding. The loop treats a warning as incomplete.
10. **The Trial review is the hook, not Finalize.** Finalize has no project copy of the Proposal's result, and running the
    check against the Baseline would report the grammar the Proposal replaces. A Trial has the post-Proposal scratch
    project, so the Trial runs the check on it once, on the lane, right after the Dry Run, and keeps the response in its
    review ledger. The review report's summary carries it as `GdrSync` and the text report prints each finding as an
    advisory line. It never holds Apply and never fails the Trial; a check that cannot run leaves `GdrSync` absent.
    The example evidence is the Trial and Assessment rows stored before this Trial, because the Trial's own rows do not
    exist yet; an example the Trial just changed shows at its next Trial. The skill's completion rule already treats any
    warning as incomplete. The Finalize warning is not built.

## Performance

Owner rule: at most one second added, O(rules + sections + examples). One pass over the grammar objects (the rules, the
affix processes among the forms), one over the records' paragraphs and runs, one batched lookup of the example words
through the `(AssessmentId, Word)` index for at most 25 Assessments. The check never scans the ledger text or the corpus. The
Baseline copy's open time is reported separately from the check.
