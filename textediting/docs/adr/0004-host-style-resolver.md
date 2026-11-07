# Styles are resolved by the host

FieldWorks keeps named paragraph and character styles in the project's stylesheet, with overrides for each writing
system. The field asks a resolver supplied by the host for the effective formatting of (style name, writing system)
and draws what comes back. It never reads a stylesheet or gives meaning to a style name.

**Consequences:** the playground supplies a small resolver of its own. Style lists, the styles combo and style
editing belong to the host.

Owner decision, 2026-11-03.
