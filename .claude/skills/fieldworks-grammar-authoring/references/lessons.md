# Lessons from real grammar work

Each lesson holds for any language. Check them when a grammar edit does nothing, loses words or doubles
readings. Examples are SYNTHETIC EXAMPLE, loosely Bantu-like; they are not project data.

## Alternates

- **Affix allomorphs are disjunctive.** HermitCrab stops at the first affix allomorph that has no
  environment and no required syntactic features, so a later allomorph of that affix is never reached and
  an edit to it is inert. Before adding an allomorph, list the affix's allomorphs in order and check that no
  earlier one without an environment already matches. Example: a passive `-w` allomorph with no
  environment listed before `-lew` hides `-lew`. Motif does not warn yet (sillsdev/PanGloss#469).
- **Stem alternates are not ordered this way.** A lane saw 15 new stem alternates gain nothing, even with no
  environment; the cause is unconfirmed. Do not assume shadowing there; diagnose with a Trial on one word.
- **An alternate with an environment blocks the unconditioned form where that environment holds.** To keep
  the original form there, give it its own alternate carrying the same environment. Example: adding
  an alternate `ve` with environment `w _ #` makes the entry's plain form `va` fail after `w` until `va`
  gets the same environment.

## Entries and classes

- **A stem with no inflection class may parse only after a derivational affix**, with the bare stem
  failing. Give it a class, or confirm with a Trial that the bare stem parses.
- **A derivation can follow an inflectional slot only if the stratum allows it.** In an Unordered stratum
  morphological rules re-apply after a template; in a Linear stratum they do not. Check the stratum's rule
  order before relying on either.
- **One morpheme, one entry.** Add alternates or extra MSAs to it. A class-free twin of a class-restricted
  entry makes every word with that morpheme parse twice. Example: a second `-ni` entry without the noun
  class list doubles every word that takes `-ni`.
- **Moving a stem to another class can strand its features.** Check that its inflection features (gender,
  for example) still fit the new class's affixes.

## Retire or block

- **Retire a lexicalised derived stem** only when both hold:
  1. the derivation is semantically right;
  2. the grammar can build every attested form of it.
- **Repeated suffixes can't be built yet.** Motif never sets HermitCrab's `multipleApplication` (default 1),
  so a derivational affix applies once, and templates refuse a repeated slot (sillsdev/PanGloss#468,
  sillsdev/motif#436). Example: a causative applied twice (`-ish-ish-`) still needs its lexicalised stem.
- **Before retiring, scan the corpus forms of the root** for chains the grammar can't build: repeated
  suffixes, forms after a passive or auxiliary, stems with their own tense alternates.
- **Otherwise keep the stem and block the nonsense chain** with an ad hoc prohibition.

## Measuring a change

- **Run a sibling check, not just the gold words.** Parse corpus words that share the touched morphemes and
  fail on any that stop parsing. A retirement can pass on every gold word and still lose dozens of siblings.
- **Count words that gain extra readings**, not only words that change. A change can report few changed
  words and still double hundreds.
- The gate that does this lives outside Motif for now; its upstreaming is sillsdev/motif#435.
- **Don't impose an affix order untested.** Try both orders against the corpus first. Example: if
  applicative and stative can each follow the other in the corpus, a fixed order loses words.

## Running

- **One parse per project at a time.** A transient `database is locked` (`StoreInconsistent`, SQLite error
  5) most likely means retry the command, not that the grammar failed (sillsdev/motif#434).
