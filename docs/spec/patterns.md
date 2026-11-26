# Spec: patterns (format 2)

Status: **draft**. Builds on [project.md](project.md).

## 1. What a pattern is

A pattern is **a part's music, without its track or its condition**: a beat, a line or a strum written once and
used by any number of parts, in any number of songs, on any kit or patch that fits. A four-on-the-floor plays on
every kit with a `kick` and a `hat-closed`, a strum pattern on every guitar, and a bass line follows whatever chords
it is placed over.

The notations are format 1's ([synth.md](../synth.md), "The notations"), unchanged. A pattern only moves a part's
content into an entry of its own.

## 2. Pattern files

`Patterns/**/*.pattern.json`. The id is the path (project.md §2.1), such as `drums/four-on-the-floor`.

```json
{
  "format": "lunelle-pattern/1",
  "kind": "beat",
  "about": "Kick on every beat, hats on the offbeats",
  "step": "sixteenths",
  "rows": {
    "kick":       "x... x... x... x...",
    "hat-closed": "..x. ..x. ..x. ..x."
  }
}
```

```json
{ "format": "lunelle-pattern/1", "kind": "strum", "step": "eighths", "notes": "D.DU .UDU", "voicing": "open" }
```

```json
{ "format": "lunelle-pattern/1", "kind": "line", "step": "eighths", "notes": "1 1 . 1 5, 1 . 1", "overChords": true }
```

| Kind | Holds | Plays on |
|---|---|---|
| `beat` | `step`, `rows` (by kit piece name) | A kit track |
| `line` | `step`, `notes`, optionally `overChords` and `octave` | A patch track |
| `strum` | `step`, `notes`, optionally `voicing` and `spread` | A patch track |

- **`step`** is the grid, written as format 1's part keys are (`"quarters"`, `"eighths"`, `"sixteenths"`,
  `"triplets"`).
- **`meter`** is optional (default `4/4`). It's what the pattern's bars are counted in.
- **A pattern is as long as its bars.** Every bar must be whole, as in format 1. A part repeats a shorter pattern
  through its section and cuts a longer one at the section's end, as it does today.
- **A line's degrees and a strum's chords are relative**, to the song's scale and the section's chords. A pattern
  carries no key.

### 2.1 Patterns made of patterns

```json
{
  "format": "lunelle-pattern/1",
  "kind": "beat",
  "about": "Three bars of the groove, then a fill",
  "sequence": [
    { "pattern": "four-on-the-floor", "times": 3 },
    { "pattern": "lunelle:drums/fill-snare-roll" }
  ]
}
```

- **A pattern has either its own content or a `sequence`, never both.**
- Each item is a pattern reference (project.md §2.2), optionally with `times` (default 1) and the transforms of §4.
- **Every item must be of the pattern's `kind` and `meter`.** The items play one after another.
- **A cycle is an error**, naming the chain. Nesting may be at most **16** deep.

## 3. Using a pattern in a part

```json
{ "track": "drums", "pattern": "game:drums/four-on-the-floor", "when": "intensity > 0.3",
  "endWith": { "pattern": "fill-a" } }
```

- **A part has either `pattern` or its own content** (`line`/`beat`/`strum` with `notes`/`rows`), never both.
- **Everything about a part's placement is unchanged**: `name`, `when`, `level`, `onPhrase`, `endWith`. `endWith`
  may be a pattern reference, or inline content as today.
- **A part with `pattern` plays exactly as the same part with the pattern's content written inline**, with the same
  transforms applied as part options (§4). This equivalence defines what a reference means. The studio's **detach**
  and **extract** (§5) rely on it.

## 4. Transforms

A reference, in a part or in a sequence item, may change how the pattern plays, without changing the pattern:

| Transform | Kinds | Does |
|---|---|---|
| `velocity` | all | Multiplies every hit's or note's velocity (0 to 2), then clamps at 1. `0.7` is a softer take of the same groove. |
| `pieces` | beat | Maps a row to another kit piece, or to `null` to leave it out: `{ "hat-closed": "ride", "crash": null }`. |
| `octave` | line, strum | As the part option: replaces the pattern's own. |
| `overChords` | line | As the part option: replaces the pattern's own. |
| `voicing`, `spread` | strum | As the part options: replace the pattern's own. |

- **Each transform is a plain value.** There are no expressions, and no edits to single notes.
- **Changing the notes means detaching** (§5).
- `swing` and `humanise` stay on the **track**, as in format 1. They aren't transforms, so a pattern feels like its
  track wherever it is used.

## 5. Detach and extract

The studio does these. The format only has to make them exact:

- **Detach** replaces `"pattern"` with the pattern's content inline, its `sequence` expanded, and the transforms
  written as the part's options. With `pieces` and `velocity`, it rewrites the rows. **The part sounds the same
  before and after** (§3).
- **Extract** makes a new pattern entry from a part's inline content, leaving the part's track, condition and
  placement options. It replaces the content with a reference. **Again, the part sounds the same.**

**Library patterns are read-only** from the project that uses them. To change one, the project extracts or detaches
its own copy. Patterns don't take part in overrides (overrides.md): a pattern is small enough to copy, and a
transform covers the common changes.

## 6. Mismatches

| Case | Is |
|---|---|
| A `beat` on a patch track, or a `line`/`strum` on a kit track | **Error**, on the part |
| A pattern's `meter` different from the song's | **Error**, on the part |
| A `sequence` mixing kinds or meters | **Error**, on the pattern |
| A beat row naming a piece the track's kit doesn't have (after `pieces`) | **Warning**, on the part. The row is silent there. A kit without a ride is ordinary, so this isn't an error. |
| `pieces` mapping a row the pattern doesn't have | **Warning**, on the part (dangling, as in overrides.md §7) |
| A reference to a missing pattern | **Error**, on the part. The part plays a rest, as code does in a project of files alone. |

The studio dims a pattern in the library browser when dropping it on the track at hand would be an error. It marks
it when the drop would give a warning.

## 7. In a project with code

The generator writes each pattern a project uses as a static member returning its `Part`:

```csharp
public static partial class Patterns
{
	public static partial class Drums
	{
		/// <summary>Kick on every beat, hats on the offbeats</summary>
		public static Part FourOnTheFloor => Beat.Sixteenths((Kit.Kick, "x... x... x... x..."), (Kit.HatClosed, "..x. ..x. ..x. ..x."));
	}
}
```

A part referencing a pattern applies its transforms in the generated code. The generated song plays what
`SongFile.Load` plays from the same files, as `MusicGeneratorTests` checks today.

## 8. Test cases

1. **Equivalence.** For every pattern kind, a part with `pattern` and a part with the same content inline render
   bit-identically. With each transform, they match the inline content rewritten by hand.
2. **Detach and extract** each leave a song's render unchanged.
3. **Sequence.** `[a ×3, b]` plays a, a, a, b, then repeats. A cycle is an error. A beat in a line sequence is an
   error.
4. **Kits.** Four-on-the-floor on two kits with `kick` and `hat-closed` plays on both. On a kit without `hat-closed`
   it warns, and only the kick plays. `pieces: { "hat-closed": "ride" }` plays the ride instead.
5. **Mismatches.** A beat on a patch track, and a 3/4 pattern in a 4/4 song, are errors.
6. **Both modes.** The generated `Patterns` members, and the parts using them, render bit-identically to the runtime
   load.

## 9. Out of scope

- Derived patterns (a pattern based on another with edits). Copy it, or use transforms.
- Transforms that edit notes (transpose a beat row, add or remove hits, reverse).
- Chord progressions as patterns. A section's chords stay the section's. This may come later as its own kind.
- Patterns that span several tracks (a whole drum, bass and keys groove as one entry).

## Open questions

- Should a part be able to start a pattern part-way through (an `offset` in steps), for a groove that comes in on
  the "and"?
- Is `times` in a sequence enough, or do sequences want the `"x3"` shorthand of a notation string?
