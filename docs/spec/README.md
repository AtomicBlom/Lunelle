# Format 2 specifications

Implemented on Lunelle's `format-2` branch. Format 2 replaces format 1 outright (there are no users to migrate). Read them in this order:

1. [project.md](project.md): the project, the `.lunelle` file, entry ids, references, libraries, packing, and `about`.
2. [overrides.md](overrides.md): deriving patches and kits in layers (library, project, song), by keyed paths.
3. [cues.md](cues.md): cue files, namespaces, songs importing cues, and cues in code.
4. [patterns.md](patterns.md): reusable beats, lines and strums, their transforms, and detach/extract.
5. [exports.md](exports.md): scripted, deterministic renders of a song, loop points, and albums.

The plan these come from is LunelleStudio's `docs/plan.md`.

## As built

Where the implementation settled what the drafts left open, or differs from them:

- **Format strings**: a manifest is `lunelle/2`, a song `lunelle-song/3`, a patch `lunelle-patch/2`, a kit
  `lunelle-kit/1`, a pattern `lunelle-pattern/1`, a cue file `lunelle-cues/1`.
- **Host libraries** are found by the name the manifest lists them by; Lunelle provides `lunelle` (`BuiltInLibrary`).
- **A library's cue** is known at run time as `<the library's own name>:<id>`; a project's own cue by its id.
- **Patterns**: `octave` transforms a line only (a strum has no octave). A pattern made of patterns is written into a
  part as a `"sequence"` of parts (each with its `"times"`), whose items share one kind and meter, rather than one long
  notation string; `"velocity"` is a part option any part may carry (`Part.Velocity`), and `Part.Sequence` plays a
  sequence.
- **Exports**: an event at a section's start applies on its first frame, found by asking the player which section comes
  next as one ends; ramps move every 64 frames. After a `fade`, no tail is rendered unless one is asked for.
- **The generator** reads a library by path only when the project names its folder (`<LunelleLibrary Include="…" />`),
  since it sees only additional files. A sampled kit stays the game's own C# in a project with code; a kit of patches
  is generated.
- **Saving a derived entry from an editor** writes what differs from its base (`OverrideDiff`), its sets pinned.

