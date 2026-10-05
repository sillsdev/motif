# Show writing systems

`motif writing-systems --project <fwdata> [--json]` reads the current [Baseline](term:baseline)'s writing systems without opening the FieldWorks project. Before the first capture, it reports that no Baseline exists.

Each current vernacular and analysis list keeps FieldWorks' order and default. The response includes the id, name, abbreviation, requested default font family, verbatim feature string, direction, and each style's effective font, features and point size. A writing system in both lists appears once per membership; positions in JSON count from zero.

The command includes every project style; a missing style uses that writing system's effective Normal settings. Normal without a size uses FieldWorks' 10-point default. Sizes contain no window units or zoom.

For which FieldWorks style each part of the window uses and where to change its writing-system settings, see [Writing systems in the window](guide:writing-systems).

The font name is the project's request. The command reports saved settings and does not inspect installed fonts.
